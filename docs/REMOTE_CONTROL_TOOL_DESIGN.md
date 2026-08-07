# RemoteControlTool 设计文档（局域网远程控制）

> 状态：设计阶段 | 日期：2026-08-07 | 目标平台：Windows 10 19041+ / .NET 9 / WPF
> 本页为 RemoteControlTool 的唯一设计事实源；实现过程中如变更决策，同步回填本节与第 13 章决策记录。

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
- 按钮：启动 / 停止服务；复制访问地址；**复制当前 Token**（Token 展示见 FR-4）
- 服务生命周期（避免后台残留监听）：
  - 面板提供手动"停止"按钮
  - 工具内容 UI 的 **Unloaded 事件**（切换工具/关闭时触发）自动 Stop()
  - ⚠️ 注意：主窗口对工具内容有缓存复用机制，Stop 须挂在 Unloaded 事件，
    不能依赖工具实例销毁（实例可能一直存活）
  - **Stop() 必须幂等**（Unloaded 在 TransitioningContentControl 过渡中可能多次触发，
    重复调用无害）；Start() 在已运行时调用同样无害（忽略或返回当前状态）
  - **服务状态显示以 `RemoteControlServer.IsRunning` 为唯一事实源**，
    面板不得用本地 bool 字段记忆状态——缓存复用/重建下 UI 与实际状态永不脱节

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
| status | 电池容量/状态 | ✅ 公共 Helper：`SystemInfoHelper.GetBatteryInfo()`（2026-08-08 新增；首页时钟卡片与远程控制页共用） |
| network | MAC / 网关 / DNS | 插件内自实现：`NetworkInterface.GetAllNetworkInterfaces()` 等 .NET 公共 API |
| network | 公网 IP | 插件内自实现：HttpClient 请求公共 IP 服务（try-catch + 超时，离线时降级返回 null 不阻塞） |

状态页面支持**自动刷新**（前端 3s 轮询或 SSE），便于远程盯屏。

### FR-4 访问认证
- 启动服务时自动生成 Token（也可手动指定），控制页首次访问需输入 Token
- 未认证请求返回 401；认证后浏览器会话内有效（Cookie/会话缓存）
- 状态查询与指令执行共用同一 Token
- **Token 可见性**：服务启动后，工具面板必须展示当前 Token 并提供"复制"按钮
  （用户需凭它登录控制页；仅服务停止后重新启动时重新生成才隐藏）
- **手动指定 Token 仅当前会话有效**：不落盘，服务重启后回到随机生成；
  保持"Token 不落盘"安全红线（用户如需固定 Token，每次启动前手动填写）

### FR-5 审计与容错
- 控制页显示最近操作记录（指令、时间、来源 IP）
- 危险指令（关机/重启）服务端强制 confirm 强校验（前端直发恒带，见 7.4）
- 所有异常返回友好 JSON 错误，不崩溃

## 3. 技术选型

### 3.1 HTTP 服务器：TcpListener 手写极简 HTTP（推荐，已评估）
- **选型结论**：`TcpListener` 手写极简 HTTP 协议，零第三方依赖、无系统配置
- **评估（对比 HttpListener）**：
  | 维度 | HttpListener | TcpListener 手写 |
  |------|-------------|-----------------|
  | URL ACL 权限 | ❌ 监听非 localhost 需 http.sys 保留权，普通权限可能 Access Denied（需 netsh/管理员） | ✅ 无此限制，普通权限即可监听 |
  | 服务骨架代码量 | 少 | 需自研协议解析（一次性成本，见 10 章 M1） |
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
├── RemoteControlTool.cs              ITool 实现：工具面板 UI（状态/端口/URL/Token/按钮）
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
  ├── CreateContent() → 工具面板 UI（状态灯、端口输入、启动/停止、Token 展示与复制、地址列表、操作日志）
  ├── 持有 RemoteControlServer 实例，状态灯绑定 IsRunning（唯一事实源）
  └── Unloaded 事件 → Stop()（幂等）；服务回调更新 UI 一律经 Dispatcher（遵循项目线程安全惯例）

RemoteControlServer（持有 IRemoteHttpServer，主/备方案可切换）
  ├── Start(port, token) / Stop()（Stop 幂等，重复调用无害）
  ├── IsRunning 只读属性（面板与服务器状态的唯一事实源）
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
      - 可测性：PowerCommandHandler 构造注入命令执行器委托
        （默认实现 = PowerActions；测试注入记录型假执行器，避免真实关机，见 9 章）
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
{ "command": "shutdown", "args": { "delaySeconds": 60, "confirm": true } }

POST /api/command
{ "command": "lock" }

GET /api/status
→ { "success": true, "data": { "cpu": 12.5, "memoryTotalGB": 32, "memoryUsedGB": 18.2,
    "disks": [ {"name": "C:", "freeGB": 210, "totalGB": 512} ],
    "uptime": "3 天 4 小时", "ipv4": "192.168.1.100" } }
```

> 说明：uptime 使用 `SystemInfoHelper.FormatUptime` 中文格式（与项目内其他工具一致）；
> 内存 used = total − available（计入 standby/文件缓存，数值略高于任务管理器"使用中"，语义等价）；
> ipv4 为**公网 IP**（优先公共 `SystemInfoHelper.GetPublicIPv4Async`，失败私有兜底局域网地址，30s 缓存防 1.2s 轮询阻塞，2026-08-08）。

> 注意：`shutdown` / `restart` 请求必须携带 `args.confirm: true`（服务端强制校验，见 7.4），
> 上例为完整形态。

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
│  🔌 电源控制                  │
│  [1分][5分][10分][30分][1时]   │  快捷定时关机（按钮直发）
│  [立即关机] [重启电脑]         │  危险按钮（红）
│  [自定义分钟 + 定时关机]       │  与原 ShutdownTool 一致的输入
│  [取消关机]                    │
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
1. 首次打开 `/` → 前端检测会话无效（调 GET /api/status 收到 401）→ 显示 Token 输入遮罩
2. 输入 Token → `POST /api/auth` → 服务端校验 → 返回会话 Cookie（内存中）
3. 后续请求自动带 Cookie，免重复输入；页面刷新保持会话

## 7. 安全设计

### 7.1 Token 认证
- Token 默认 `Guid.NewGuid().ToString("N")[..16]` 随机生成，可在面板手动指定
- 仅存内存，不落盘（避免明文 Token 泄露到 settings.json）；手动指定的 Token 仅当前会话有效（见 FR-4）
- 服务启动后面板展示当前 Token + 复制按钮（用户登录控制页的唯一凭证）
- 认证失败计数：连续失败 5 次锁定 30s（防暴力枚举）

### 7.2 会话
- 认证成功后下发随机会话 ID（内存字典维护，过期 8h，惰性清理过期条目）
- 前端以 Cookie 存储，HttpOnly 防 XSS 读取

### 7.3 网络边界
- 默认监听 `0.0.0.0`，但服务**默认关闭**，用户显式点"启动"才监听
- 工具页面显示所有局域网 IP 访问地址，便于识别
- 建议文档注明：Windows 防火墙首次会弹窗，需允许专用网络（信任局域网）

### 7.4 危险指令保护
- 控制页对定时关机/立即关机/重启弹**自绘确认框**（Toolbox 风格深色模态，不调用浏览器原生 confirm；
  2026-08-08 决策：确认弹窗必须有）
- 服务端对 `shutdown`/`restart` 同时强制要求 `args.confirm=true`，否则拒绝——
  **双层防线**：前端确认（人类确认）+ 服务端 confirm 校验（CSRF 纵深，配合 X-Requested-With 头，
  跨站请求即便带 Cookie 也无法同时伪造自定义头与 confirm 语义）
- 其余指令（锁屏/睡眠/关显示器/重启资源管理器/取消关机）直发，无需确认

### 7.5 CSRF 防护
- 所有写操作（/api/command、/api/devices/kick）要求自定义头 `X-Requested-With: RemoteControl`，
  浏览器跨站表单无法伪造该头

### 7.6 认证与会话原理（2026-08-08 记录）

**"已认证"的判定**：服务端会话字典 + Cookie，**不依赖 IP**。

1. **认证**：设备 `POST /api/auth {token}` → 服务端定长比较校验（`FixedTimeEquals`，防时序侧信道；
   失败计数按来源 IP 隔离，5 次锁 30s）→ 通过后生成随机会话 ID（Guid.N）→ 写入内存字典
   `_sessions`（sessionId → { 过期时间 8h、来源 IP、设备名(UA 解析)、最后活跃 }）→
   响应 `Set-Cookie: rc_session=xxx; HttpOnly; SameSite=Lax; Max-Age=28800`
2. **保持**：浏览器自动保存 Cookie，后续请求自动携带
3. **校验**：受保护路由（status/command/events/devices/kick）先解析 Cookie → 查 `_sessions` →
   存在且未过期 → 放行并刷新 LastActive；否则 401 → 前端弹登录遮罩
4. **8h 过期**：服务端条目过期 + 浏览器端 Cookie Max-Age 同时到期；条目由惰性清理回收
   （仅当字典 >200 条时批量清过期，防无限增长）
5. **"已连接设备"列表** = `_sessions` 中有效会话按 IP 聚合（🟢）；**"曾连接设备"表**
   = 认证成功那一刻写入 `remote-control.json` 的 `KnownDevices`（IP/UA 设备名/首连/最后，
   永不自动过期，直到手动移除）
6. **自动填密钥**：`GET /` 时查 `KnownDevices`，IP 命中 → HTML 注入真实密钥 → 页面自动填入；
   "踢出/移除"= 删除该 IP 全部会话 + 从 `KnownDevices` 移除（撤销自动填充）

**IP 地址变化的影响（常见场景：DHCP 重新分配）**：
- 已记录条目**不会失效**（json 中的旧 IP 记录不自动清理、不因超时删除）
- 但设备拿到**新 IP** 后，新 IP 不在 `KnownDevices` → 自动填密钥**不生效**，
  需手动输入密钥重新认证；认证成功后新 IP 被记录
- 旧 IP 条目成为"僵尸记录"（显示在曾连接列表），直到：该 IP 被别的设备登录（同 IP 刷新为该设备记录）、
  或手动移除
- **结论**：设备记录以 IP 为键，换 IP 即"失联"——这是当前实现的已知局限；
  后续可选优化：设备表改用浏览器持久化设备 ID（localStorage 生成 + 认证时上报）作为主键，
  IP 变化仍可识别（本期不做）

## 8. 与现有代码集成点

| 现有模块 | 集成方式 |
|---------|---------|
| `ToolRegistry` | 无需改动，反射自动发现新插件 |
| `ITool` / `ToolCategory` | RemoteControlTool 实现 ITool，分类归入 `ToolCategory.Network`（🌐 网络与开发，2026-08-08 调整） |
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
| PowerCommandHandlerTests | 指令参数校验（延迟秒数范围、confirm 必填）、指令映射正确性 |
| StatusCommandHandlerTests | 状态快照字段完整性（可注入假数据源） |
| LanAddressHelperTests | 局域网 IP 列表解析 |

> **避免真实关机/重启进测试**：`PowerCommandHandler` 构造注入命令执行器委托，
> 测试注入记录型假执行器（记录调用参数、返回成功），断言只验证参数校验与指令映射，
> 不触发任何真实 Process.Start。
>
> **HTTP 层测试**：TcpHttpServer 用随机高位端口 + 本机回环地址（127.0.0.1）起停真实监听；
> 断言请求/响应解析与错误分支（畸形请求 400、超大 body 413、未认证 401）。
> 不引入 Moq 等新依赖——项目零第三方依赖惯例，假实现即测试桩。

## 10. 实施步骤（里程碑与执行顺序）

> 执行顺序：各里程碑内步骤按编号顺序执行（前一步是后一步的前置）；
> 里程碑之间 M1 → M2 → M3 → M4 → M5 串行，M4 与 M5 可在 M3 完成后并行。

### M1 服务骨架（半天）

| 步骤 | 内容 | 前置 |
|------|------|------|
| 1.1 | 定义 `IRemoteHttpServer` 接口：Start/Stop/IsRunning + 请求-响应抽象（HttpRequest/HttpResponse 最小模型） | 无 |
| 1.2 | 实现 `TcpHttpServer`（主）：TcpListener 异步循环、请求行/头/体解析、URL 解码、错误分支（400/413）、单请求限时 | 1.1 |
| 1.3 | 定义统一响应模型 `RemoteControlResponse` + 请求模型 `RemoteControlRequest` + `SystemStatusSnapshot`（Models/） | 1.1 |
| 1.4 | 实现 `HttpListenerServer`（备，先写好备用）：GetContextAsync 映射到同一抽象 | 1.1 |
| 1.5 | 认证中间件 + Token 生成 + 会话字典（含失败锁定、惰性过期清理） | 1.3 |
| 1.6 | 工具面板 UI：状态灯（绑定 IsRunning）、端口输入、启动/停止按钮、Token 展示与复制、地址列表（LanAddressHelper） | 1.5 |

### M2 指令与状态（半天）

| 步骤 | 内容 | 前置 |
|------|------|------|
| 2.1 | `PowerActions`（私有）：shutdown.exe（/s /t、/r /t、/a）、taskkill+explorer；抽命令执行器委托（构造注入点） | 无 |
| 2.2 | `PowerCommandHandler`：指令参数校验（延迟秒数范围、confirm 必填）+ 委托映射 | 2.1 |
| 2.3 | `SystemMetricsHelper`（私有）：CPU（GetSystemTimes）+ 内存总量/可用（GlobalMemoryStatusEx） | 无 |
| 2.4 | `NetworkDetailHelper`（私有）：MAC/网关/DNS + 公网 IP（HttpClient 超时+降级） | 无 |
| 2.5 | `StatusCommandHandler`：聚合 SystemInfoHelper + 私有 Helper，字段完整性 | 2.3、2.4 |
| 2.6 | `RemoteControlServer` 路由接线：/api/command → PowerCommandHandler、/api/status → StatusCommandHandler、/api/events → 日志缓冲 | 1.5、2.2、2.5 |

### M3 控制页（1 天）

| 步骤 | 内容 | 前置 |
|------|------|------|
| 3.1 | HTML 骨架 + 深色主题 + 认证遮罩（Token 输入 → /api/auth → Cookie） | 1.5 |
| 3.2 | 快捷操作组（锁屏/睡眠/关显示器/重启资源管理器，一键直发） | 2.6 |
| 3.3 | 电源控制组（定时关机下拉/立即关机/重启/取消关机，二次 confirm + `args.confirm=true`） | 2.6 |
| 3.4 | 状态仪表（CPU/内存/磁盘/运行时长/IPv4，3s 轮询，401 时回认证遮罩） | 2.6 |
| 3.5 | 操作日志区（最近 20 条） | 2.6 |
| 3.6 | 内嵌资源接入（EmbeddedResource 注册 + GET / 返回 HTML） | 3.1-3.5 |

### M4 加固与测试（半天）

| 步骤 | 内容 | 前置 |
|------|------|------|
| 4.1 | 安全加固核对：暴力锁定、CSRF 头校验、header ≤16KB/body ≤1MB 限长、Stop 幂等 | 2.6、3.6 |
| 4.2 | 单元测试（见 9 章）：4 个测试类 + 假执行器 + 回环端口 HTTP 测试 | 2.6、3.6 |
| 4.3 | 全量回归：既有测试保持全绿（数量以 changelog 为准，本页不复述） | 4.2 |

### M5 文档与验证（半天）

| 步骤 | 内容 | 前置 |
|------|------|------|
| 5.1 | 使用说明：防火墙放行、局域网访问、Token 查看/复制、AP 隔离排查 | 4.1 |
| 5.2 | Windows 实机冒烟测试清单（多设备浏览器访问、并发轮询、畸形请求、断网降级） | 4.1 |
| 5.3 | 实机冒烟：通过 → 确认主方案；不通过 → 按 13 章切换备方案 HttpListenerServer 并复测 | 5.2 |
| 5.4 | 决策回填：冒烟结论 + 任何偏离设计处回填本页与 13.4 | 5.3 |

## 11. 风险与限制

| 风险 | 影响 | 缓解 |
|------|------|------|
| 开发环境无法编译验证 WPF/实机行为 | 交付后需 Windows 实机验证 | 代码严格按项目现有风格；提供冒烟清单（5.2） |
| 防火墙未放行 | 其他设备连不上 | 文档注明首次弹窗放行；工具页提供排查提示 |
| 路由器 AP 隔离 | 同 Wi-Fi 但设备不通 | 提示检查 AP 隔离设置 |
| Token 明文传输（HTTP） | 局域网可嗅探 | 局域网信任模型 + Token 随机强；后续可加 HTTPS（自签）预留 |
| 危险指令误触 | 电脑被关机 | 服务端 confirm 字段强校验（防 CSRF/伪造）；按钮直发与本地工具一致 |
| 端口冲突 | 启动失败 | 启动时检测占用并提示换端口 |
| 手写 HTTP 协议健壮性 | 畸形请求/超大 body 拖垮服务 | 解析严格限长（header ≤16KB、body ≤1MB）、单请求超时、异常就地捕获返回 4xx/5xx；若仍不可靠，切换备方案 HttpListenerServer（见 13） |

## 12. 后续扩展（预留）

- **HTTPS 自签证书 / SSE / WebSocket 实时状态**：TcpListener 手写协议扩展成本高，
  届时评估换用 Kestrel（ASP.NET Core）或第三方库承载 HTTP 层
- **文件浏览/传输**：新增 /api/files 路由（手写协议需支持 multipart 或分块上传）
- **屏幕截图**：CopyFromScreen + 图片流返回
- **开机自启服务**：结合 `AppSettings.AutoStart`（已确认存在于 Core，[AppSettings.cs:57](Toolbox.Core/Services/AppSettings.cs#L57)），可选随 Toolbox 启动自动监听

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
- 备方案实现体量小（单文件），在 M1 阶段一并写好，避免切换时临时开发

### 13.4 决策记录

- 2026-08-07：选型定为 TcpListener 手写（主），HttpListener 为备；
  待 M4/M5 Windows 实机冒烟测试后确认或切换，结果回填本节
- 2026-08-08（二次产品决策）：分类改 Network（🌐 网络与开发）；电源控制布局对齐原工具
  （快捷按钮组 + 自定义分钟 + 自绘控件，去浏览器原生 select）；
  **自绘确认弹窗保留**（危险指令前端确认 + 服务端 confirm 强校验双层防线）；
  设置合并为**单一 json**（remote-control.json：密钥/端口/开关 + 曾连接设备表，弃 remote-control-devices.json）；
  JSON 路径约定：%LOCALAPPDATA%/Toolbox/remote-control.json
- 2026-08-07（评审修订）：分类定死 System（⚙️ 系统维护）；Token 面板展示与复制；
  手动指定 Token 仅当前会话；睡眠不要求二次确认（低风险）；
  PowerCommandHandler 注入执行器委托（测试不真关机）；
  测试不引入 Moq（零第三方依赖惯例）
- 2026-08-07（实现偏离 2 处，已同步代码与测试）：
  ① 公网 IP 不再自实现——复用公共 `SystemInfoHelper.GetPublicIPv4Async`
  （双源 fallback + 5s 超时，与"网络信息"工具共用同一份实现；NetworkDetailHelper 仅做 MAC/网关/DNS）
  ② 进程执行器委托注入点落在 `PowerActions` 构造（PowerCommandHandler 经 PowerActions 传递），
  测试用 `new PowerActions(fakeExecutor)` 注入，语义不变（仍为"绝不触发真实关机"）
- 2026-08-07（实现完成）：M1-M4 全部落地，测试 133/133 全绿（原 101 + 新增 32）；
  实机冒烟待执行（清单见 docs/remote-control-tool-usage.md）
- 2026-08-07（全链路 review 修复，测试 141/141）：对抗性审查发现并修复——
  ① P1 会话字典并发 → ConcurrentDictionary（多设备轮询+登录并发安全）
  ② P1 系统动作（锁屏/睡眠）注入点缺失，测试会真锁屏 → PowerCommandHandler 增加 systemAction 委托注入
  ③ 认证锁定改按来源 IP 隔离（防单设备爆破拖累全员）+ Token 固定时长比较（防时序侧信道）
  ④ Host 校验：仅接受 IP 字面量/localhost（防 DNS rebinding）
  ⑤ 畸形请求回应 413（不再静默断连）+ 并发连接上限 64（防慢连接洪水）
  ⑥ 备方案 HttpListenerServer 补 body 限长 + 循环读满
  ⑦ 公网 IP 30s 缓存（避免 network 指令锁内同步等外部请求阻塞关机指令）
  ⑧ 控制页改 textContent 渲染（杜绝 XSS 注入面）；500 不透出内部异常；Set-Cookie 显式 SameSite=Lax
  ⑨ 补 FR-1 缺失的"复制访问地址"按钮；运行中锁定端口/Token 输入
  ⑩ 补裸 socket 畸形请求/超大 body/DNS rebinding 测试（原设计 9 章测试盲区）
- 2026-08-08（主人 15 项产品决策，测试 149/149）：
  ① 控制页 ⏻ 图标换 🔌（字体渲染兼容）
  ② 控制页二次确认改自绘深色模态框（替代浏览器原生 confirm，Toolbox 风格）
  ③ ghost 次级按钮样式明确化（暗底 + 描边，修复"颜色缺失"观感）
  ④ 全链路文案 Token → 密钥
  ⑤ 密钥输入框常驻深色提示（留空自动生成）
  ⑥ 修复"当前密钥"标签与值重复
  ⑦ 访问地址改逐行可复制列表（多网卡每行独立复制）
  ⑧ **服务常驻**：服务提升为工具级静态单例，取消 Unloaded 自动停止；
     切换工具/前台后台均不中断，仅手动停止或关闭 Toolbox 终止（取代原 FR-1 防残留监听约定）
  ⑨ 无密钥时自动生成随机密钥的开关（remote-control.json 持久化）
  ⑩ **密钥明文落盘**（用户确认接受权衡）：remote-control.json 记录上次密钥/端口，输入框回填；
     打破原 7.1"密钥不落盘"红线（参照 passwords.json 先例）
  ⑪ **已连接设备管理**：会话升级 SessionInfo（IP/设备名/最后活跃）；
     GET /api/devices + POST /api/devices/kick（CSRF 校验）；面板与控制页双端展示/踢出
  ⑫ **记住设备 + 自动填密钥**：认证成功即记录设备（remote-control-devices.json）；
     已记录 IP 访问控制页时服务端注入密钥自动填入（踢出即撤销）；信任局域网 IP 难伪造
  ⑬ 状态灯移入"服务配置"卡片、启动按钮下方
  ⑭ 设置页新增"远程控制"卡片：自动启动服务开关 + 默认端口 + 默认密钥
     （授权扩展 Core AppSettings：AutoStartRemoteControl / RemoteControlDefaultPort / RemoteControlDefaultKey）
  ⑮ 设备表/设置测试注入临时路径，不污染真实 LocalAppData
