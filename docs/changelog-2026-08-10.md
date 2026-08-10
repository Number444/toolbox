# Toolbox 更新日志（2026-08-10）

> 当日工作日志：开机自启静默启动功能（v1.6.2）+ 全链路审查修复 + 设置页排版整理 + 发布。

---

## 1. 新增：开机自启静默启动

自启时不再弹出主界面，后台驻留托盘，悬浮窗照常显示；从托盘（或再次双击 exe）恢复主窗口。

- **触发链路**：注册表自启值改为 `"Toolbox.exe" --autostart`；App.OnStartup 解析参数，命中且 `AutoStartSilent`（新设置项，默认开）→ 静默启动
- **初始化拆分**：托盘/悬浮窗等后台服务不依赖窗口显示（`InitializeBackgroundServices`，幂等）；DWM 圆角/布局等 UI 初始化延迟到首次显示（`OnWindowLoaded`）
- **兜底**：静默时托盘创建失败 → 回退显示主窗口，应用不会不可达
- **唤起机制**：命名事件 `ToolboxShowRequestEvent`——静默驻留/最小化到托盘期间双击 exe，第二实例经单实例互斥锁发信号，第一实例恢复显示（修复既有缺陷：隐藏窗口 `SetForegroundWindow` 无效导致双击无反应）
- **设置页**：新增"自启时不显示主窗口（后台托盘驻留）"开关（ToggleSwitch，依赖开机自启开启）
- **升级迁移**：`EnsureStartupRegistryValue` 启动自检——旧版裸路径值 / 安装路径变化自动重写为新格式（后台执行不占启动路径），升级用户无需手动重新开关自启

## 2. 设置页排版整理

- 首卡片间隔统一：行间距 16px、开关与说明 4px（原 8/16/24px 混用 + 说明零间距）

## 3. 审查修复

- `RestoreFromTray` 加 `_isShuttingDown` 防御（退出过程中收到唤起信号不再 Show 关闭中的窗口）
- `ActivateExistingInstance` 按窗口可见性分流：可见 → 置前；不可见（驻留中）→ 事件唤起

## 4. 发布

- 版本号更新 **v1.6.1 → v1.6.2**（setup/ToolboxSetup.iss + 底部状态栏）
- 产物：`setup/Toolbox_Setup.exe`（self-contained 单文件）
- 已 push 云端

---

## v1.7.0 发布（同日，版本迭代）

> 界面动效体系大版本：统一动画语言 + 启动遮罩 + 交互优化。

### 变更

- 全局按钮叠层动画（hover 白 10%/按下黑 12% + 微缩放）
- 开关/复选框轨道颜色 200ms 渐变
- 滚动条动态展开（16px 容器、四档颜色、拖拽 Accent 绿）
- ComboBox 弹层自绘入场（弃系统 Slide）
- 工具切换两段式过渡（200ms 退场 + 400ms 进场，标题区对向动画）
- 设置层进出过渡（进入 360ms，退出 150ms）与工具退场串行对齐
- 启动遮罩：纯黑无缝衔接首帧 + 图标文字 2s 淡入上滑
- Acrylic 延迟到首帧后启用（消除启动毛玻璃闪）
- 修复复制卡顿（剪贴板写入移入专用 STA 线程）
- 搜索框美化（矢量放大镜/focus 绿线/无结果空态）
- 工具切换时内容区滚动回顶
- 架构文档动效体系同步 + README 更新

---

## 5. Debug / 正式版实例隔离

> 开发调试版与正式安装版可同时运行、互不干扰——编译 Debug 不再需要先关闭正在跑的正式版。

- **新增 `Toolbox.Core/Services/AppPaths.cs`**：`#if DEBUG` 编译期常量统一承载隔离点（Release 产物行为零变化）
- **隔离清单**：
  - 单实例 Mutex：`ToolboxSingleInstanceMutex` ↔ `...Debug`（Debug 与正式版可共存；正式版之间仍互斥）
  - 唤起事件：`ToolboxShowRequestEvent` ↔ `...Debug`（防唤起信号串扰弹窗）
  - 数据目录：`%LocalAppData%/Toolbox` ↔ `Toolbox-Debug`（AppSettings / audioflow.json / remote-control.json / crash.log 全隔离）
  - 远程控制默认端口：8090 ↔ 8091（避免双实例同时监听冲突）
  - 自启注册表值名：`Toolbox` ↔ `Toolbox-Debug`（防互抢开机自启）
- **保留共享**：PaddleOCR 引擎目录 `%LocalAppData%/Toolbox/PaddleOCR`（避免 Debug 版重复下载）
- 测试 161/161 全绿

---

## 6. 切回前台动画（后台 → 前台入场）

窗口从后台恢复（最小化还原 / 托盘恢复 / 静默驻留唤起）时播放，`PlayReturnAnimations()`。

- **触发**：`_wasBackground` 标志（最小化 / 关闭到托盘 / 静默驻留三处置位）+ `Activated` 触发；普通失焦不触发（不监听 Deactivated）
- **节奏**：左侧保持从左向右滑入淡入（400ms/位移 420ms）；右侧仿左侧完整动画，方向从下到上（淡入 + 大幅上滑 Y 100→0）。曾试错峰 + QuinticEase 优化版（左侧先落位、右侧延迟 200ms），实测不理想已回退
- **首帧闪烁修复**（录屏逐帧验证，Kimi 协作定稿）：还原瞬间先显完整界面→闪到起点。① 后台置位点同步置起点状态 + 还原显示前再设一次（双保险，DWM 缓存完整帧问题由此暴露）；② **清场帧拦截**（最终方案）：`MinimizePreClearHook` 拦下 `WM_SYSCOMMAND/SC_MINIMIZE` → 置起点 → `WaitForRenderedFramesAsync(2)` 等清场帧提交进 DWM 缓存 → 程序化最小化。还原时 DWM 亮出的是空窗帧，WPF 从起点播动画，无缝衔接。自建标题栏最小化按钮改调 `PreClearThenMinimizeAsync`（不走 SC_MINIMIZE）。边界：Win+D/Win+M 不经 SC_MINIMIZE，该路径首帧闪烁保留（系统级限制），由 StateChanged 兜底播动画；托盘/静默路径无 DWM 缓存问题（Hide 销毁表面）
- **动效精调**（现代动效三原则）：① 位移曲线 KeySpline(0.16,1,0.3,1)（Apple/Fluent emphasized，起步干脆收尾柔，替代 CubicEase 偏硬尾段）；② 淡入先于位移完成（280/300ms ≈ 位移 55%）；③ 右侧 60ms 微错峰。左移 500ms / 右移 540ms。**审查修正**：参数提升为类级常量（Return* 前缀）与 `SetReturnStartState` 同源，防起点/动画 From 双写漂移；`PreClearThenMinimizeAsync` 加 try-finally 防标志位卡死

## 7. AppPaths 审查修复

- 审查 AppPaths.cs 重构：4 个消费文件原值与 Release 常量逐一一致 ✓
- 修复 3 处硬编码残留：`RemoteControlSettings` 反序列化兜底端口、`RemoteControlTool` 自动启动端口兜底（均改 `AppPaths.DefaultRemotePort`）、测试断言跟随常量（Debug=8091/Release=8090）
- 架构文档同步：02-main-app 发布流程警告（必须 `-c Release`）、07-ui-system 切回动画小节 + 动效总表

---

## 8. v1.7.1 发布（版本迭代）

> 切回前台入场动画 + 还原首帧闪烁修复（Kimi 清场帧方案）+ AppPaths Debug/Release 四重隔离。

### 变更

- **切回前台动画**：窗口从后台恢复（最小化/托盘/静默唤起）时，左侧工具栏从左向右滑入淡入、右侧工具页从下到上大幅上滑淡入；`_wasBackground` 三处置位 + `Activated` 触发，普通失焦不触发
- **动效精调**（现代动效三原则）：KeySpline(0.16,1,0.3,1) emphasized 曲线、淡入先于位移完成（≈55% 时长）、右侧 60ms 微错峰；参数集中在类级常量区与起点状态同源
- **还原首帧闪烁修复**：SC_MINIMIZE 清场帧拦截（`MinimizePreClearHook` 拦最小化 → 置起点 → 等 2 帧提交 DWM 缓存 → 程序化最小化），还原时 DWM 亮出空窗帧无缝衔接；标题栏按钮/任务栏/托盘三入口一致
- **AppPaths 四重隔离**：Debug 构建数据目录/互斥名/唤起事件/端口（8091）/注册表值名与正式版完全隔离，开发调试版可与正式版同时运行互不污染
- 审查修正：参数类级常量防双写漂移、`PreClearThenMinimizeAsync` try-finally 防标志卡死、发布流程文档化（`-c Release` 硬性要求）
- 版本号 v1.7.0 → **v1.7.1**（iss + 状态栏）；产物 `setup/Toolbox_Setup.exe`（self-contained 单文件）
- 测试 161/161 全绿

---

## 9. v1.7.2 发布（交互增强）

> 界面细节质感批次：ToolTip 深色样式 + 侧栏按压缩放反馈 + 分组展开子项错落淡入 + 设置层进出缩放。

### 变更

- **ToolTip 深色样式**（App.xaml 隐式样式）：BgCard 底 + BorderSubtle 描边 + 6px 圆角 + 150ms 淡入，替换系统浅色默认样式（影响面仅 PasswordGenerator 搜索框一处，应用内菜单均走 ThemedMenuWindow）
- **侧栏按压反馈**：导航项/分组头按下缩至 0.96（90ms EaseOut）、松开回弹（180ms），滑出取消点击兜底复位；`EnsureMutableScale` 防冻结实例（DataTemplate 中 `po:Freeze="False"` 不生效，静态 XAML 默认冻结 Freezable）
- **分组展开错落淡入**：展开时子项 25ms 间隔逐条淡入（150ms/条，与 Clip 揭示同向生长）；`GroupItemStaggerMs`=0 关闭，调参集中类级常量区
- **设置层进出缩放**：进入 0.96→1 / 退出→0.98（与 ComboBox 绽放同语言）；显式 From 防 HoldEnd 锁值、复位先清动画再写本地值
- **光晕与动画同步**：播放 RenderTransform 动画时边缘发光光圈不再定格——`IsAnyGlowTrackedAnimationActive` 闸门检测受跟踪动画（`IsAnimated` 查询），活跃期逐帧 `Refresh()`；补全清单遗漏的 NavPane/ContentScrollViewer 淡入。决策：动画完成后 HoldEnd 保持期闸门持续刷新至 60fps 为预期（现代电脑非性能浪费），**不做**动画完成清时钟——曾尝试清时钟方案引入组件消失/工具切换卡死两个回归，已整体回退（2026-08-11）。回归清单第 10 节含快速连点回归项
- **启动遮罩两段式入场**：① 文字从下方 40px 滑入（EaseOut 先快后慢）+ 渐渐淡入（EaseIn 缓慢亮起，同步 1.3s；淡入不可用 EaseOut——DWM 首帧跳帧会吞掉前段飙升）→ ② 图标从文字背后（完全透明）淡入滑入文字左侧（位移 48px = 1.5×图标宽，文字同步右移 22px 让位，三者同刻起止 1.3s→2.3s）→ 定格 0.5s → 遮罩淡出（2.8s–3.3s）；调参集中 OnWindowLoaded 常量区
- 版本号 v1.7.1 → **v1.7.2**（iss + 状态栏）；产物 `setup/Toolbox_Setup.exe`（self-contained 单文件）
- 测试 161/161 全绿
