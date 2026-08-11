using System.Collections.Concurrent;
using System.IO;
using Toolbox.Core.Services;

namespace Toolbox.Plugins.Services;

/// <summary>传输方向：Upload = 手机→电脑，Download = 电脑→手机</summary>
public enum TransferDirection { Upload, Download }

/// <summary>传输状态</summary>
public enum TransferState { InProgress, Completed, Failed }

/// <summary>传输进度快照（方向/文件名/已传/总量/状态；进度事件与记录列表共用）</summary>
public sealed record TransferProgress(
    string TransferId,
    TransferDirection Direction,
    string FileName,
    long Transferred,
    long Total,
    TransferState State,
    string? Error = null);

/// <summary>待发送文件条目（PC 登记 → 手机端可见/下载；仅内存，会话级）</summary>
public sealed record SharedFileEntry(int Id, string FullPath, string Name, long Size);

/// <summary>
/// 文件传输业务服务（静态单例，风格照 RemoteControlSettings.Instance）：
/// 接收目录设置（file-transfer.json）+ 待发送文件表 + 流式落盘/读盘 + 进度事件 + 传输记录。
/// 由 RemoteControlServer 的 /api/transfer/* 路由与 FileTransferTool 面板共用。
/// </summary>
public sealed class FileTransferService
{
    private static readonly Lazy<FileTransferService> _instance = new(() => new FileTransferService());
    public static FileTransferService Instance => _instance.Value;

    /// <summary>传输分块大小（64KB）</summary>
    private const int ChunkBytes = 64 * 1024;

    /// <summary>每块读/写的空闲超时（传输中不限总时长，无数据 60s 判失败）</summary>
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);

    /// <summary>进度事件最小间隔（100ms，防高速局域网刷爆 UI 事件）</summary>
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>传输记录上限（含进行中；超出时淘汰最早已完成记录）</summary>
    private const int MaxRecords = 40;

    private readonly string _settingsPath;

    /// <summary>进度/状态变化事件（服务器连接线程触发，UI 须经 Dispatcher 订阅）</summary>
    public event Action<TransferProgress>? ProgressChanged;

    // ==================== 接收目录（file-transfer.json 持久化） ====================

    private string _saveDir;

    /// <summary>接收目录（默认 AppPaths.DataDir/Received；setter 自动建目录 + 落盘）</summary>
    public string SaveDirectory
    {
        get => _saveDir;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            _saveDir = value;
            EnsureSaveDir();
            Save();
        }
    }

    // ==================== 待发送文件表（内存，会话级） ====================

    private readonly object _filesLock = new();
    private readonly List<SharedFileEntry> _sharedFiles = new();
    private int _nextFileId = 1;

    /// <summary>待发送文件快照（手机端 /api/transfer/list 与 PC 面板共用）</summary>
    public IReadOnlyList<SharedFileEntry> SharedFiles
    {
        get { lock (_filesLock) return _sharedFiles.ToArray(); }
    }

    /// <summary>登记待发送文件；路径不存在返回 null，已登记返回原条目（按完整路径去重）</summary>
    public SharedFileEntry? AddSharedFile(string fullPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath)) return null;
            var info = new FileInfo(fullPath);
            lock (_filesLock)
            {
                var existing = _sharedFiles.FirstOrDefault(f =>
                    string.Equals(f.FullPath, info.FullName, StringComparison.OrdinalIgnoreCase));
                if (existing != null) return existing;
                var entry = new SharedFileEntry(_nextFileId++, info.FullName, info.Name, info.Length);
                _sharedFiles.Add(entry);
                return entry;
            }
        }
        catch (Exception) { return null; } // 路径非法/无权限等
    }

    /// <summary>移除待发送文件（不删磁盘文件，仅移出共享清单）</summary>
    public bool RemoveSharedFile(int id)
    {
        lock (_filesLock) return _sharedFiles.RemoveAll(f => f.Id == id) > 0;
    }

    /// <summary>清空待发送清单</summary>
    public void ClearSharedFiles()
    {
        lock (_filesLock) _sharedFiles.Clear();
    }

    /// <summary>按 id 查待发送文件（下载路由用）</summary>
    public SharedFileEntry? GetSharedFile(int id)
    {
        lock (_filesLock) return _sharedFiles.FirstOrDefault(f => f.Id == id);
    }

    // ==================== 传输记录（进行中 + 已完成，UI 列表用） ====================

    private readonly ConcurrentDictionary<string, (TransferProgress Progress, DateTime UpdatedAt)> _records = new();

    /// <summary>传输记录快照（进行中在前，其余按更新时间倒序）</summary>
    public IReadOnlyList<TransferProgress> RecentRecords => _records.Values
        .OrderByDescending(r => r.Progress.State == TransferState.InProgress)
        .ThenByDescending(r => r.UpdatedAt)
        .Select(r => r.Progress)
        .ToArray();

    // ==================== 上传（手机→电脑，流式落盘） ====================

    /// <summary>
    /// 流式接收上传：64KB 分块从 <paramref name="input"/> 读 <paramref name="length"/> 字节直写磁盘（不落内存）。
    /// 文件名经净化，重名自动编号；磁盘空间预检。同步阻塞（调用方是独立连接线程）。
    /// </summary>
    /// <returns>（成功, 落盘文件名, 错误信息）</returns>
    public (bool Ok, string SavedName, string? Error) ReceiveUpload(Stream input, long length, string rawFileName)
    {
        var safeName = SanitizeFileName(rawFileName);
        var transferId = Guid.NewGuid().ToString("N");
        string? tempPath = null;
        try
        {
            EnsureSaveDir();

            // 磁盘空间预检
            var root = Path.GetPathRoot(Path.GetFullPath(_saveDir));
            if (!string.IsNullOrEmpty(root))
            {
                var free = new DriveInfo(root).AvailableFreeSpace;
                if (length > free)
                {
                    Report(transferId, TransferDirection.Upload, safeName, 0, length, TransferState.Failed, "磁盘剩余空间不足");
                    return (false, safeName, "磁盘剩余空间不足");
                }
            }

            tempPath = UniquePath(Path.Combine(_saveDir, safeName));
            var finalName = Path.GetFileName(tempPath);

            Report(transferId, TransferDirection.Upload, finalName, 0, length, TransferState.InProgress);

            var buffer = new byte[ChunkBytes];
            long remaining = length;
            var lastReport = DateTime.MinValue;
            using (var file = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, ChunkBytes))
            {
                while (remaining > 0)
                {
                    int read;
                    using (var cts = new CancellationTokenSource(IdleTimeout))
                        read = input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cts.Token)
                                    .GetAwaiter().GetResult();
                    if (read == 0) throw new EndOfStreamException("对端提前断开");
                    file.Write(buffer, 0, read);
                    remaining -= read;

                    if (DateTime.Now - lastReport >= ProgressInterval)
                    {
                        lastReport = DateTime.Now;
                        Report(transferId, TransferDirection.Upload, finalName, length - remaining, length, TransferState.InProgress);
                    }
                }
            }

            Report(transferId, TransferDirection.Upload, finalName, length, length, TransferState.Completed);
            return (true, finalName, null);
        }
        catch (Exception ex)
        {
            Report(transferId, TransferDirection.Upload, safeName, 0, length, TransferState.Failed, ex.Message);
            if (tempPath != null)
            {
                try { File.Delete(tempPath); } catch (Exception) { /* 半成品清理失败忽略 */ }
            }
            return (false, safeName, ex.Message);
        }
    }

    /// <summary>打开待发送文件流（下载路由用；文件已删/无权限返回 null）。返回进度包装流：随读取上报进度，读完报完成，提前释放报中断；流由传输层释放</summary>
    public Stream? OpenSharedFileStream(SharedFileEntry entry)
    {
        try
        {
            if (!File.Exists(entry.FullPath)) return null;
            var inner = new FileStream(entry.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkBytes);
            var transferId = Guid.NewGuid().ToString("N");
            return new ProgressReportStream(inner, entry.Name, transferId, this);
        }
        catch (Exception) { return null; }
    }

    /// <summary>下载进度包装流：读数据即累计已传字节并按节流上报；读到末尾报 Completed；未读完即 Dispose 报 Failed（对端中断）</summary>
    private sealed class ProgressReportStream : Stream
    {
        private readonly FileStream _inner;
        private readonly string _fileName;
        private readonly string _transferId;
        private readonly FileTransferService _owner;
        private readonly long _total;
        private long _transferred;
        private DateTime _lastReport = DateTime.MinValue;
        private bool _completed;

        public ProgressReportStream(FileStream inner, string fileName, string transferId, FileTransferService owner)
        {
            _inner = inner;
            _fileName = fileName;
            _transferId = transferId;
            _owner = owner;
            _total = inner.Length;
            _owner.Report(_transferId, TransferDirection.Download, _fileName, 0, _total, TransferState.InProgress);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var read = await _inner.ReadAsync(buffer, ct);
            if (read > 0)
            {
                _transferred += read;
                if (DateTime.Now - _lastReport >= ProgressInterval)
                {
                    _lastReport = DateTime.Now;
                    _owner.Report(_transferId, TransferDirection.Download, _fileName, _transferred, _total, TransferState.InProgress);
                }
            }
            else if (!_completed)
            {
                _completed = true;
                _owner.Report(_transferId, TransferDirection.Download, _fileName, _transferred, _total, TransferState.Completed);
            }
            return read;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!_completed && _transferred < _total)
                    _owner.Report(_transferId, TransferDirection.Download, _fileName, _transferred, _total, TransferState.Failed, "对端中断");
                _inner.Dispose();
            }
            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _total;
        public override long Position { get => _transferred; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // ==================== 文件名净化 ====================

    /// <summary>
    /// 净化文件名：剥离路径成分（防 ../../ 穿越）→ 非法字符替换下划线 → 去首尾点/空格（Windows 限制）→ 空名兜底。
    /// </summary>
    public static string SanitizeFileName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "unnamed";
        var name = Path.GetFileName(raw.Trim());
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        name = name.Trim('.', ' ');
        return string.IsNullOrWhiteSpace(name) ? "unnamed" : name;
    }

    /// <summary>重名自动编号：name.ext → name (1).ext / name (2).ext…</summary>
    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 1; ; i++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    // ==================== 内部 ====================

    private FileTransferService() : this(Path.Combine(AppPaths.DataDir, "file-transfer.json"))
    { }

    /// <param name="settingsPath">设置文件路径（测试注入临时路径，避免污染真实 LocalAppData）</param>
    internal FileTransferService(string settingsPath)
    {
        _settingsPath = settingsPath;
        _saveDir = Path.Combine(AppPaths.DataDir, "Received");
        var data = JsonSettingsFile.Load<SettingsData>(settingsPath);
        if (!string.IsNullOrWhiteSpace(data?.SaveDir)) _saveDir = data.SaveDir;
        // 不在构造时建目录（EnsureSaveDir 在上传/改目录时调用）：测试注入实例不应污染真实目录
    }

    private void EnsureSaveDir()
    {
        try
        {
            if (!Directory.Exists(_saveDir)) Directory.CreateDirectory(_saveDir);
        }
        catch (Exception) { /* 目录创建失败在上传时统一报错 */ }
    }

    private void Report(string transferId, TransferDirection direction, string fileName,
        long transferred, long total, TransferState state, string? error = null)
    {
        var progress = new TransferProgress(transferId, direction, fileName, transferred, total, state, error);
        _records[transferId] = (progress, DateTime.Now);

        // 容量淘汰：超限删最早已完成记录（进行中永不淘汰）
        if (_records.Count > MaxRecords)
        {
            var stale = _records
                .Where(kv => kv.Value.Progress.State != TransferState.InProgress)
                .OrderBy(kv => kv.Value.UpdatedAt)
                .Select(kv => kv.Key)
                .FirstOrDefault();
            if (stale != null) _records.TryRemove(stale, out _);
        }

        try { ProgressChanged?.Invoke(progress); } catch (Exception) { /* 订阅者异常不影响传输 */ }
    }

    private void Save()
    {
        try
        {
            JsonSettingsFile.Save(_settingsPath, new SettingsData { SaveDir = _saveDir });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FileTransferService] 保存失败: {ex.Message}");
        }
    }

    private sealed class SettingsData
    {
        public string? SaveDir { get; set; }
    }
}
