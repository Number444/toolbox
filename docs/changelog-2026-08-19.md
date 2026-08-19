# changelog-2026-08-19

> 当日工作日志：任务栏嵌入式音乐控件（新功能）+ v1.8.1 发布 + 启动动画质感优化。

## 1. 任务栏嵌入式音乐控件（TaskbarMusicWidget）

- **任务栏内嵌控件**：`TaskbarMusicWindow` 以子窗口形式嵌入 Shell_TrayWnd（SetParent），纯显示：封面 32×32（圆角 6 + 描边）、歌名/歌手双行、超宽跑马灯滚动（FormattedText 精确测宽）、播放态角标（Segoe Fluent Icons E768/E769）
- **位置**：左/右两档（右键菜单切换）；左侧紧贴任务栏左边缘 + 8 DIP 间距（随 DPI 缩放）；可拖动，8px 阈值区分点击/拖拽，松手吸附
- **主题自适应**：`TaskbarThemeHelper` 读注册表 SystemUsesLightTheme，文字/悬停色随任务栏明暗切换
- **无播放时自动隐藏**（可配置），悬停 15% 圆角高亮

## 2. 弹出媒体卡片（TaskbarMediaPopupWindow）

- 点击控件弹出 340×120 Mica 卡片（DWM 系统背板 + WindowChrome，边框交 DWM 原生绘制）：封面 88×88 投影、歌名/歌手垂直居中、播放控制按钮行（上一首/播放暂停/下一首）
- **按钮走公共样式**：新增 `MediaTransportButtonStyle`（App.xaml），严格复刻全局 StandardButtonStyle 叠层法（HoverOverlay 白 0.10 / PressOverlay 黑 0.12 + 缩放 0.97）
- **打开动画（FluentFlyout 同款路径，参照 github.com/unchihugo/FluentFlyout 源码）**：Show 前预置于最终位置下方 20px → 直接动画 Window.Top 上移 + Window.Opacity 0→1（300ms CubicEase EaseOut），内容层模糊 8→0 以 450ms 更缓收尾；收拢 180ms EaseIn 镜像后 Hide
  - 踩坑记录：WPF 逐帧改窗口 Top/Height 会引起 DWM 重算背板 → 抽搐；Window.Opacity<1 会切 WS_EX_LAYERED 杀死 Acrylic（但 FluentFlyout 实证 Opacity 回到 1 后背板恢复，最终采用此路径）；面纱机制（v6）证明只能遮材质闪现、遮不住窗口存在闪现，已删除
- **点击外部自动关闭**（Activate + Deactivated 链路，400ms 重开防抖），Esc 关闭

## 3. 退出/重启健壮性修复

- X 关闭主窗口（后台留任务栏）时卡片窗口残留 → 进程不退、重启唤起崩溃（RestoreFromTray 对已关闭窗口 Show）
- 修复：`MainWindow.OnClosed` 兜底关闭卡片窗口 + `_isClosed` 守卫

## 4. 设置项清理

- 移除"任务栏控件内嵌播放按钮"设置（`TaskbarWidgetControlsEnabled` 全链删除：AudioflowSettings / NeteaseMusicTool 复选框 / TaskbarThemeHelper 冗余色）——控件纯显示，播放控制集中在弹出卡片

## 5. v1.8.1 发布

- 版本号 v1.8.0 → **v1.8.1**（`setup/ToolboxSetup.iss` + 底部状态栏）
- 产物 `setup/Toolbox_Setup.exe`（Release 自包含单文件，ISCC LZMA2）；已 push 云端（b2104f6）

## 6. 启动动画质感优化（方向文件：`docs/待删除/启动动画优化方向-2026-08-19.md`，已执行归档）

**约束**：时序骨架（1.3s 弹入 / 1.0s 图标 / 0.5s 停留 / 0.5s 淡出，共 3.3s）不动——
黑遮罩停留是盖住 WPF 启动初期无法绘制黑屏期的功能需求（Four 确认）。只改质感：

- **曲线统一**：品牌阶段全部位移（文字弹入/图标滑入/文字让位）从 CubicEase EaseOut
  换为 `KeySpline(0.16,1→0.3,1)` emphasized（改用 DoubleAnimationUsingKeyFrames 承载），
  与 8-10 切回动画设计语言统一；文字/图标淡入保留 EaseIn（跳帧期实测结论不动）
- **模糊渐变（BlurEffect.Radius 动画）**：文字 6→0（阶段 1 同步）、图标 5→0（阶段 2 同步）、
  内容区 6→0（揭示时 650ms）——"从虚焦到聚焦"；QuadraticEase EaseOut 对焦曲线；
  **完成后 Effect 一律置 null 摘除**（不留常驻离屏渲染开销/文字发虚）。
  XAML 侧 ContentRoot（主体内容区 Grid）/ MaskTitle / MaskIcon 各挂 Radius=0 实例，起点值全在代码调参区
  （⚠️ 此半径动画方案已被第 7 节 VisualBrush 覆盖层方案替代——Radius 整数量化跳档）
- **淡出 × 入场编排**：遮罩仍纯黑时 `SetReturnStartState()` 藏好左右栏起点 →
  淡出 +120ms 后 `PlayContentEntrance(entranceStart)` 开播（抽出共用方法，切回动画 = baseTime Zero
  同源调用）——动作前段隔半透黑幕隐约可见、后段清晰（"浮出"感），消除硬切静态界面
- **顺手修复**：静默启动首次托盘唤起时 `_wasBackground=true` 会让 Activated 触发
  PlayReturnAnimations 与冷启动入场叠加互踩 → OnWindowLoaded 编排前置 false
- 新增调参常量：MaskFadeStartMs/MaskFadeMs/EntranceTitleBlurRadius/EntranceIconBlurRadius/
  ContentBlurRadius/ContentBlurMs/ContentEntranceDelayMs
- 改动文件：`MainWindow.xaml`（3 个 BlurEffect + ContentRoot 命名）、`MainWindow.xaml.cs`；编译 0 错误
- **调参（同日，Four 反馈模糊不可见）**：根因 = 淡入 EaseIn（前暗后亮）× 模糊 EaseOut（前快散完）
  曲线错开——元素亮起时模糊已散完。对焦曲线改 **CubicEase EaseIn**（扛着不散、最亮时对焦），
  半径 6/5/6 → 8/6/7，对焦时长比落定位移多 200ms（"亮起→落定→对上焦"三段可辨），
  文字 1500ms / 图标 1200ms / 内容 900ms

## 7. 模糊对焦跳档排查与修复（BlurEffect 整数量化）

- **现象**（Four 反馈）：模糊渐显曲线不稳，可见明显"切换效果"
- **排查**（实证，`C:\Agent Space\待删除\blurstep-test` 渲染测试）：半径 8.0→0 以 0.1 步进
  逐档渲染算像素差——**档内（如 7.9→7.0）逐像素完全相同，跨整数边界才突变**；
  Performance/Quality 两档 RenderingBias 行为一致。结论：**WPF BlurEffect.Radius 被量化到
  整数档（floor）**，半径动画 8→0 实际只有 8 次离散跳变，任何缓动曲线都救不了
- **修复**：半径固定、动画改做 Opacity——每个对焦目标盖一层"模糊镜像覆盖层"
  （Rectangle + VisualBrush 实时镜像 + 固定 Radius 8/6/7），入场时覆盖层 Opacity 1→0 淡出
  = 连续对焦；完成后 Collapse（双份渲染只存在于入场期间）。
  三个覆盖层：MaskTitleFocusVeil / MaskIconFocusVeil（遮罩内，尺寸/变换绑定本体）+
  ContentFocusVeil（主 Grid Row 1，与 ContentRoot 同单元格像素级对齐）
- **回归验证**：覆盖层 Opacity 逐 0.05 渲染 20 档——Δ 全部 ≈0.023 均匀无平台期（平滑）；
  对齐敏感性：2px 故意偏移 Δ 仅 0.077（8px 模糊下镜像错位不可见），绑定对齐方案稳
- 顺带的隐性收益：原方案完成时摘除 Effect 会触发文字 ClearType↔灰阶抗锯齿切换（另一种"咔"），
  新方案本体全程不挂 Effect，此跳变一并消除
- C# 侧删除三个半径动画与 EntranceTitleBlurRadius/EntranceIconBlurRadius/ContentBlurRadius 常量，
  ContentBlurMs 更名 ContentFocusMs；编译 0 错误

## 8. 交叉淡化修复（覆盖层 v1 审查：文字/图标无模糊 + 内容模糊不同步）

- **现象**（Four 审查反馈）：①文字和图标完全看不到模糊；②黑屏淡出时内容模糊未与入场动画同步
- **根因（叠层合成原理性缺陷）**：v1 清晰本体在下、模糊镜像盖在上面——8px 模糊把 4-5px 笔画
  alpha 冲淡到 ~50%，**清晰层从模糊层下透出** → 合成 = "清晰+光晕"而非虚焦；内容区更甚：
  VisualBrush 镜像继承本源透明度，内容淡入到 0.2 时镜像也只剩 0.2 亮度，模糊压不住场
- **修复（真·交叉淡化）**：清晰层与模糊层透明度独立控制，**模糊镜像源恒不透明**：
  - 品牌元素三层结构：Fader（整体淡入，沿用原 EaseIn 渐亮节奏）→ Sharp 清晰层（同曲线淡入，
    双重衰减 → 清晰边缘晚到）+ Veil 模糊镜像（淡出，镜像恒不透明本体）
  - 内容区：新增 ContentSharpLayer 容器统一淡入（左右栏不再各自淡入），ContentRoot 恒不透明
    做镜像源，ContentFocusVeil 与入场动画同一 BeginTime 开播（严格同步）
  - 结构红利：变换移到组容器，v1 的 ActualWidth/变换绑定全删
  - SetReturnStartState/PlayContentEntrance 同步改走 ContentSharpLayer（切回动画行为不变）
- **回归验证**（blurstep-test 梯度能量法）：t=650ms 相对模糊度 0.93（≈纯模糊）→
  1040ms 0.75 → 1300ms 0.13 → 1500ms 0.00（纯清晰），单调无跳档——真对焦成立；编译 0 错误

## 9. 内容区对焦回退初版方案（覆盖层残影，Four 实测反馈）

- **现象**：黑屏淡出时内容区的模糊镜像覆盖层产生**残影**——全亮度模糊镜像跟随滑动中的内容
  拖尾、与清晰层错位叠加成鬼影（品牌文字/图标是原地对焦，不受影响）
- **决策（Four）**：内容区回退**初版方案**——直接对内容挂 BlurEffect 动画半径（6→0/650ms/
  QuadraticEase EaseOut），接受整数量化的有限跳档（初版实测观感可接受）
- **改动**：XAML 删 ContentFocusVeil，BlurEffect 挂到 ContentSharpLayer（x:Name=ContentBlur，
  完成后置 null 摘除）；C# 恢复 ContentBlurRadius=6/ContentBlurMs=650 常量
- **保留**：品牌区（文字/图标）交叉淡化覆盖层不动——原地对焦无残影且已过梯度能量验证；
  ContentSharpLayer 容器保留（统一淡入 + 模糊载体，切回动画行为不变）；编译 0 错误

## 10. v1.8.2 发布

- 版本号 v1.8.1 → **v1.8.2**（`setup/ToolboxSetup.iss` + 底部状态栏）
- 内容 = 第 6–9 节启动动画质感优化全套；产物 `setup/Toolbox_Setup.exe`

## 11. 弹窗开关动画全覆盖（v1.8.3）

- **PopupAnimator 移植**：dsh-app 菜单动画助手原样移植到 `Toolbox.Core/Controls/PopupAnimator.cs`
  （打开 = 垂直抛出 24px + 惯性回弹 2px（单条 BackEase）+ 缩放 0.5→1（70% 落位）+ 模糊 20→0
  420ms 线性渐清 + 240ms 淡入；关闭 = 打开严格时间倒放；尊重系统"菜单动画"设置，Tier<2 跳过逐帧模糊）。
  转写时抓到自己抄丢一帧：BuildScaleReverse 少了 0.30 处保持 1.0 的关键帧（会导致关闭动画开头立刻缩小），已对照原版修正
- **ConfirmDialog / DownloadDialog 挂载**：卡片四周 40px 透明动画安全区（分层窗口裁切越界位移）；
  打开动画构造函数挂 `Loaded` 播；关闭重写 `OnClosing` 首次拦截播倒放、完成后回调真正关窗；
  三处 OnClosing 均带 `Dispatcher.HasShutdownStarted` 放行守卫（否则托盘"退出"会被关窗动画中止 Shutdown）
- **ThemedMenuWindow 挂载（托盘/悬浮窗/TextBox 右键 3 处共用）**：三层重构（动画承载/静态阴影/视觉卡片，
  Effect 同元素唯一铁律），原 16px 投影边距并入 40px 安全区常量；抛出方向随展开方向自适应（贴底上翻时从下方抛出）
- **JunkCleanerTool 私有确认弹窗删除合并**：共享 ConfirmDialog 新增可选 `warningText` 警示行参数
  （承载"⚠️ 回收站清空后不可恢复！"），-110 行重复代码；按钮文字"确定清理"与警告行为保持不变
- **菜单首帧时序三连坑（实测逐轮排查）**：
  ① `BeginInvoke(DispatcherPriority.Loaded)` 在首帧之后才执行——优先级数值大者先，Render(7) > Loaded(6) → 闪帧；
  ② AllowsTransparency 分层窗口 `Show()` **内部同步合成呈现首帧**——Show 返回后再设起始态 = 最终态已上屏一帧
  （"瞬间出现→消失→再播动画"）；且 `Window.Show()` 同步触发 Loaded，事后挂事件永远等不到（动画整体丢失）；
  ③ **唯一起效位置 = Show 之前挂 `Loaded` 事件**（Loaded 在同步呈现之前、布局完成之后触发）——
  起始态 + 定位夹紧在其中同步完成，首帧即动画起点
- **同类隐患全项目审计**：其余 9 处 Loaded/Render 优先级延后调用逐处确认无害（交换链重建/走马灯/重定位
  等均为故意延后或操作已可见稳定界面）；闪帧模式只存在于 PopupAnimator 3 个调用点，已全部落在正确时序槽
- **文档同步**：ARCHITECTURE 03-core（ThemedMenuWindow/PopupAnimator 行）/04-plugins（对话框节）/
  06-flows（菜单行）/07-ui-system（动效表 2 行 + 新节「弹窗开关动画」）/09-tool-dev（公共类表）+ README 动效行
