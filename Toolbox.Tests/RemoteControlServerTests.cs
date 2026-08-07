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
        _server = new RemoteControlServer(new TcpHttpServer(), power, new StatusCommandHandler());
        _server.Start(0, TestToken); // 端口 0 = 系统分配随机高位端口

        _client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{_server.ActualPort}") };
        _client.DefaultRequestHeaders.Add("X-Requested-With", "RemoteControl");
    }

    public void Dispose()
    {
        _server.Stop();
        _client.Dispose();
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
