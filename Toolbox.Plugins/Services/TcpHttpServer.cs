using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Toolbox.Plugins.Services;

/// <summary>
/// ★ 主方案：TcpListener 手写极简 HTTP 服务器（零第三方依赖、普通权限即可监听非 localhost 端口）。
/// 每个连接一个后台 Task；畸形/超大请求返回 400/413；连接异常就地捕获不崩溃；单请求限时防挂起。
/// 设计见 docs/REMOTE_CONTROL_TOOL_DESIGN.md 第 3.1 / 4.2 节。
/// </summary>
public sealed class TcpHttpServer : IRemoteHttpServer
{
    /// <summary>请求头总长度上限（16KB，防畸形请求拖垮服务）</summary>
    private const int MaxHeaderBytes = 16 * 1024;

    /// <summary>请求体长度上限（1MB，本项目无大文件传输）</summary>
    private const int MaxBodyBytes = 1024 * 1024;

    /// <summary>单请求处理时限（30s，超时即断开，防挂起连接堆积）</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>并发连接上限（慢连接洪水防线：超出直接断开，防线程池饥饿——审查 P2-6）</summary>
    private const int MaxConcurrentConnections = 64;

    private readonly SemaphoreSlim _connectionSlots = new(MaxConcurrentConnections, MaxConcurrentConnections);

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private int _actualPort;

    public bool IsRunning => _listener != null;

    public int ActualPort => _actualPort;

    public Func<RemoteHttpRequest, RemoteHttpResponse>? RequestHandler { get; set; }

    public void Start(int port)
    {
        if (IsRunning) return; // 幂等

        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        _listener = listener;
        _actualPort = ((IPEndPoint)listener.LocalEndpoint).Port;

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        var listener = _listener;
        if (listener == null) return; // 幂等

        _listener = null;
        _cts?.Cancel();
        try { listener.Stop(); } catch (Exception) { /* 已停止/句柄被并发关闭，忽略 */ }
        _cts?.Dispose();
        _cts = null;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (_listener != null && !ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(ct);
            }
            catch (Exception)
            {
                return; // Stop() 触发或监听异常 → 退出循环
            }

            // 并发连接超限：直接断开（防慢连接洪水占满线程池）
            if (!_connectionSlots.Wait(0))
            {
                try { client.Dispose(); } catch (Exception) { }
                continue;
            }

            _ = Task.Run(async () =>
            {
                try { await HandleClientAsync(client); }
                finally { _connectionSlots.Release(); }
            });
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                using var stream = client.GetStream();
                using var timeoutCts = new CancellationTokenSource(RequestTimeout);

                var request = await ReadRequestAsync(stream, client, timeoutCts.Token);
                if (request == null) return; // 解析失败已尝试响应，或对端提前关闭

                var response = RequestHandler?.Invoke(request)
                    ?? new RemoteHttpResponse { StatusCode = 404, Body = """{"success":false,"error":"no handler"}""" };

                await WriteResponseAsync(stream, response, timeoutCts.Token);
            }
            catch (Exception)
            {
                // 连接级异常就地吞掉：单连接失败不影响服务器（设计文档第 13.2 条切换条件之一）
            }
        }
    }

    // ==================== 请求解析 ====================

    private async Task<RemoteHttpRequest?> ReadRequestAsync(NetworkStream stream, TcpClient client, CancellationToken ct)
    {
        try
        {
            var requestLine = await ReadLineAsync(stream, MaxHeaderBytes, ct);
            if (requestLine == null) return null; // 对端提前关闭（读 0 字节）

            var parts = requestLine.Split(' ');
            if (parts.Length < 3)
            {
                await WriteSimpleAsync(stream, 400, "text/plain; charset=utf-8", "bad request", ct);
                return null;
            }

            var method = parts[0].ToUpperInvariant();
            var (path, query) = SplitPathQuery(parts[1]);

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (true)
            {
                var line = await ReadLineAsync(stream, MaxHeaderBytes, ct);
                if (line == null) return null;
                if (line.Length == 0) break; // 空行 = 头结束

                var colon = line.IndexOf(':');
                if (colon <= 0) continue; // 畸形头行忽略
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }

            var body = "";
            if (headers.TryGetValue("Content-Length", out var clText) && int.TryParse(clText, out var contentLength))
            {
                if (contentLength > MaxBodyBytes)
                {
                    await WriteSimpleAsync(stream, 413, "text/plain; charset=utf-8", "body too large", ct);
                    return null;
                }
                if (contentLength > 0)
                {
                    var buffer = new byte[contentLength];
                    await stream.ReadExactlyAsync(buffer, ct);
                    body = Encoding.UTF8.GetString(buffer);
                }
            }

            return new RemoteHttpRequest
            {
                Method = method,
                Path = path,
                Query = query,
                Headers = headers,
                Body = body,
                RemoteIp = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? ""
            };
        }
        catch (TooLargeException)
        {
            // 请求行/头行超限：回应 413 而非静默断连（设计文档 4.2；审查 P2-4）
            await WriteSimpleAsync(stream, 413, "text/plain; charset=utf-8", "request too large", ct);
            return null;
        }
    }

    /// <summary>读取一行；对端关闭返回 null；超过限长抛 <see cref="TooLargeException"/></summary>
    private static async Task<string?> ReadLineAsync(NetworkStream stream, int maxBytes, CancellationToken ct)
    {
        var sb = new StringBuilder(128);
        var buffer = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, 1), ct);
            if (read == 0) return null; // 对端关闭
            var c = (char)buffer[0];
            if (c == '\n') return sb.ToString();
            if (c != '\r') sb.Append(c);
            if (sb.Length >= maxBytes) throw new TooLargeException();
        }
    }

    /// <summary>请求行/头行超过限长（内部信号，映射为 413 响应）</summary>
    private sealed class TooLargeException : Exception { }

    private static (string Path, Dictionary<string, string> Query) SplitPathQuery(string raw)
    {
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var qIndex = raw.IndexOf('?');
        if (qIndex < 0) return (raw, query);

        var path = raw[..qIndex];
        foreach (var pair in raw[(qIndex + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var key = eq < 0 ? Uri.UnescapeDataString(pair) : Uri.UnescapeDataString(pair[..eq]);
            var value = eq < 0 ? "" : Uri.UnescapeDataString(pair[(eq + 1)..]);
            query[key] = value;
        }
        return (path, query);
    }

    // ==================== 响应编码 ====================

    private static async Task WriteResponseAsync(NetworkStream stream, RemoteHttpResponse response, CancellationToken ct)
    {
        var reason = StatusReason(response.StatusCode);
        var bodyBytes = Encoding.UTF8.GetBytes(response.Body);

        var sb = new StringBuilder(256);
        sb.Append("HTTP/1.1 ").Append(response.StatusCode).Append(' ').Append(reason).Append("\r\n");
        sb.Append("Content-Type: ").Append(response.ContentType).Append("\r\n");
        sb.Append("Content-Length: ").Append(bodyBytes.Length).Append("\r\n");
        sb.Append("Connection: close\r\n");
        foreach (var (key, value) in response.Headers)
            sb.Append(key).Append(": ").Append(value).Append("\r\n");
        sb.Append("\r\n");

        var headBytes = Encoding.ASCII.GetBytes(sb.ToString());
        await stream.WriteAsync(headBytes, ct);
        if (bodyBytes.Length > 0)
            await stream.WriteAsync(bodyBytes, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task WriteSimpleAsync(NetworkStream stream, int code, string contentType, string body, CancellationToken ct)
    {
        await WriteResponseAsync(stream, new RemoteHttpResponse
        {
            StatusCode = code,
            ContentType = contentType,
            Body = body
        }, ct);
    }

    private static string StatusReason(int code) => code switch
    {
        200 => "OK",
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        413 => "Payload Too Large",
        429 => "Too Many Requests",
        500 => "Internal Server Error",
        _ => "OK"
    };
}
