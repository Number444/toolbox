using System.Net;
using System.Text;

namespace Toolbox.Plugins.Services;

/// <summary>
/// ★ 备方案：.NET 内置 HttpListener 实现（设计文档第 13 章切换条件满足时启用）。
/// 注意：监听非 localhost 端口需要 URL ACL（管理员执行
/// `netsh http add urlacl url=http://+:8090/ user=Everyone`），这是选主方案 TcpHttpServer 的原因。
/// </summary>
public sealed class HttpListenerServer : IRemoteHttpServer
{
    /// <summary>请求体长度上限（与主方案 TcpHttpServer 对齐，防超大 body 拖垮进程）</summary>
    private const int MaxBodyBytes = 1024 * 1024;

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    public bool IsRunning => _listener?.IsListening == true;

    public int ActualPort { get; private set; }

    public Func<RemoteHttpRequest, RemoteHttpResponse>? RequestHandler { get; set; }

    public void Start(int port)
    {
        if (IsRunning) return; // 幂等

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://*:{port}/");
        listener.Start();
        _listener = listener;
        ActualPort = port;

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => LoopAsync(listener, _cts.Token));
    }

    public void Stop()
    {
        var listener = _listener;
        if (listener == null) return; // 幂等

        _listener = null;
        _cts?.Cancel();
        try { listener.Stop(); } catch (Exception) { /* 已停止，忽略 */ }
        _cts?.Dispose();
        _cts = null;
    }

    private async Task LoopAsync(HttpListener listener, CancellationToken ct)
    {
        while (listener.IsListening && !ct.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception)
            {
                return; // Stop() 触发或异常 → 退出循环
            }

            _ = Task.Run(() => HandleAsync(context));
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            // 超大 body：直接 413（与主方案语义一致）
            if (context.Request.ContentLength64 > MaxBodyBytes)
            {
                await WriteResponseAsync(context.Response,
                    new RemoteHttpResponse { StatusCode = 413, Body = "body too large" });
                return;
            }

            var request = new RemoteHttpRequest
            {
                Method = context.Request.HttpMethod,
                Path = context.Request.Url?.AbsolutePath ?? "/",
                Query = BuildQuery(context.Request.QueryString),
                Headers = BuildHeaders(context.Request.Headers),
                Body = await ReadBodyAsync(context.Request),
                RemoteIp = context.Request.RemoteEndPoint?.Address.ToString() ?? ""
            };

            var response = RequestHandler?.Invoke(request)
                ?? new RemoteHttpResponse { StatusCode = 404, Body = """{"success":false,"error":"no handler"}""" };

            await WriteResponseAsync(context.Response, response);
        }
        catch (Exception)
        {
            // 单请求异常就地吞掉（与主方案一致）
        }
        finally
        {
            try { context.Response.Close(); } catch (Exception) { }
        }
    }

    private static Dictionary<string, string> BuildQuery(System.Collections.Specialized.NameValueCollection qs)
    {
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in qs.AllKeys)
        {
            if (key != null) query[key] = qs[key] ?? "";
        }
        return query;
    }

    private static Dictionary<string, string> BuildHeaders(System.Collections.Specialized.NameValueCollection headers)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in headers.AllKeys)
        {
            if (key != null) result[key] = headers[key] ?? "";
        }
        return result;
    }

    private static async Task<string> ReadBodyAsync(HttpListenerRequest request)
    {
        if (request.ContentLength64 <= 0) return "";
        var buffer = new byte[request.ContentLength64];
        // 循环读满：单次 ReadAsync 不保证读满 ContentLength（审查 P2-5）
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await request.InputStream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset));
            if (read == 0) break;
            offset += read;
        }
        return Encoding.UTF8.GetString(buffer, 0, offset);
    }

    private static async Task WriteResponseAsync(HttpListenerResponse response, RemoteHttpResponse content)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(content.Body);
        response.StatusCode = content.StatusCode;
        response.ContentType = content.ContentType;
        response.ContentLength64 = bodyBytes.Length;
        foreach (var (key, value) in content.Headers)
            response.Headers[key] = value;
        if (bodyBytes.Length > 0)
            await response.OutputStream.WriteAsync(bodyBytes);
    }
}
