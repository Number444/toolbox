using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Toolbox.Plugins.Handlers;
using Toolbox.Plugins.Models;

namespace Toolbox.Plugins.Services;

/// <summary>
/// 远程控制核心服务：路由 + Token 认证 + 会话 + 指令分发 + 操作审计（设计文档 4.2 节）。
/// 持有 <see cref="IRemoteHttpServer"/>（主/备方案可切换），传输层与业务层解耦。
/// 线程安全：指令执行经 SemaphoreSlim 串行化（避免并发关机/重启竞态）。
/// </summary>
public sealed class RemoteControlServer
{
    /// <summary>连续认证失败 5 次后锁定时长（防暴力枚举）</summary>
    private const int AuthFailLockThreshold = 5;

    private static readonly TimeSpan AuthLockDuration = TimeSpan.FromSeconds(30);

    /// <summary>会话有效期 8 小时</summary>
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

    private const int MaxLogEntries = 20;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        // API 响应保持中文可读（默认 \uXXXX 转义只影响可读性，不影响 JSON 合法性）
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IRemoteHttpServer _http;
    private readonly IRemoteCommandHandler[] _handlers;
    private readonly ConcurrentQueue<LogEntry> _logs = new();
    private readonly SemaphoreSlim _commandLock = new(1, 1);

    /// <summary>会话字典：多设备并发读写（登录写 + 轮询读），必须用并发集合（审查 P1-2）</summary>
    private readonly ConcurrentDictionary<string, DateTime> _sessions = new();

    /// <summary>认证失败状态（按来源 IP 隔离，防单设备恶意爆破拖累全员——审查 P2-2）</summary>
    private readonly object _authLock = new();
    private readonly Dictionary<string, AuthState> _authByIp = new();

    private string? _token;
    private byte[]? _htmlBytes;

    /// <summary>按来源 IP 的认证状态：失败计数 + 锁定期</summary>
    private sealed class AuthState
    {
        public int FailCount;
        public DateTime LockedUntil = DateTime.MinValue;
    }

    public RemoteControlServer(IRemoteHttpServer http, params IRemoteCommandHandler[] handlers)
    {
        _http = http;
        _handlers = handlers;
        _http.RequestHandler = HandleRequest;
    }

    /// <summary>服务运行状态（面板状态灯的唯一事实源）</summary>
    public bool IsRunning => _http.IsRunning;

    /// <summary>实际监听端口</summary>
    public int ActualPort => _http.ActualPort;

    /// <summary>当前 Token（未启动为 null）</summary>
    public string? Token => _token;

    /// <summary>最近操作日志快照（最新在前）</summary>
    public IReadOnlyList<LogEntry> Logs => _logs.Reverse().ToArray();

    /// <summary>启动服务（幂等）。token 为空时自动生成随机 Token（不落盘）</summary>
    public void Start(int port, string? token = null)
    {
        if (IsRunning) return; // 幂等
        _token = token ?? Guid.NewGuid().ToString("N")[..16];
        _sessions.Clear();
        _logs.Clear();
        lock (_authLock) _authByIp.Clear();
        _http.Start(port);
    }

    /// <summary>停止服务（幂等，重复调用无害）</summary>
    public void Stop()
    {
        _http.Stop();
        _token = null;
        _sessions.Clear();
        _logs.Clear();
        lock (_authLock) _authByIp.Clear();
    }

    // ==================== 路由 ====================

    private RemoteHttpResponse HandleRequest(RemoteHttpRequest request)
    {
        try
        {
            // DNS rebinding 防线：Host 必须为本机 IP 字面量/localhost（审查 P2-8）
            if (!IsAllowedHost(request))
                return JsonError(404, "not found");

            if (request.Method == "GET" && request.Path == "/")
                return HtmlResponse();
            if (request.Method == "POST" && request.Path == "/api/auth")
                return HandleAuth(request);
            if (request.Method == "POST" && request.Path == "/api/command")
                return RequireSession(request, () => HandleCommand(request));
            if (request.Method == "GET" && request.Path == "/api/status")
                return RequireSession(request, () => ExecuteHandler("status", null, recordLog: false, request.RemoteIp));
            if (request.Method == "GET" && request.Path == "/api/events")
                return RequireSession(request, HandleEvents);
            return JsonError(404, $"not found: {request.Method} {request.Path}");
        }
        catch (Exception ex)
        {
            // 任何内部异常 → 友好 JSON 500，不崩溃（设计文档 FR-5）；细节只进本地日志，不回给远端（审查 P2-9）
            Debug.WriteLine($"[RemoteControlServer] 请求处理异常: {ex}");
            return JsonError(500, "internal error");
        }
    }

    /// <summary>Host 头白名单：仅接受 IP 字面量 / localhost（拒绝域名 → 阻断 DNS rebinding 类攻击）</summary>
    private static bool IsAllowedHost(RemoteHttpRequest request)
    {
        if (!request.Headers.TryGetValue("Host", out var host) || string.IsNullOrWhiteSpace(host))
            return false;

        var name = host;
        if (name.StartsWith('[')) // IPv6 字面量 [::1]:port
        {
            var end = name.IndexOf(']');
            name = end > 0 ? name[1..end] : name;
        }
        else
        {
            var colon = name.LastIndexOf(':');
            if (colon > 0) name = name[..colon]; // 去端口
        }

        if (name.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return IPAddress.TryParse(name, out _);
    }

    // ==================== 认证与会话 ====================

    private RemoteHttpResponse HandleAuth(RemoteHttpRequest request)
    {
        // 失败计数与锁定按来源 IP 隔离（_authLock 保护）；锁定期内的正确 Token 也拒绝
        var ip = string.IsNullOrEmpty(request.RemoteIp) ? "unknown" : request.RemoteIp;
        lock (_authLock)
        {
            if (!_authByIp.TryGetValue(ip, out var state))
            {
                state = new AuthState();
                _authByIp[ip] = state;
            }
            if (DateTime.Now < state.LockedUntil)
                return JsonError(429, $"认证失败次数过多，已锁定至 {state.LockedUntil:HH:mm:ss}");

            string? token;
            try
            {
                using var doc = JsonDocument.Parse(request.Body);
                token = doc.RootElement.TryGetProperty("token", out var t) ? t.GetString() : null;
            }
            catch (Exception)
            {
                return JsonError(400, "invalid json body");
            }

            if (string.IsNullOrEmpty(token) || _token == null || !TokensEqual(token, _token))
            {
                state.FailCount++;
                if (state.FailCount >= AuthFailLockThreshold)
                {
                    state.LockedUntil = DateTime.Now.Add(AuthLockDuration);
                    state.FailCount = 0;
                }
                return JsonError(401, "invalid token");
            }

            state.FailCount = 0;
        }

        var sessionId = Guid.NewGuid().ToString("N");
        _sessions[sessionId] = DateTime.Now.Add(SessionLifetime);

        var response = new RemoteHttpResponse { Body = """{"success":true,"data":null,"error":null}""" };
        response.Headers["Set-Cookie"] = $"rc_session={sessionId}; HttpOnly; SameSite=Lax; Path=/; Max-Age=28800";
        return response;
    }

    /// <summary>Token 定长比较：防时序侧信道（审查 P2-3）</summary>
    private static bool TokensEqual(string actual, string expected)
    {
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return actualBytes.Length == expectedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }

    private RemoteHttpResponse RequireSession(RemoteHttpRequest request, Func<RemoteHttpResponse> action)
    {
        if (!TryGetSessionId(request, out var sessionId) || !IsSessionValid(sessionId))
            return JsonError(401, "unauthorized");

        // 惰性清理过期会话（防字典无限增长；ConcurrentDictionary 枚举线程安全）
        if (_sessions.Count > 200)
        {
            var now = DateTime.Now;
            foreach (var expired in _sessions.Where(kv => kv.Value < now).Select(kv => kv.Key).ToList())
                _sessions.TryRemove(expired, out _);
        }
        return action();
    }

    private bool TryGetSessionId(RemoteHttpRequest request, out string sessionId)
    {
        sessionId = "";
        if (!request.Headers.TryGetValue("Cookie", out var cookieHeader)) return false;
        foreach (var part in cookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.StartsWith("rc_session=", StringComparison.OrdinalIgnoreCase))
            {
                sessionId = part["rc_session=".Length..];
                return sessionId.Length > 0;
            }
        }
        return false;
    }

    private bool IsSessionValid(string sessionId)
        => _sessions.TryGetValue(sessionId, out var expires) && DateTime.Now <= expires;

    // ==================== 指令分发 ====================

    private RemoteHttpResponse HandleCommand(RemoteHttpRequest request)
    {
        // CSRF 防护：所有写操作要求自定义头（浏览器跨站表单无法伪造）
        if (!request.Headers.TryGetValue("X-Requested-With", out var csrf) ||
            !string.Equals(csrf, "RemoteControl", StringComparison.Ordinal))
            return JsonError(403, "missing X-Requested-With header");

        RemoteControlRequest? command;
        try
        {
            command = JsonSerializer.Deserialize<RemoteControlRequest>(request.Body, JsonOptions);
        }
        catch (Exception)
        {
            return JsonError(400, "invalid json body");
        }
        if (command == null || string.IsNullOrWhiteSpace(command.Command))
            return JsonError(400, "missing command");

        return ExecuteHandler(command.Command, command.Args, recordLog: true, request.RemoteIp);
    }

    private RemoteHttpResponse ExecuteHandler(string commandName, Dictionary<string, JsonElement>? args, bool recordLog, string remoteIp)
    {
        var handler = _handlers.FirstOrDefault(h => h.CanHandle(commandName));
        if (handler == null)
            return JsonError(404, $"unknown command: {commandName}");

        RemoteControlResponse result;
        try
        {
            // 指令串行执行：避免并发关机/重启竞态
            _commandLock.Wait();
            try { result = handler.Execute(commandName, args); }
            finally { _commandLock.Release(); }
        }
        catch (Exception ex)
        {
            result = RemoteControlResponse.Fail(ex.Message);
        }

        // 审计日志：只记 POST /api/command 指令（状态查询 3s 轮询会刷满日志，不记）
        if (recordLog)
        {
            _logs.Enqueue(new LogEntry(commandName, DateTime.Now, remoteIp));
            while (_logs.Count > MaxLogEntries) _logs.TryDequeue(out _);
        }
        return Json(result);
    }

    private RemoteHttpResponse HandleEvents()
        => Json(RemoteControlResponse.Ok(Logs.Select(l => new
        {
            l.Command,
            Time = l.Time.ToString("HH:mm:ss"),
            l.FromIp
        })));

    // ==================== 控制页 ====================

    private RemoteHttpResponse HtmlResponse()
    {
        if (_htmlBytes == null)
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("Toolbox.Plugins.Resources.control_panel.html");
            if (stream == null)
                return JsonError(500, "control panel resource missing");
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            _htmlBytes = memory.ToArray();
        }
        return new RemoteHttpResponse
        {
            ContentType = "text/html; charset=utf-8",
            Body = Encoding.UTF8.GetString(_htmlBytes)
        };
    }

    // ==================== 响应工具 ====================

    private static RemoteHttpResponse Json(RemoteControlResponse content)
        => new() { Body = JsonSerializer.Serialize(content, JsonOptions) };

    private static RemoteHttpResponse JsonError(int statusCode, string error)
        => new()
        {
            StatusCode = statusCode,
            Body = JsonSerializer.Serialize(RemoteControlResponse.Fail(error), JsonOptions)
        };
}

/// <summary>审计日志条目（指令、时间、来源 IP）</summary>
public sealed record LogEntry(string Command, DateTime Time, string FromIp);
