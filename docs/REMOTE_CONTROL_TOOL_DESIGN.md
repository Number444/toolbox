# RemoteControlTool 设计文档（局域网远程控制）

> 状态：设计阶段 | 日期：2026-08-07 | 目标平台：Windows 10 19041+ / .NET 9 / WPF

## 1. 概述

在 Toolbox 内新增插件 `RemoteControlTool`：在**本地局域网**监听一个 HTTP 端口，
同一局域网内的手机/电脑用**浏览器**访问控制页，通过点击按钮向本机发送指令，
实现远程控制系统操作与查看状态。

### 目标
- 零安装：控制端只需浏览器（手机/电脑均可）
- 即开即用：工具页一键启动/停止服务，显示访问地址
- 安全可控：Token 认证 + 仅监听局域网 + 显式启用才开放

### 非目标（本期不做）
- 不做公网穿透 / 互联网远程（无公网 IP 场景留待后续）
- 不做文件传输、屏幕截图、远程桌面（可扩展预留）
- 不做专用客户端 App（Web 页即客户端）

## 2. 功能需求

### FR-1 服务管理
- 工具面板显示：服务状态（已停止/运行中）、监听端口（可配置，默认 8090）、访问 URL
- 按钮：启动 / 停止服务；复制访问地址
- 服务生命周期（避免后台残留监听）：
  - 面板提供手动"停止"按钮
  - 工具内容 UI 的 **Unloaded 事件**（切换工具/关闭时触发）自动 Stop()
  - ⚠️ 注意：主窗口对工具内容有缓存复用机制，Stop 须挂在 Unloaded 事件，
    不能依赖工具实例销毁（实例可能一直存活）

### FR-2 系统控制指令
| 指令 | 行为 | 实现方式（遵守项目修改规范） |
|------|------|---------|
| shutdown | 定时关机（可选延迟秒数） | 插件内自实现：`shutdown.exe /s /t {秒}`（标准系统命令） |
| restart | 重启电脑 | 插件内自实现：`shutdown.exe /r /t 0` |
| lock | 锁屏 | ✅ 公共 Helper：`SystemPowerHelper.Lock()` |
| sleep | 睡眠 | ✅ 公共 Helper：`SystemPowerHelper.Sleep()` |
| monitor_off | 关闭显示器 | ✅ 公共 Helper：`SystemPowerHelper.TurnOffMonitor()` |
| explorer_restart | 重启资源管理器 | 插件内自实现：`taskkill /f /im explorer.exe` + `explorer.exe` |
| cancel_shutdown | 取消已排程关机 | 插件内自实现：`shutdown.exe /a` |

> **边界说明**：`ShutdownTool` / `QuickSystemTool` 为其他工具类，其内部方法不在可复用清单内；
> 本工具一律不调用、不修改、不抽取它们的私有逻辑。

### FR-3 状态监控
| 指令 | 返回内容 | 实现方式（遵守项目修改规范） |
|------|---------|---------|
| status | 内存占用百分比 | ✅ 公共 Helper：`SystemInfoHelper.GetMemoryUsagePercent()` |
| status | 内存总量/可用 | 插件内自实现（公共类未提供）：本工具私有 `SystemMetricsHelper`（GlobalMemoryStatusEx P/Invoke） |
| status | 磁盘各分区剩余、系统运行时长、IPv4 | ✅ 公共 Helper：`SystemInfoHelper.GetUptime() / GetDriveSpace() / GetLocalIPv4()` |
| status | CPU 占用 | 插件内自实现（公共类未提供）：本工具私有 `SystemMetricsHelper`（GetSystemTimes P/Invoke，零第三方依赖） |
| network | MAC / 网关 / DNS | 插件内自实现：`NetworkInterface.GetAllNetworkInterfaces()` 等 .NET 公共 API |
| network | 公网 IP | 插件内自实现：HttpClient 请求公共 IP 服务（try-catch + 超时） |

状态页面支持**自动刷新**（前端 3s 轮询或 SSE），便于远程盯屏。

### FR-4 访问认证
- 启动服务时自动生成 Token（也可手动指定），控制页首次访问需输入 Token
- 未认证请求返回 401；认证后浏览器会话内有效（Cookie/会话缓存）
- 状态查询与指令执行共用同一 Token

### FR-5 审计与容错
- 控制页显示最近操作记录（指令、时间、来源 IP）
- 危险指令（关机/重启）需在页面二次确认
- 所有异常返回友好 JSON 错误，不崩溃

## 3. 技术选型

### 3.1 HTTP 服务器：TcpListener 手写极简 HTTP（推荐，已评估）
- **选型结论**：`TcpListener` 手写极简 HTTP 协议，零第三方依赖、无系统配置
- **评估（对比 HttpListener）**：
  | 维度 | HttpListener | TcpListener 手写 |
  |------|-------------|-----------------|
  | URL ACL 权限 | ❌ 监听非 localhost 需 http.sys 保留权，普通权限可能 Access Denied（需 netsh/管理员） | ✅ 无此限制，普通权限即可监听 |
  | 服务骨架代码量 | 少（~50 行） | 需自研协议解析（~200 行一次性成本） |
  | 协议健壮性 | 系统级，成熟 | 需自行处理边界（畸形请求/超时/超大 body） |
  | 依赖 | 无 | 无 |
  | 后续 HTTPS/SSE/WebSocket | 较易扩展 | 成本高，届时换 Kestrel（见 12） |
- **结论**：本项目端点收敛（1 页面 + 5 API + JSON + Cookie，无大文件/流），
  TcpListener 手写成本可控，且规避 URL ACL 权限问题，更符合"即开即用"目标
- 替代方案对比：
  - HttpListener：功能强，但 URL ACL 权限问题影响零配置目标（见上表）
  - ASP.NET Core Minimal API / Kestrel：功能最强，但引入依赖大，单文件发布体积增加
  - WebSocket：实时性好，但本期状态轮询够用，复杂度不划算

### 3.2 前端：内嵌单页 HTML（无构建链）
- 单个 `control_panel.html` 字符串资源嵌入插件程序集（EmbeddedResource）
- 纯原生 HTML + CSS + JS（fetch 调用 API），无框架无 CDN（离线可用）
- 深色主题，与 Toolbox 视觉一致（#1C1C1C / #2D2D2D / #76B580 色板）

### 3.3 端口与监听地址
- 默认监听 `http://0.0.0.0:8090/`（局域网可达）
- 端口可配置（工具面板），冲突时启动报错提示

## 4. 架构设计

### 4.1 新增文件（均在 Toolbox.Plugins/）

```
Toolbox.Plugins/
├── RemoteControlTool.cs              ITool 实现：工具面板 UI（状态/端口/URL/按钮）
├── Services/
│   ├── IRemoteHttpServer.cs          HTTP 服务抽象接口（主/备方案统一契约，切换零成本）
│   ├── TcpHttpServer.cs              ★ 主方案：TcpListener 手写 HTTP（默认启用）
│   ├── HttpListenerServer.cs         ★ 备方案：HttpListener（URL ACL 需 netsh/管理员，见 13）
│   └── RemoteControlServer.cs        核心：路由 + 认证 + 指令分发（持有 IRemoteHttpServer）
├── Handlers/                         指令执行器（与 UI 解耦，便于测试）
│   ├── PowerCommandHandler.cs        关机/重启/锁屏/睡眠/关显示器/取消关机
│   ├── StatusCommandHandler.cs       CPU/内存/磁盘/网络信息聚合
│   └── IRemoteCommandHandler.cs      指令处理器接口
├── Models/
│   ├── RemoteControlRequest.cs       请求模型 {command, args, token}
│   ├── RemoteControlResponse.cs      统一响应模型 {success, data, error}
│   └── SystemStatusSnapshot.cs       状态快照模型
├── Resources/
│   └── control_panel.html            内嵌 Web 控制页（EmbeddedResource）
└── Helpers/
    ├── LanAddressHelper.cs           获取局域网 IP 列表（用于展示访问地址）
    ├── PowerActions.cs               ★ 本工具私有：shutdown.exe / taskkill+explorer 封装（仅 RemoteControlTool 内部使用，不与其他工具共享）
    ├── NetworkDetailHelper.cs        ★ 本工具私有：MAC/网关/DNS/公网 IP 获取（仅 RemoteControlTool 内部使用）
    └── SystemMetricsHelper.cs        ★ 本工具私有：CPU 占用（GetSystemTimes）+ 内存总量/可用（GlobalMemoryStatusEx）；
                                          P/Invoke 插件内私有声明（遵循 SystemPowerHelper 先例，不改 Core 的 Win32Native）
```

### 4.2 类职责

```
RemoteControlTool (ITool)
  ├── CreateContent() → 工具面板 UI（状态灯、端口输入、启动/停止、地址列表、操作日志）
  └── 持有 RemoteControlServer 实例，页面关闭时 Stop()

RemoteControlServer（持有 IRemoteHttpServer，主/备方案可切换）
  ├── Start(port, token) / Stop()
  ├── IRemoteHttpServer 抽象：解析请求 → 返回统一 HTTP 响应（主/备实现互换）
  │   ├── TcpHttpServer（主）：TcpListener 异步循环：AcceptTcpClient → 每连接一 Task → 读取/解析 HTTP 请求
  │   │   请求解析（自研极简）：请求行（方法/路径/query）+ 请求头 + Content-Length 体 + URL 解码；
  │   │   畸形/超大请求返回 400/413，连接异常就地捕获不崩溃；单请求限时防挂起
  │   └── HttpListenerServer（备）：GetContextAsync()，URL ACL 需 netsh/管理员（见 13）
  ├── 路由表：GET / → 控制页 | POST /api/auth → 验证 Token | POST /api/command → 执行指令 | GET /api/status → 状态快照 | GET /api/events → 操作日志
  ├── 认证中间件：白名单路径（控制页/静态资源）除外，其余校验 Token（解析 Cookie 会话）
  └── 线程安全：指令执行串行化（SemaphoreSlim），避免并发关机/重启竞态

PowerCommandHandler / StatusCommandHandler
  └── 实现 IRemoteCommandHandler.Execute(command, args) → RemoteControlResponse
      - Power（锁屏/睡眠/关显示器）: 调用公共 Helper SystemPowerHelper
      - Power（关机/重启/取消关机/重启资源管理器）: 调用本工具私有 PowerActions
        （shutdown.exe / taskkill+explorer 标准命令，Process.Start + try-catch）
      - Status: 调用公共 Helper SystemInfoHelper（内存百分比/磁盘/运行时长/IPv4）
        + 本工具私有 NetworkDetailHelper（MAC/网关/DNS/公网 IP）
        + 本工具私有 SystemMetricsHelper（CPU 占用/内存总量）
```

### 4.3 复用边界（遵守项目修改规范）

**✅ 允许使用（规范《可复用类目录》列出的公共共享类）：**
- `SystemPowerHelper`：Lock / Sleep / TurnOffMonitor
- `SystemInfoHelper`：GetMemoryUsagePercent / GetUptime / GetDriveSpace / GetLocalIPv4
- `ThemeColors`（颜色常量）、`GlowCardMarker`（卡片发光）、`ConfirmDialog`（确认弹窗）、
  `JsonSettingsFile`（独立 JSON 设置，用于 remote-control.json 持久化端口等配置）
- `Toolbox.Core` 公共基础设施（ToolCategory 分类常量等）

**❌ 禁止：**
- 调用 `ShutdownTool` / `QuickSystemTool` / `NetworkInfoTool` 等**其他工具类**的内部方法
  （无论私有还是公开，均不属于本工具的公共接口边界）
- 修改 / 抽取 / 重构其他工具类代码（含提取公共静态类），保持各工具自包含
- 修改 Core 层（除非未来规范允许）

**本工具私有实现（仅 RemoteControlTool 内部，不与其他工具共享）：**
- `PowerActions`：shutdown.exe（/s /t、/r /t、/a）、taskkill+explorer 重启资源管理器
- `NetworkDetailHelper`：MAC/网关/DNS（NetworkInterface API）、公网 IP（HttpClient）
- `SystemMetricsHelper`：CPU 占用（GetSystemTimes）+ 内存总量/可用（GlobalMemoryStatusEx）
- **P/Invoke 策略**：插件内私有声明（遵循 SystemPowerHelper 先例，规范 3.8.1），
  不修改 Core 的 Win32Native（保持"禁止改 Core"约束）；不引入第三方 NuGet 包
- 全部 Process.Start 按规范六包裹 try-catch，失败返回用户可读错误

## 5. API 设计

### 5.1 路由表
| 方法 | 路径 | 认证 | 说明 |
|------|------|:----:|------|
| GET | `/` | 无 | 返回控制页 HTML |
| POST | `/api/auth` | 无 | Body `{token}`；成功返回 Set-Cookie + 会话令牌 |
| POST | `/api/command` | 有 | Body `{command, args}`；执行系统指令 |
| GET | `/api/status` | 有 | 返回系统状态快照（CPU/内存/磁盘/网络） |
| GET | `/api/events` | 有 | 返回最近操作日志 |

### 5.2 统一响应格式
```json
{ "success": true, "data": { }, "error": null }
{ "success": false, "data": null, "error": "invalid token" }
```

### 5.3 指令示例
```json
POST /api/command
{ "command": "shutdown", "args": { "delaySeconds": 60 } }

POST /api/command
{ "command": "lock" }

GET /api/status
→ { "success": true, "data": { "cpu": 12.5, "memoryTotalGB": 32, "memoryUsedGB": 18.2,
    "disks": [ {"name": "C:", "freeGB": 210, "totalGB": 512} ],
    "uptime": "3d 04:12:33", "ipv4": "192.168.1.100" } }
```

## 6. Web 控制页设计（control_panel.html）

### 布局（深色主题，移动端优先）
```
┌──────────────────────────────┐
│  Toolbox 远程控制        [🔒] │  顶部栏：标题 + 连接状态
│  已连接：192.168.1.100       │
├──────────────────────────────┤
│  ⚡ 快捷操作                 │
│  [锁屏] [睡眠] [关显示器]     │  一键指令（低风险，无需确认）
│  [重启资源管理器]             │
├──────────────────────────────┤
│  ⏻ 电源控制（二次确认）       │
│  [定时关机 ▾ 60s]  [立即关机]  │  危险操作弹确认框
│  [重启电脑]  [取消关机]        │
├──────────────────────────────┤
│  📊 系统状态（3s 自动刷新）    │
│  CPU ████░░ 12.5%            │  进度条 + 数值
│  内存 ██████░ 18.2/32 GB     │
│  磁盘 C: ███████ 210/512 GB  │
│  运行时长 / IPv4              │
├──────────────────────────────┤
│  📜 操作日志                  │
│  14:02:33 lock ✓ from 手机    │  最近 20 条
└──────────────────────────────┘
```

### 认证流程
1. 首次打开 `/` → 前端检测会话无效 → 显示 Token 输入遮罩
2. 输入 Token → `POST /api/auth` → 服务端校验 → 返回会话 Cookie（内存中）
3. 后续请求自动带 Cookie，免重复输入；页面刷新保持会话

## 7. 安全设计

### 7.1 Token 认证
- Token 默认 `Guid.NewGuid().ToString("N")[..16]` 随机生成，可在面板手动指定
- 仅存内存，不落盘（避免明文 Token 泄露到 settings.json）
- 认证失败计数：连续失败 5 次锁定 30s（防暴力枚举）

### 7.2 会话
- 认证成功后下发随机会话 ID（内存字典维护，过期 8h）
- 前端以 Cookie 存储，HttpOnly 防 XSS 读取

### 7.3 网络边界
- 默认监听 `0.0.0.0`，但服务**默认关闭**，用户显式点"启动"才监听
- 工具页面显示所有局域网 IP 访问地址，便于识别
- 建议文档注明：Windows 防火墙首次会弹窗，需允许专用网络（信任局域网）

### 7.4 危险指令保护
- 关机/重启/睡眠在控制页二次确认（前端 confirm）
- 服务端对 `shutdown`/`restart` 强制要求 `args.confirm=true`，否则拒绝（防 CSRF/误触）

### 7.5 CSRF 防护
- 所有写操作（/api/command）要求自定义头 `X-Requested-With: RemoteControl`，
  浏览器跨站表单无法伪造该头

## 8. 与现有代码集成点

| 现有模块 | 集成方式 |
|---------|---------|
| `ToolRegistry` | 无需改动，反射自动发现新插件 |
| `ITool` / `ToolCategory` | RemoteControlTool 实现 ITool，分类归入 `⚙️ 系统维护`（或 `🌐 网络与开发`） |
| `SystemPowerHelper` | ✅ 公共 Helper 直接调用（锁屏/睡眠/关显示器） |
| `SystemInfoHelper` | ✅ 公共 Helper 直接调用（内存/磁盘/运行时长/IPv4） |
| `ShutdownTool` | ❌ 不调用、不修改；关机/重启/取消关机由本工具私有 `PowerActions` 自实现 |
| `QuickSystemTool` | ❌ 不调用、不修改；重启资源管理器由本工具私有 `PowerActions` 自实现（taskkill + explorer） |
| `App.xaml` 主题 | 控制页 HTML 使用同色板（#1C1C1C/#2D2D2D/#76B580），视觉统一 |
| `docs/TOOL_DEVELOPMENT_GUIDELINE.md` | 遵循其新工具开发规范（文件/命名空间/UI/异常处理） |

## 9. 测试方案（Toolbox.Tests）

| 测试类 | 覆盖 |
|--------|------|
| RemoteControlServerTests | 路由分发、Token 认证成功/失败/锁定、CSRF 头校验 |
| PowerCommandHandlerTests | 指令参数校验（延迟秒数范围、confirm 必填） |
| StatusCommandHandlerTests | 状态快照字段完整性（可注入假数据源） |
| LanAddressHelperTests | 局域网 IP 列表解析 |

> 说明：TcpListener 绑定真实端口在 CI 上可行（用随机高位端口 + 本机回环测试），
> 或抽 `IRemoteCommandHandler` 用 Moq 模拟，避免真实关机指令进入测试。

## 10. 实施步骤（里程碑）

### M1 服务骨架（半天）
- IRemoteHttpServer 抽象 + TcpHttpServer（主方案）：启动/停止、请求解析、统一响应模型
- HttpListenerServer（备方案）实现（约 80 行，先写好备用）
- 认证中间件 + Token 生成 + 会话字典
- 工具面板 UI：状态灯、端口、启动/停止、地址列表

### M2 指令与状态（半天）
- PowerCommandHandler：锁屏/睡眠/关显示器（公共 SystemPowerHelper）
  + 关机/重启/取消关机/重启资源管理器（本工具私有 PowerActions，标准命令）
- StatusCommandHandler：内存/磁盘/运行时长/IPv4（公共 SystemInfoHelper）
  + CPU 占用 + 网络详情（本工具私有实现）

### M3 控制页（1 天）
- control_panel.html：认证遮罩 + 快捷操作 + 电源控制（二次确认）+ 状态仪表 + 日志
- 内嵌资源 + 加载渲染
- 移动端适配

### M4 加固与测试（半天）
- 暴力锁定、CSRF 头、操作日志、异常兜底
- xUnit 测试 + 80/80 baseline 回归保持全绿

### M5 文档与验证（半天）
- 使用说明（防火墙、局域网访问、Token 查看）
- Windows 实机冒烟测试清单（多设备浏览器访问、并发轮询、畸形请求）
- 冒烟通过 → 确认主方案；不通过 → 按第 13 章切换备方案 HttpListenerServer 并复测

## 11. 风险与限制

| 风险 | 影响 | 缓解 |
|------|------|------|
| 沙盒无法编译 WPF | 交付后需 Windows 实机验证 | 代码严格按项目现有风格；提供冒烟清单 |
| 防火墙未放行 | 其他设备连不上 | 文档注明首次弹窗放行；工具页提供排查提示 |
| 路由器 AP 隔离 | 同 Wi-Fi 但设备不通 | 提示检查 AP 隔离设置 |
| Token 明文传输（HTTP） | 局域网可嗅探 | 局域网信任模型 + Token 随机强；后续可加 HTTPS（自签）预留 |
| 危险指令误触 | 电脑被关机 | 二次确认 + confirm 字段校验 |
| 端口冲突 | 启动失败 | 启动时检测占用并提示换端口 |
| 手写 HTTP 协议健壮性 | 畸形请求/超大 body 拖垮服务 | 解析严格限长（header ≤16KB、body ≤1MB）、单请求超时、异常就地捕获返回 4xx/5xx；若仍不可靠，切换备方案 HttpListenerServer（见 13） |

## 12. 后续扩展（预留）

- **HTTPS 自签证书 / SSE / WebSocket 实时状态**：TcpListener 手写协议扩展成本高，
  届时评估换用 Kestrel（ASP.NET Core）或第三方库承载 HTTP 层
- **文件浏览/传输**：新增 /api/files 路由（手写协议需支持 multipart 或分块上传）
- **屏幕截图**：CopyFromScreen + 图片流返回
- **开机自启服务**：结合 AppSettings.AutoStart，可选随 Toolbox 启动自动监听

## 13. 备选方案与切换策略

### 13.1 主/备方案

| 方案 | 实现 | 状态 |
|------|------|------|
| 主方案 | `TcpHttpServer`：TcpListener 手写极简 HTTP | 默认启用 |
| 备方案 | `HttpListenerServer`：.NET 内置 HttpListener | 触发条件满足时切换 |

### 13.2 切换触发条件（满足任一即评估切换）

在 Windows 实机冒烟测试（M4/M5）中出现以下任一问题：
1. 手写协议解析在真实网络环境出现崩溃 / 挂起 / 连接泄漏
2. 控制端浏览器兼容问题（部分设备请求无法正常解析/响应异常）
3. 并发/性能不满足需求（多设备同时轮询状态时明显卡顿）
4. 实现中发现边界情况过多，维护成本显著超出预期

### 13.3 切换成本控制

- 预先抽象 `IRemoteHttpServer`（4.1 / 4.2）：主/备实现仅替换 HTTP 传输层，
  **路由 / 认证 / Handler / 控制页完全复用**，切换成本≈替换 1 个实现类
- 切换后差异点：
  - 监听 `http://0.0.0.0:{port}/` 需 URL ACL：管理员执行一次
    `netsh http add urlacl url=http://+:8090/ user=Everyone`
    （或工具面板检测到 AccessDenied 时给出提示引导）
  - 请求/响应语义与手写版对齐（Content-Length、UTF-8、Set-Cookie 一致）
- 备方案实现约 80 行，在 M1 阶段一并写好，避免切换时临时开发

### 13.4 决策记录

- 2026-08-07：选型定为 TcpListener 手写（主），HttpListener 为备；
  待 M4/M5 Windows 实机冒烟测试后确认或切换，结果回填本节
