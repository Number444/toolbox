using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Toolbox.Plugins.Handlers;
using Toolbox.Plugins.Helpers;
using Toolbox.Plugins.Services;
using Xunit;

namespace Toolbox.Tests;

/// <summary>
/// RemoteControlServer 端到端测试：真实 TcpHttpServer 监听随机高位端口 + 127.0.0.1 回环。
/// 认证流程 / 路由 / CSRF / 暴力锁定全覆盖；指令执行器注入记录型假实现（绝不触发真实关机）。
/// 设计文档 9 章：不引入 Moq，假实现即测试桩。
/// </summary>
public class RemoteControlServerTests : IDisposable
{
    private const string TestToken = "test-token-123";

    private readonly string _tempDir;
    private readonly RemoteControlServer _server;
    private readonly HttpClient _client;
    private readonly List<ProcessStartInfo> _started = new();
    private readonly List<string> _systemActions = new();

    public RemoteControlServerTests()
    {
        // 假执行器隔离：进程命令与系统动作（锁屏/睡眠）全部注入记录型假实现——跑测试绝不触发真实系统动作
        var power = new PowerCommandHandler(
            new PowerActions(psi => { _started.Add(psi); return 0; }),
            action => { _systemActions.Add(action); return true; });
        // 设置注入临时目录（单一 json）：避免污染真实 LocalAppData
        _tempDir = Path.Combine(Path.GetTempPath(), $"toolbox-rc-test-{Guid.NewGuid():N}");
        var settings = new RemoteControlSettings(Path.Combine(_tempDir, "remote-control.json"));
        _server = new RemoteControlServer(new TcpHttpServer(), settings, power, new StatusCommandHandler());
        _server.Start(0, TestToken); // 端口 0 = 系统分配随机高位端口

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

    private static async Task<string> PostJsonAsync(HttpClient client, string path, string json)
    {
        var response = await client.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();
        return response.StatusCode + "|" + body;
    }

    // ==================== 路由与认证 ====================

    [Fact]
    public async Task Root_ReturnsControlPanelHtml()
    {
        var response = await _client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("text/html", response.Content.Headers.ContentType!.ToString());
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Toolbox 远程控制", html);
        Assert.Contains("api/command", html);
    }

    [Fact]
    public async Task Status_WithoutSession_Returns401()
    {
        var response = await _client.GetAsync("/api/status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var json = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        Assert.False(json.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Auth_WrongToken_Returns401()
    {
        var result = await PostJsonAsync(_client, "/api/auth", """{"token":"wrong-token"}""");

        Assert.StartsWith("Unauthorized", result);
        Assert.Contains("invalid token", result);
    }

    [Fact]
    public async Task Auth_AndSessionFlow_ThenStatusOk()
    {
        // 认证成功（HttpClient 自动管理 Set-Cookie 会话）
        var auth = await PostJsonAsync(_client, "/api/auth", $$"""{"token":"{{TestToken}}"}""");
        Assert.StartsWith("OK", auth);

        // 同一 client 携带会话 Cookie → 状态可访问
        var status = await _client.GetAsync("/api/status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var json = JsonSerializer.Deserialize<JsonElement>(await status.Content.ReadAsStringAsync());
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.True(json.GetProperty("data").TryGetProperty("ipv4", out _));
    }

    [Fact]
    public async Task Auth_FailsFiveTimes_LocksForThirtySeconds()
    {
        for (var i = 0; i < 5; i++)
            await PostJsonAsync(_client, "/api/auth", """{"token":"wrong"}""");

        // 锁定期内即使正确 Token 也拒绝
        var locked = await PostJsonAsync(_client, "/api/auth", $$"""{"token":"{{TestToken}}"}""");
        Assert.StartsWith("TooManyRequests", locked);
        Assert.Contains("锁定", locked);
    }

    // ==================== 指令与 CSRF ====================

    [Fact]
    public async Task Command_WithoutCsrfHeader_Returns403()
    {
        // 去掉 CSRF 头的独立 client（模拟跨站请求）
        using var noCsrfClient = new HttpClient { BaseAddress = _client.BaseAddress };
        await PostJsonAsync(noCsrfClient, "/api/auth", $$"""{"token":"{{TestToken}}"}""");

        var result = await PostJsonAsync(noCsrfClient, "/api/command", """{"command":"lock"}""");
        Assert.StartsWith("Forbidden", result);
        Assert.Empty(_started); // 未执行任何指令
    }

    [Fact]
    public async Task Command_WithCsrfHeader_ExecutesViaInjectedExecutor()
    {
        await PostJsonAsync(_client, "/api/auth", $$"""{"token":"{{TestToken}}"}""");

        var result = await PostJsonAsync(_client, "/api/command",
            """{"command":"shutdown","args":{"delaySeconds":60,"confirm":true}}""");

        Assert.StartsWith("OK", result);
        var psi = Assert.Single(_started); // 假执行器记录：确认到达指令层而非真实关机
        Assert.Equal("shutdown.exe", psi.FileName);
        Assert.Contains("/s /t 60", psi.Arguments);
    }

    [Fact]
    public async Task Command_UnknownCommand_Returns404()
    {
        await PostJsonAsync(_client, "/api/auth", $$"""{"token":"{{TestToken}}"}""");

        var result = await PostJsonAsync(_client, "/api/command", """{"command":"bogus"}""");
        Assert.StartsWith("NotFound", result);
        Assert.Contains("unknown command", result);
    }

    [Fact]
    public async Task Command_WithoutAuth_Returns401()
    {
        var result = await PostJsonAsync(_client, "/api/command", """{"command":"lock"}""");
        Assert.StartsWith("Unauthorized", result);
        Assert.Empty(_started);
    }

    // ==================== 审计日志 ====================

    [Fact]
    public async Task Events_ListsExecutedCommandsWithSourceIp()
    {
        await PostJsonAsync(_client, "/api/auth", $$"""{"token":"{{TestToken}}"}""");
        await PostJsonAsync(_client, "/api/command", """{"command":"lock"}""");

        // 系统动作经注入假实现执行——绝不真实锁屏
        Assert.Equal("lock", Assert.Single(_systemActions));

        var response = await _client.GetAsync("/api/events");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        var items = json.GetProperty("data").EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal("lock", items[0].GetProperty("command").GetString());
        Assert.Equal("127.0.0.1", items[0].GetProperty("fromIp").GetString());
    }

    [Fact]
    public async Task Events_StatusPolling_NotLogged()
    {
        await PostJsonAsync(_client, "/api/auth", $$"""{"token":"{{TestToken}}"}""");
        await _client.GetAsync("/api/status"); // 状态轮询不产生审计日志

        var response = await _client.GetAsync("/api/events");
        var json = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        Assert.Empty(json.GetProperty("data").EnumerateArray());
    }

    // ==================== 畸形请求/限长（设计 9 章：裸 socket 直测协议层） ====================

    [Fact]
    public async Task Malformed_HugeRequestLine_Returns413()
    {
        using var client = new System.Net.Sockets.TcpClient();
        await client.ConnectAsync("127.0.0.1", _server.ActualPort);
        await using var stream = client.GetStream();

        // 请求行超 16KB（协议限长）→ 应收到 413 而非静默断连（审查 P2-4）
        var hugeLine = new string('A', 20 * 1024) + "\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(hugeLine));
        var response = await ReadAllUntilClosedAsync(stream);

        Assert.Contains("413", response);
    }

    [Fact]
    public async Task Malformed_HugeBody_Returns413()
    {
        using var client = new System.Net.Sockets.TcpClient();
        await client.ConnectAsync("127.0.0.1", _server.ActualPort);
        await using var stream = client.GetStream();

        // Content-Length 声明超 1MB 上限 → 413
        var head = "POST /api/command HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Length: 2097152\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
        var response = await ReadAllUntilClosedAsync(stream);

        Assert.Contains("413", response);
    }

    [Fact]
    public async Task Malformed_GarbageRequest_ServerStillServes()
    {
        // 畸形请求后服务必须仍然健康（FR-5 容错）
        using (var client = new System.Net.Sockets.TcpClient())
        {
            await client.ConnectAsync("127.0.0.1", _server.ActualPort);
            await using var stream = client.GetStream();
            await stream.WriteAsync(Encoding.ASCII.GetBytes("GARBAGE NOT HTTP\r\n\r\n"));
            await ReadAllUntilClosedAsync(stream);
        }

        var response = await _client.GetAsync("/api/status");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); // 服务仍响应
    }

    [Fact]
    public async Task DnsRebinding_HostName_Rejected()
    {
        // Host 必须为 IP 字面量/localhost（审查 P2-8）：域名 Host 一律 404
        var request = new HttpRequestMessage(HttpMethod.Get, $"{_client.BaseAddress}/api/status");
        request.Headers.Host = "evil.example.com";
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<string> ReadAllUntilClosedAsync(System.Net.Sockets.NetworkStream stream)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = new byte[4096];
        var sb = new StringBuilder();
        while (true)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(buffer.AsMemory(), timeoutCts.Token);
            }
            catch (System.Net.Sockets.SocketException)
            {
                break; // RST：对端带未读数据关闭（服务端已响应 413 后断开），保留已读内容
            }
            catch (IOException)
            {
                break;
            }
            if (read == 0) break; // Connection: close 后读到 EOF
            sb.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }
        return sb.ToString();
    }

    // ==================== 设备管理与自动填密钥 ====================

    [Fact]
    public async Task Devices_AfterAuth_ListsConnectedDevice()
    {
        await PostJsonAsync(_client, "/api/auth", $$"""{"token":"{{TestToken}}"}""");

        var response = await _client.GetAsync("/api/devices");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        var connected = json.GetProperty("data").GetProperty("connected").EnumerateArray().ToList();
        Assert.Single(connected);
        Assert.Equal("127.0.0.1", connected[0].GetProperty("ip").GetString());
    }

    [Fact]
    public async Task Devices_WithoutAuth_Returns401()
    {
        var response = await _client.GetAsync("/api/devices");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task KnownDevice_GetsAutoKeyInjected()
    {
        // 认证成功 = 设备被记录 → GET / 注入真实密钥（自动填密钥，JSON 字面量转义形式）
        await PostJsonAsync(_client, "/api/auth", $$"""{"token":"{{TestToken}}"}""");

        var html = await (await _client.GetAsync("/")).Content.ReadAsStringAsync();
        Assert.Contains($"__AUTO_KEY__ = {JsonSerializer.Serialize(TestToken)};", html);
    }

    [Fact]
    public async Task UnknownDevice_NoAutoKeyInjected()
    {
        // 未认证过的设备（无会话也无设备记录）→ 页面保持占位符
        var html = await (await _client.GetAsync("/")).Content.ReadAsStringAsync();
        Assert.Contains("window.__AUTO_KEY__ = '';", html);
        Assert.DoesNotContain("__AUTO_KEY__ = 'test", html);
    }

    [Fact]
    public async Task Kick_RemovesSession_AndRevokesAutoKey()
    {
        await PostJsonAsync(_client, "/api/auth", $$"""{"token":"{{TestToken}}"}""");

        var kick = await PostJsonAsync(_client, "/api/devices/kick", """{"ip":"127.0.0.1"}""");
        Assert.StartsWith("OK", kick);

        // 会话被删 → 状态接口回到 401
        var status = await _client.GetAsync("/api/status");
        Assert.Equal(HttpStatusCode.Unauthorized, status.StatusCode);

        // 设备表移除 → 自动填密钥撤销
        var html = await (await _client.GetAsync("/")).Content.ReadAsStringAsync();
        Assert.DoesNotContain($"__AUTO_KEY__ = '{TestToken}'", html);
    }

    [Fact]
    public async Task Kick_WithoutCsrfHeader_Returns403()
    {
        using var noCsrfClient = new HttpClient { BaseAddress = _client.BaseAddress };
        await PostJsonAsync(noCsrfClient, "/api/auth", $$"""{"token":"{{TestToken}}"}""");

        var kick = await PostJsonAsync(noCsrfClient, "/api/devices/kick", """{"ip":"127.0.0.1"}""");
        Assert.StartsWith("Forbidden", kick);
    }

    // ==================== 免登录模式（无密钥启动） ====================

    [Fact]
    public async Task NoKeyMode_NoAuthRequired_AndFlagSet()
    {
        // 无密钥启动（token 为 null = 自动生成）→ 免登录模式
        var settings = new RemoteControlSettings(Path.Combine(_tempDir, "no-key-mode.json"));
        var power = new PowerCommandHandler(new PowerActions(_ => 0), _ => true);
        var server = new RemoteControlServer(new TcpHttpServer(), settings, power, new StatusCommandHandler());
        server.Start(0, null);

        Assert.True(server.IsNoKeyMode);
        Assert.NotNull(server.Token); // 自动生成

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.ActualPort}") };
        client.DefaultRequestHeaders.Add("X-Requested-With", "RemoteControl"); // CSRF 头独立于认证，仍必须
        var status = await client.GetAsync("/api/status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode); // 免认证直接放行
        var events = await client.GetAsync("/api/events");
        Assert.Equal(HttpStatusCode.OK, events.StatusCode);
        var command = await PostJsonAsync(client, "/api/command",
            """{"command":"shutdown","args":{"delaySeconds":1,"confirm":true}}""");
        Assert.StartsWith("OK", command);

        server.Stop();
        Assert.False(server.IsNoKeyMode);
    }

    [Fact]
    public void KeyMode_NoKeyModeFlagFalse()
    {
        // 手动指定密钥 → 非免登录模式
        var settings = new RemoteControlSettings(Path.Combine(_tempDir, "key-mode.json"));
        var server = new RemoteControlServer(new TcpHttpServer(), settings, new PowerCommandHandler(new PowerActions(_ => 0), _ => true), new StatusCommandHandler());
        server.Start(0, "manual-key");

        Assert.False(server.IsNoKeyMode);
        server.Stop();
    }

    [Fact]
    public async Task NoKeyMode_GenerateKeyOff_TokenIsNull()
    {
        // 开关"无密钥时自动生成随机密钥"关：免登录启动且不生成密钥（面板显示"—"）
        var settings = new RemoteControlSettings(Path.Combine(_tempDir, "no-key-no-gen.json"));
        var server = new RemoteControlServer(new TcpHttpServer(), settings, new PowerCommandHandler(new PowerActions(_ => 0), _ => true), new StatusCommandHandler());
        server.Start(0, null, generateKey: false);

        Assert.True(server.IsNoKeyMode);
        Assert.Null(server.Token); // 不生成密钥

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.ActualPort}") };
        var status = await client.GetAsync("/api/status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode); // 免登录放行

        server.Stop();
    }

    [Fact]
    public void NoKeyMode_GenerateKeyOn_TokenGenerated()
    {
        // 开关开（默认）：免登录启动且生成随机密钥（面板可展示/复制）
        var settings = new RemoteControlSettings(Path.Combine(_tempDir, "no-key-gen.json"));
        var server = new RemoteControlServer(new TcpHttpServer(), settings, new PowerCommandHandler(new PowerActions(_ => 0), _ => true), new StatusCommandHandler());
        server.Start(0, null); // generateKey 默认 true

        Assert.True(server.IsNoKeyMode);
        Assert.NotNull(server.Token);
        Assert.Equal(16, server.Token!.Length); // Guid.N[..16]

        server.Stop();
    }

    [Fact]
    public void Start_EmptyStringKey_NormalizedToNoKeyMode()
    {
        // API 规范化：空串密钥视为未指定 → 免登录（防锁死，审查 P2-9）
        var settings = new RemoteControlSettings(Path.Combine(_tempDir, "empty-key.json"));
        var server = new RemoteControlServer(new TcpHttpServer(), settings, new PowerCommandHandler(new PowerActions(_ => 0), _ => true), new StatusCommandHandler());
        server.Start(0, "   "); // 空白串

        Assert.True(server.IsNoKeyMode);
        server.Stop();
    }

    [Fact]
    public async Task AutoKeyInjection_SpecialChars_EscapedAsJsonLiteral()
    {
        // 密钥含引号/反斜杠：注入必须为合法 JS 字符串字面量，不破坏控制页 script（审查 P1-1）
        const string trickyKey = "abc'def\"gh\\ij";
        var settings = new RemoteControlSettings(Path.Combine(_tempDir, "tricky-key.json"));
        settings.RecordDevice("127.0.0.1", "test"); // 预置已记录设备（触发注入）
        var server = new RemoteControlServer(new TcpHttpServer(), settings, new PowerCommandHandler(new PowerActions(_ => 0), _ => true), new StatusCommandHandler());
        server.Start(0, trickyKey);

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.ActualPort}") };
        var html = await (await client.GetAsync("/")).Content.ReadAsStringAsync();

        // JSON 序列化注入：引号/反斜杠全部转义为合法 JS 字符串
        var expected = $"window.__AUTO_KEY__ = {JsonSerializer.Serialize(trickyKey)};";
        Assert.Contains(expected, html);
        Assert.DoesNotContain($"__AUTO_KEY__ = '{trickyKey}'", html); // 旧裸引号注入已不存在

        server.Stop();
    }

    // ==================== 关闭 Toolbox（/api/app/shutdown） ====================

    [Fact]
    public async Task AppShutdown_RequiresCsrfHeader()
    {
        await PostJsonAsync(_client, "/api/auth", $$"""{"token":"{{TestToken}}"}""");
        using var noCsrfClient = new HttpClient { BaseAddress = _client.BaseAddress };
        await PostJsonAsync(noCsrfClient, "/api/auth", $$"""{"token":"{{TestToken}}"}""");

        var result = await PostJsonAsync(noCsrfClient, "/api/app/shutdown", "{}");
        Assert.StartsWith("Forbidden", result);
    }

    [Fact]
    public async Task AppShutdown_RequiresAuth()
    {
        var result = await PostJsonAsync(_client, "/api/app/shutdown", "{}");
        Assert.StartsWith("Unauthorized", result);
    }

    [Fact]
    public async Task AppShutdown_Authenticated_ReturnsOk_WithoutExitingProcess()
    {
        // 测试环境 Application.Current == null → 服务器不执行真实关闭，仅验证响应
        await PostJsonAsync(_client, "/api/auth", $$"""{"token":"{{TestToken}}"}""");

        var result = await PostJsonAsync(_client, "/api/app/shutdown", "{}");
        Assert.StartsWith("OK", result);
        Assert.True(_server.IsRunning); // 进程未被关闭（非应用上下文）
    }

    [Fact]
    public async Task AppShutdown_NoKeyMode_Allowed()
    {
        var settings = new RemoteControlSettings(Path.Combine(_tempDir, "app-shutdown-nokey.json"));
        var server = new RemoteControlServer(new TcpHttpServer(), settings, new PowerCommandHandler(new PowerActions(_ => 0), _ => true), new StatusCommandHandler());
        server.Start(0, null);

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.ActualPort}") };
        client.DefaultRequestHeaders.Add("X-Requested-With", "RemoteControl");
        var result = await PostJsonAsync(client, "/api/app/shutdown", "{}");
        Assert.StartsWith("OK", result);

        server.Stop();
    }

    // ==================== 生命周期幂等 ====================

    [Fact]
    public void Start_AndStop_AreIdempotent()
    {
        _server.Start(0, "another-token"); // 已运行：忽略
        Assert.True(_server.IsRunning);

        _server.Stop();
        Assert.False(_server.IsRunning);
        _server.Stop(); // 重复调用无害
        Assert.False(_server.IsRunning);
    }
}
