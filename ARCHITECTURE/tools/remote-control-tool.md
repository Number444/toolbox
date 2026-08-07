# 工具 · 局域网远程控制（RemoteControlTool）

- **分类**：🌐 网络与开发（Network）
- **文件**：`Toolbox.Plugins/RemoteControlTool.cs`
- **配套服务**：`Toolbox.Plugins/Services/RemoteControlServer.cs`、`TcpHttpServer.cs`（主方案）、`HttpListenerServer.cs`（备方案）、`IRemoteHttpServer.cs`、`RemoteControlSettings.cs`
- **设计文档**：docs/REMOTE_CONTROL_TOOL_DESIGN.md（唯一设计事实源）

## 功能

- 局域网内浏览器远程控制：关机/重启/锁屏/睡眠/关显示器/重启资源管理器/取消关机
- 状态监控：CPU/内存/磁盘/电池/运行时长/公网 IP（控制页 1.2s 刷新）
- 密钥认证（防爆破按 IP 隔离）+ 免登录模式（无密钥启动）
- 服务常驻（静态单例，切换工具不中断）；设备管理（自动填密钥/踢出）；操作审计
- 控制页可远程关闭 Toolbox 进程

## 实现要点

- 手写极简 HTTP（TcpListener，零第三方依赖）；备方案 HttpListenerServer（URL ACL 需管理员）
- 认证/CSRF/Host 白名单/限长/限并发/单请求超时；危险指令服务端 confirm 强校验
- 指令层：PowerCommandHandler（注入假执行器防真关机）/ StatusCommandHandler（只读指令锁外执行）
- 私有 Helper：PowerActions / SystemMetricsHelper（P/Invoke 插件内声明）/ NetworkDetailHelper / LanAddressHelper
- 设置单一 json（remote-control.json：密钥/端口/开关 + 设备表，7 天活跃期）

## 已知问题

> 状态以 `docs/待解决-2026-07-31.md` 为准（唯一事实源），本页不复述具体条目以避免状态过时。

## 测试覆盖

- `RemoteControlServerTests.cs`（路由/认证/CSRF/设备/免登录/关闭按钮/畸形请求）
- `PowerCommandHandlerTests.cs`、`StatusCommandHandlerTests.cs`、`LanAddressHelperTests.cs`、`RemoteControlSettingsTests.cs`

## 依赖（公共共享类）

| 类 | 用途 |
|----|------|
| SystemPowerHelper | 锁屏 / 睡眠 / 关显示器 |
| SystemInfoHelper | 内存/磁盘/运行时长/公网 IP/电池 |
| ThemeColors / GlowCardMarker | UI 一致性 |
| AppSettings（Core） | 自动启动/默认端口/默认密钥 |

## 相关文档

- 插件层总览 → [../04-plugins.md](../04-plugins.md)
- 设计文档 → docs/REMOTE_CONTROL_TOOL_DESIGN.md
- 使用说明与冒烟清单 → docs/remote-control-tool-usage.md
