using System.IO;

namespace Toolbox.Plugins.Services;

/// <summary>
/// 最小 HTTP 请求模型 —— 由服务器实现解析填充，交 RemoteControlServer 路由。
/// </summary>
public sealed class RemoteHttpRequest
{
    /// <summary>请求方法（GET/POST）</summary>
    public string Method { get; init; } = "";

    /// <summary>路径（不含 query，如 /api/status）</summary>
    public string Path { get; init; } = "";

    /// <summary>query 参数（Key 大小写不敏感）</summary>
    public Dictionary<string, string> Query { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>请求头（Key 大小写不敏感）</summary>
    public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>请求体（UTF-8 文本，含 Content-Length 时按限长读取；流式路由恒为 ""）</summary>
    public string Body { get; init; } = "";

    /// <summary>
    /// 流式请求体的裸流（仅命中 <see cref="IRemoteHttpServer.StreamingRoutes"/> 的请求有值）：
    /// 传输层不预读 body，由处理器自行从流消费 <see cref="RawLength"/> 字节；
    /// 流所有权归服务器，处理器返回后即被释放。
    /// </summary>
    public Stream? RawStream { get; init; }

    /// <summary>流式请求体长度（Content-Length，仅流式路由有值）</summary>
    public long RawLength { get; init; }

    /// <summary>来源 IP（审计日志用）</summary>
    public string RemoteIp { get; init; } = "";
}

/// <summary>
/// 最小 HTTP 响应模型 —— 由路由层构造，服务器实现负责编码发送。
/// </summary>
public sealed class RemoteHttpResponse
{
    public int StatusCode { get; init; } = 200;

    public string ContentType { get; init; } = "application/json; charset=utf-8";

    /// <summary>响应体（UTF-8 文本；BodyStream 有值时忽略）</summary>
    public string Body { get; init; } = "";

    /// <summary>
    /// 流式响应体（大文件下载用）：有值时服务器按 <see cref="BodyStreamLength"/>
    /// 声明 Content-Length 并分块拷贝发送，读完后由服务器负责释放。
    /// </summary>
    public Stream? BodyStream { get; init; }

    /// <summary>流式响应体长度（Content-Length，仅 BodyStream 有值时使用）</summary>
    public long? BodyStreamLength { get; init; }

    /// <summary>附加响应头（如 Set-Cookie），服务器实现按需添加标准头</summary>
    public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// HTTP 服务器抽象 —— 主方案 TcpHttpServer / 备方案 HttpListenerServer 的统一契约。
/// 传输层只负责：解析请求 → 回调 <see cref="RequestHandler"/> → 编码响应；路由/认证与传输层无关。
/// </summary>
public interface IRemoteHttpServer
{
    bool IsRunning { get; }

    /// <summary>实际监听端口（Start 传 0 时为系统分配端口，测试用）</summary>
    int ActualPort { get; }

    /// <summary>请求处理委托（由 RemoteControlServer 注入路由）</summary>
    Func<RemoteHttpRequest, RemoteHttpResponse>? RequestHandler { get; set; }

    /// <summary>
    /// 流式路由表（"METHOD /path" 格式，Start 前登记）：命中的请求不预读 body、
    /// 不受默认 body 限长约束，裸流经 <see cref="RemoteHttpRequest.RawStream"/> 交处理器（大文件上传用）。
    /// </summary>
    ISet<string> StreamingRoutes { get; }

    /// <summary>开始监听（幂等：已运行时直接返回）</summary>
    void Start(int port);

    /// <summary>停止监听（幂等：未运行时直接返回）</summary>
    void Stop();
}
