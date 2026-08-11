# 工具 · 局域网文件传输（FileTransferTool）

- **分类**：🌐 网络与开发（Network）
- **文件**：`Toolbox.Plugins/FileTransferTool.cs`
- **配套服务**：`Toolbox.Plugins/Services/FileTransferService.cs`（静态单例）
- **架构关系**：与远程控制**同端口同页面**（用户决策 2026-08-11）——路由挂在 `RemoteControlServer`，
  页面并入 `control_panel.html`，传输层流式化见 `docs/REMOTE_CONTROL_TOOL_DESIGN.md`（唯一设计事实源，2026-08-11 条目）

## 功能

- 手机→电脑上传：控制页选文件（多选）→ raw body + `X-File-Name` 头流式上传，64KB 分块直写接收目录，不落内存
- 电脑→手机下载：面板登记待发送文件 → 控制页共享清单（3s 轮询）点击下载，流式响应
- 接收目录可配（持久化 file-transfer.json，默认 `DataDir/Received`）；重名自动 `name (1).ext` 编号
- 面板传输记录：双向实时进度（100ms 节流）、✅/❌ 状态；服务未启动时引导至远程控制页启动

## 实现要点

- 传输层流式旁路：`IRemoteHttpServer.StreamingRoutes` 登记 `POST /api/transfer/upload` 后，
  `TcpHttpServer`/`HttpListenerServer` 对该路由不预读 body（豁免 1MB 限长/30s 整体超时），
  裸流经 `RemoteHttpRequest.RawStream` 交业务层；60s 空闲超时
- 网络读写**禁用 CancellationToken 逐块取消**（取消 socket 异步操作有 SAEA 异步回收竞态，
  会抛 "socket operation is already in progress"，2026-08-11 实测修复）：
  上传同步 `Read` + `ReadTimeout`（SO_RCVTIMEO），下载同步 `Write` + `WriteTimeout`（SO_SNDTIMEO）
- `Expect: 100-continue` 支持：部分手机浏览器/WebView 上传先等确认才发 body，
  `TcpHttpServer` 解析头后先回 `100 Continue`（HttpListener 由系统托管天然支持）
- 下载经 `RemoteHttpResponse.BodyStream` 分块写出，`Content-Disposition: filename*=UTF-8''` 兼容中文名
- 文件名净化：`Path.GetFileName` 剥离路径（防 `../../` 穿越）+ 非法字符替换 + 空名兜底 `unnamed`；磁盘剩余空间预检
- 认证零新增：路由全部走 `RequireSession`，上传额外校验 `X-Requested-With`（CSRF）
- 下载进度：`ProgressReportStream` 包装 FileStream，随读取上报；提前释放报"对端中断"

## 测试覆盖

- `FileTransferTests.cs`：净化（含路径穿越）/去重/上传下载回环字节一致（>1MB 验证流式旁路）/
  重名编号/401/403/404/`Expect: 100-continue` 临时响应/非流式路由 1MB 上限回归

## 依赖（公共共享类）

| 类 | 用途 |
|----|------|
| RemoteControlServer / IRemoteHttpServer | 路由挂载与流式传输层 |
| JsonSettingsFile | 接收目录原子写入持久化 |
| ToolNavigation | 服务未启动时跳转远程控制页 |
| ThemeColors / GlowCardMarker | UI 一致性 |

## 相关文档

- 插件层总览 → [../04-plugins.md](../04-plugins.md)
- 设计文档 → docs/REMOTE_CONTROL_TOOL_DESIGN.md（2026-08-11 条目）
- 远程控制工具 → [remote-control-tool.md](remote-control-tool.md)
