using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Toolbox.Plugins.Handlers;
using Toolbox.Plugins.Services;
using Xunit;

namespace Toolbox.Tests;

/// <summary>
/// 文件传输端到端测试：真实 TcpHttpServer 回环 + 临时目录注入（绝不污染真实 LocalAppData）。
/// 覆盖：流式上传（>1MB 绕开默认 body 限长）/流式下载回环字节一致、文件名净化、重名编号、
/// 认证/CSRF 防线对传输路由同样生效。
/// </summary>
public class FileTransferTests : IDisposable
{
    private const string TestToken = "ft-token-123";

    private readonly string _tempDir;
    private readonly string _receiveDir;
    private readonly FileTransferService _transfer;
    private readonly RemoteControlServer _server;
    private readonly HttpClient _client;

    public FileTransferTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"toolbox-ft-test-{Guid.NewGuid():N}");
        _receiveDir = Path.Combine(_tempDir, "Received");
        var settings = new RemoteControlSettings(Path.Combine(_tempDir, "remote-control.json"));
        _transfer = new FileTransferService(Path.Combine(_tempDir, "file-transfer.json"))
        {
            SaveDirectory = _receiveDir
        };
        _server = new RemoteControlServer(new TcpHttpServer(), settings, _transfer,
            new PowerCommandHandler(), new StatusCommandHandler());
        _server.Start(0, TestToken);

        _client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{_server.ActualPort}") };
        _client.DefaultRequestHeaders.Add("X-Requested-With", "RemoteControl");
    }

    public void Dispose()
    {
        _server.Stop();
        _client.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (Exception) { }
    }

    private async Task AuthenticateAsync()
    {
        var response = await _client.PostAsync("/api/auth",
            new StringContent($$"""{"token":"{{TestToken}}"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HttpResponseMessage> UploadAsync(byte[] data, string fileName)
    {
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        content.Headers.TryAddWithoutValidation("X-File-Name", Uri.EscapeDataString(fileName));
        return await _client.PostAsync("/api/transfer/upload", content);
    }

    // ==================== 文件名净化 ====================

    [Theory]
    [InlineData("../../evil.exe", "evil.exe")]           // 路径穿越 → 剥离路径
    [InlineData("..\\..\\evil.exe", "evil.exe")]         // Windows 反斜杠穿越
    [InlineData("a/b/c.txt", "c.txt")]                   // 深层路径剥离
    [InlineData("", "unnamed")]                          // 空名兜底
    [InlineData("   ", "unnamed")]                       // 空白兜底
    [InlineData("normal file.zip", "normal file.zip")]   // 正常名不变
    [InlineData("中文文件.rar", "中文文件.rar")]          // 中文名保留
    public void SanitizeFileName_VariousInputs_Safe(string input, string expected)
        => Assert.Equal(expected, FileTransferService.SanitizeFileName(input));

    [Fact]
    public void SanitizeFileName_InvalidChars_Replaced()
    {
        var result = FileTransferService.SanitizeFileName("a<b>c:d.txt");
        Assert.DoesNotContain('<', result);
        Assert.DoesNotContain('>', result);
        Assert.DoesNotContain(':', result);
        Assert.EndsWith(".txt", result);
    }

    // ==================== 待发送文件表 ====================

    [Fact]
    public void SharedFiles_AddMissingFile_ReturnsNull()
    {
        Assert.Null(_transfer.AddSharedFile(Path.Combine(_tempDir, "not-exists.bin")));
    }

    [Fact]
    public void SharedFiles_AddSamePath_Dedupes()
    {
        var path = Path.Combine(_tempDir, "dup.bin");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });

        var first = _transfer.AddSharedFile(path);
        var second = _transfer.AddSharedFile(path);

        Assert.NotNull(first);
        Assert.Equal(first!.Id, second!.Id);
        Assert.Single(_transfer.SharedFiles);
    }

    // ==================== 上传 ====================

    [Fact]
    public async Task Upload_WithoutSession_Returns401()
    {
        var response = await UploadAsync(new byte[16], "x.bin");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_WithoutCsrfHeader_Returns403()
    {
        await AuthenticateAsync();

        // 临时移除默认 X-Requested-With 头（已认证会话下，CSRF 校验应先于参数校验返回 403）
        _client.DefaultRequestHeaders.Remove("X-Requested-With");
        try
        {
            var content = new ByteArrayContent(new byte[16]);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            content.Headers.TryAddWithoutValidation("X-File-Name", "x.bin");
            var response = await _client.PostAsync("/api/transfer/upload", content);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            _client.DefaultRequestHeaders.Add("X-Requested-With", "RemoteControl");
        }
    }

    [Fact]
    public async Task Upload_RoundTrip_BytesIdentical()
    {
        await AuthenticateAsync();
        var data = new byte[5 * 1024 * 1024]; // 5MB：远超默认 1MB body 限长，验证流式旁路生效
        Random.Shared.NextBytes(data);

        var response = await UploadAsync(data, "big.bin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = Path.Combine(_receiveDir, "big.bin");
        Assert.True(File.Exists(saved));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(data)),
                     Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(saved))));
    }

    [Fact]
    public async Task Upload_PathTraversalName_Sanitized()
    {
        await AuthenticateAsync();
        var response = await UploadAsync(new byte[] { 9, 9 }, "../../evil.txt");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(File.Exists(Path.Combine(_receiveDir, "evil.txt")));
        Assert.False(File.Exists(Path.Combine(_tempDir, "evil.txt")));
    }

    [Fact]
    public async Task Upload_SameNameTwice_AutoRenamed()
    {
        await AuthenticateAsync();
        await UploadAsync(new byte[] { 1 }, "same.txt");
        var second = await UploadAsync(new byte[] { 2 }, "same.txt");

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.True(File.Exists(Path.Combine(_receiveDir, "same.txt")));
        Assert.True(File.Exists(Path.Combine(_receiveDir, "same (1).txt")));
        // 两份内容各自完整
        Assert.Equal(new byte[] { 1 }, await File.ReadAllBytesAsync(Path.Combine(_receiveDir, "same.txt")));
        Assert.Equal(new byte[] { 2 }, await File.ReadAllBytesAsync(Path.Combine(_receiveDir, "same (1).txt")));
    }

    // ==================== 下载 ====================

    [Fact]
    public async Task Download_RoundTrip_BytesIdentical()
    {
        await AuthenticateAsync();
        var data = new byte[3 * 1024 * 1024 + 7]; // 非整倍数，验证分块边界
        Random.Shared.NextBytes(data);
        var path = Path.Combine(_tempDir, "share-测试.bin");
        await File.WriteAllBytesAsync(path, data);
        var entry = _transfer.AddSharedFile(path);
        Assert.NotNull(entry);

        var response = await _client.GetAsync($"/api/transfer/download?id={entry!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(data.Length, response.Content.Headers.ContentLength);
        var disposition = response.Content.Headers.GetValues("Content-Disposition").Single();
        Assert.Contains("attachment", disposition);
        Assert.Contains(Uri.EscapeDataString("share-测试.bin"), disposition); // UTF-8 编码附件名
        Assert.Equal(data, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Download_UnknownId_Returns404()
    {
        await AuthenticateAsync();
        var response = await _client.GetAsync("/api/transfer/download?id=999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_AfterAdd_ContainsEntry()
    {
        await AuthenticateAsync();
        var path = Path.Combine(_tempDir, "listed.txt");
        File.WriteAllText(path, "hello");
        var entry = _transfer.AddSharedFile(path);

        var response = await _client.GetAsync("/api/transfer/list");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        var items = json.GetProperty("data");
        Assert.Contains(items.EnumerateArray(), item => item.GetProperty("id").GetInt32() == entry!.Id);
    }

    // ==================== 非流式路由回归 ====================

    [Fact]
    public async Task NonStreamingRoute_BodyOver1MB_Still413()
    {
        // 非流式路由仍受 1MB 整包限长约束——传输层改造不影响既有语义。
        // 用裸 TcpClient 只发头部（声明超限 Content-Length 但不发 body）：
        // HttpClient 会把 body 发完才读响应，服务端 413 后关连接会 RST 打断上传，测的是竞态而非语义。
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, _server.ActualPort);
        await using var stream = tcp.GetStream();

        var head = "POST /api/auth HTTP/1.1\r\n" +
                   $"Host: 127.0.0.1:{_server.ActualPort}\r\n" +
                   "Content-Type: application/json\r\n" +
                   $"Content-Length: {1024 * 1024 + 1}\r\n" +
                   "\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));

        var buffer = new byte[4096];
        var read = await stream.ReadAsync(buffer);
        var responseText = Encoding.ASCII.GetString(buffer, 0, read);
        Assert.StartsWith("HTTP/1.1 413", responseText);
    }
}
