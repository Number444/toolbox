# 07 · UI 系统（横切）

> 横切 UI 系统：鼠标光晕 + 控件边缘发光 + 主题资源 + 自定义样式。

## 双层鼠标光晕系统

### 层 1 — HaloLayer（鼠标跟随呼吸光晕）

**位置**：`MainWindow.xaml` → `<Canvas x:Name="HaloLayer">` + `MainWindow.xaml.cs` → `InitHalo()`

| 组件 | 细节 |
|------|------|
| 渲染元素 | `Ellipse` 140×140px，`RadialGradientBrush` 10 个色标等差衰减（中心 `#40FFFFFF` → 边缘 `#00FFFFFF`） |
| 呼吸动画 | XAML `EventTrigger.Loaded` → `Storyboard` 循环驱动 `ScaleTransform` 0.9↔1.1，`SineEase` 1.5s |
| 位置跟随 | `GetCursorPos`（Win32）逐帧读取 → `PointFromScreen` 转换 → `lerp(0.12)` 插值滞后 |
| 淡入淡出 | `_haloOpacity` 系数 0.08 平滑过渡，移出窗口缓出 |
| 数据源选择 | 用 `GetCursorPos` 而非 `Mouse.GetPosition`——后者在 `HTCAPTION` 非客户区不更新 |

### 层 2 — EdgeGlowLayer（控件边缘发光叠加层）

**位置**：`Toolbox.Core/Helpers/EdgeGlowLayer.cs`，`FrameworkElement` 子类。

**基本参数**：
- `GlowRadius = 120px`（发光影响范围）
- `MaxAlpha = 1.0`（hover 峰值不透明度，完全过曝）
- `StrokeThickness = 2px`（硬切高光线）
- `MaxLitRadius = 100px`（照亮半径上限，亮弧大小贴合鼠标光晕）
- 强度公式：`t = 1 - d/120` → `alpha = t² × 1.0`；沿边框亮度 = `alpha × (1-offset)^0.6 × 1.3`

**核心机制**：

1. **控件识别 → 模板边界提取**：`ButtonBase` / `ComboBox` / `TextBox`（无需标记）可发光；卡片容器（`Border`）通过 `GlowCardMarker.IsGlowCard="True"` 显式 opt-in——全部工具卡片已按此模式标记，新增工具按 09-tool-dev 指引标记即可（本页不记录具体标记处数与清单，避免随工具增删漂移）。递归视觉树查找模板内首个 `Border` 的 `CornerRadius`，若四角半径不同用 `StreamGeometry` 逐角构造异径圆角矩形描边。

2. **径向渐变描边**：`RadialGradientBrush`（`MappingMode=Absolute`，中心=光标位置）。10 段色标，`alpha × (1-offset)^0.6 × 1.3`，近光心一端形成过曝平台，背光侧完全熄灭。

3. **遮挡检测**：5 点采样（中心 + 四角 20% 内缩）命中测试，仅在所有采样点都被非控件元素覆盖时判定遮挡；`HitTestAt` 用 `HitTestFilterCallback` 跳过 `IsHitTestVisible=false` 元素。

4. **长卡片滚出视口修复**：`PushClip` 裁剪到滚动视口相交区域，`PushOpacityMask` 叠加 32px 渐隐遮罩。

5. **目标清单管理**：只存元素引用不存坐标——每帧实时重算。`LayoutUpdated` → `_glowTargetsDirty` → 250ms 节流重建。工具切换/设置层显隐 → `ClearTargets()` 0ms 清除。

6. **重绘触发与动画同步**：重绘仅三源——光标移动（`UpdateCursor` 去重）、滚动（`ScrollChanged` → `Refresh()`）、清单重建。RenderTransform 动画（设置层进出/工具切换/切回前台）不触发布局与滚动，光标静止时光圈会定格在动画起点：`HaloFrame` 内用 `DependencyPropertyHelper.GetValueSource(...).IsAnimated` 检测受跟踪动画（`IsAnyGlowTrackedAnimationActive`），活跃期逐帧 `Refresh()`。**闸门不可省**——无条件 `Refresh()` 的 `InvalidateVisual` 会自激产帧，把渲染循环永久钉在 60fps（旧实现已踩过并回退）。**当前清单（9 项）**：`SettingsLayer.Opacity` / `SettingsLayerTransform.Y` / `SettingsLayerScale.ScaleX` / `NavPane.Opacity` / `NavPaneTransform.X` / `ContentScrollViewer.Opacity` / `ContentPaneTransform.Y` / `ContentTransitionControl.Opacity` / `TitleTransitionControl.Opacity`。**新增 RenderTransform 动画须把元素/属性补进该清单**。注（2026-08-11 决策）：动画完成后 HoldEnd 保持期 `IsAnimated` 仍为 true，闸门会持续逐帧 `Refresh()` 至下次动画——60fps 常驻为接受的预期（现代电脑不算性能浪费），**不做**动画完成清时钟（曾尝试，引入组件消失/状态机卡死两个回归，已回退）。

**配套样式变更**（App.xaml）：Button/ToggleButton 模板 Border → `CornerRadius="6"`；ComboBoxItem 模板 → `CornerRadius="4"` + `Margin="2,1"`。

**设置开关**：`AppSettings.MouseHaloEnabled`（鼠标光晕）、`ControlGlowEnabled`（控件边缘发光），均默认 true。

> ⚠️ **回归警示**：`EdgeGlowLayer` 是全项目回归率最高的模块（遮挡透出/Hover 消失曾多次互修互发）。任何改动它或其调用方前，必读并按 `docs/EDGE_GLOW_REGRESSION_CHECKLIST.md` 逐项实测。

### 主窗口光晕初始化流程

```csharp
MainWindow 构造函数末尾:
  InitHalo()

InitHalo():
  1. GlowLayer.LayoutUpdated → _glowTargetsDirty = true
  2. MainViewModel.PropertyChanged(SelectedTool) → RequestGlowRebuild()
  3. SettingsLayer.IsVisibleChanged → RequestGlowRebuild()
  4. CompositionTarget.Rendering 逐帧轮询:
     a. GetCursorPos() 读取光标
     b. HaloLayer 位置插值 + 淡入淡出
     c. GlowLayer 目标重建（250ms 节流）
     d. GlowLayer.UpdateCursor(pt, inside)
```

## 全局主题资源（App.xaml 定义）

| 资源 Key | 值 | 用途 |
|----------|---|------|
| `BgDarkBrush` | `#1C1C1C` | 右侧内容区背景 |
| `BgSurfaceBrush` | `#2D2D2D` | 左侧导航栏背景 / 卡片背景 |
| `BgCardBrush` | `#323232` | 卡片/输入框背景 |
| `BgHoverBrush` | `#3A3A3A` | 悬停高亮 |
| `AccentBrush` | `#76B580` | 主色调（按钮默认背景、ToggleSwitch 选中色） |
| `AccentHoverBrush` | `#92CD9B` | 按钮悬停提亮参考值（WPF 端已改白叠层法不再直接引用；HTML 控制面板色板对齐用） |
| `TextPrimaryBrush` | `#F0F0F0` | 主文字 |
| `TextSecondaryBrush` | `#999999` | 次要/描述文字 |
| `BorderSubtleBrush` | `#3F3F3F` | 分隔线/边框 |
| `SuccessBrush` | `#63D47E` | 成功提示 |
| `DangerBrush` | `#F07070` | 危险/取消按钮 |
| `GlobalFont` | Segoe UI, Microsoft YaHei | 全局字体 |

## 自定义样式

| 样式 Key | 说明 |
|----------|------|
| （隐式 `Button`） | Accent 绿主按钮：hover/press 为白 10%/黑 12% 叠层 90~120ms 淡变（保留各按钮自身色调，红按钮不再 hover 变绿）+ 0.97 按压缩放 |
| `StandardButtonStyle` | 次级按钮（BgSurface 灰底 + 1px BorderSubtle 描边）：导航/取消等非主操作用，hover 白叠层减半档 6%，其余同上 |
| `MediaTransportButtonStyle` | 媒体播放控制按钮（34×34 透明圆形，任务栏弹出媒体卡片用）：严格复刻全局叠层法参数（hover 白 0.10 90ms 进/120ms 出、press 黑 0.12 + 缩放 0.97、禁用 Opacity 0.5），图标走 Segoe Fluent Icons 字形 |
| `WindowButtonStyle` | 46x38 透明→#3A3A3A 标题栏按钮，内 Border CornerRadius=0,0,6,6（异径圆角） |
| `CloseButtonStyle` | 继承+悬停#E81123 关闭按钮 |
| `ToggleSwitchStyle` | Win11 极简滑块开关（42x22 轨道 + 18x18 滑块 + 0.2s 滑动动画，轨道颜色同步渐变 #45475A↔#76B580） |
| `CapsuleToggleStyle` | 纯开关胶囊（60x26，悬浮窗工具用，动画同 ToggleSwitchStyle） |
| `ClassicCheckBoxStyle` | 方框+对勾传统复选框（备用；勾选时方框底色/描边 0.2s 渐变，对勾瞬时出现） |
| `CustomScrollBar` | 深色滚动条（容器 16px，Thumb 居中：常态 6px 两侧各留 5px、展开 10px 各留 3px，不碰窗口边缘。四档：常态 #33FFFFFF → bar 悬停 #55 → thumb 悬停 #CC → 拖拽 Accent 绿 #CC76B580。导航区实例 `Margin="0,0,-3,0"` 右缩 3px，与内容区滚动条贴窗口右缘的圆角让位感对齐） |
| （隐式 `ComboBox`） | 弹层自绘入场：弃系统 Slide；透明度 150ms 快到位 + 缩放 0.96→1 / 位移 -6→0 走 240ms QuinticEase（原点 0.5,0 从本体绽放；动画须显式 From 否则 HoldEnd 锁终值导致重播失效）。不加 DropShadow——透明 Popup 中四角会堆积暗色尖角 |
| （隐式 `ToolTip`） | 深色样式（BgCard 底 + BorderSubtle 描边 + 6px 圆角，替换默认浅色系统样式）；Loaded 时 150ms 淡入。应用内右键菜单均走 `ThemedMenuWindow`（本样式不影响） |
| `TransitioningContentControl` | 内容切换两段式：旧内容 200ms 淡出（EaseIn，无位移）→ 新内容 400ms 淡入 + 滑入（EaseOut）；退场期间回写旧内容真正停留，`_pendingContent` 以最新内容为准；`SlideFromY` 控制滑入方向（标题区 -8 与内容区 8 对向）；暴露 `IsExiting`/`ExitCompleted` 供设置层串行对齐。⚠️ 本控件**无**动画完成清时钟（曾尝试，2026-08-11 快速连点回归后随机制整体回退）；知识留档：Storyboard.Completed 是根时钟事件，进场子动画被退场替换后根时钟仍会触发 Completed，若在此回调清时钟会误杀退场动画导致状态机卡死 |

## 动效参数总表（统一语言）

各动画时长/缓动集中一览（新改动先看这里，避免各处漂移）：

| 动画 | 时长 | 缓动 | 说明 |
|------|------|------|------|
| 内容切换·退场 | 200ms | CubicEase EaseIn | 旧内容淡出（无位移，避免与进场上滑方向打架） |
| 内容切换·进场 | 400ms | CubicEase EaseOut | 新内容淡入 + 上滑 8→0（标题区 -8 对向下滑） |
| 按钮 hover/press 叠层 | 90/120ms | 二次 EaseOut/EaseIn | 白 10%/黑 12% 叠层；按压缩放 0.97（80/120ms） |
| 开关轨道颜色 | 200ms | CubicEase EaseOut | 轨道 #45475A↔#76B580 与滑块位移同步 |
| 滚动条 Thumb 展开 | 150ms | QuadraticEase EaseOut | 宽度 6↔10px，四档颜色（#33/#55/#CC/拖拽绿） |
| ComboBox 弹层入场 | 150ms 透明 + 240ms 变换 | 二次/五次 EaseOut | 缩放 0.96→1 + 位移 -6→0，原点 (0.5,0) |
| ToolTip 淡入 | 150ms | 线性 | Loaded 触发 |
| 侧栏按压反馈 | 90ms 下压 / 180ms 回弹 | CubicEase EaseOut | 导航项/分组头缩放 0.96↔1（PreviewMouseLeftButtonDown/Up + MouseLeave 复位；`PressScale`/`PressDownMs`/`PressUpMs` 调参） |
| 设置层进入 | 360ms | 二次 EaseOut | 淡入 + 8px 上滑 + 缩放 0.96→1（`SettingsEnterScale` 调参，1=关闭缩放） |
| 设置层退出 | 150ms | CubicEase EaseIn | 淡出 + 8px 下滑 + 缩放→0.98（`SettingsExitScale`）；设置页点工具时串行对齐（见下） |
| 切回前台·整体淡入 | 280ms | 二次 EaseOut | 统一走 ContentSharpLayer 容器（2026-08-19 起左右栏不再各自淡入）；先于位移完成（≈55% 时长） |
| 切回前台·左侧位移 | 500ms | KeySpline(0.16,1,0.3,1) | 工具栏从左滑入（X -220→0） |
| 切回前台·右侧位移 | 延迟 60ms + 540ms | KeySpline(0.16,1,0.3,1) | 工具页大幅上滑 100→0（对等左侧滑入感）；60ms 微错峰润色 |
| 搜索框 focus 绿线 | 120ms 入 / 150ms 出 | 线性 | 底部 Accent 绿线 |
| 导航高亮移动 | 200ms | CubicEase EaseOut | HighlightAnimMs |
| 分组展开/折叠 | 200ms | CubicEase | 渲染式 Clip 揭示 + 兄弟平移；展开时子项 25ms 间隔错落淡入（`GroupItemStaggerMs`=0 关闭，`GroupItemFadeMs` 调单条时长） |
| 启动遮罩·文字 | 淡入 1300ms EaseIn + 上滑 40px 1300ms + 对焦 1500ms | 位移 KeySpline emphasized；淡入/对焦 CubicEase EaseIn | 从下往上滑入 + 渐渐亮起 + 虚焦→聚焦（Veil 覆盖层 8px 固定模糊，Opacity 淡出，落定后才对上焦） |
| 启动遮罩·图标 | 淡入 700ms EaseIn + 位移 48px 1000ms + 对焦 1200ms | 同上 | 从文字背后滑入文字左侧（终点动态计算，兜底 -66）；文字同步右移 22px——三者同刻起止（1.3s→2.3s）；Veil 6px 模糊淡出，详见「启动遮罩（两段式入场 + 对焦淡出编排）」 |
| 冷启动·内容入场 | 遮罩淡出 +120ms 起：位移 500/540ms + 淡入 280ms | KeySpline emphasized | 与切回前台同源（`PlayContentEntrance`）；黑幕揭开时内容滑入"浮出"，消除硬切 |
| 冷启动·内容对焦 | 650ms | 二次 EaseOut | ContentSharpLayer 直接 BlurEffect 6→0（初版方案：覆盖层在滑动内容上产生残影已回退，接受整数量化跳档；完成后摘除 Effect） |

## 设置层过渡（进出 + 串行对齐）

**进入**：`EnterSettingsView`——淡入 + 8px 上滑 + 缩放 0.96→1，360ms EaseOut（进慢出快的节奏，关闭保持 150ms；缩放与 ComboBox 下拉绽放同语言增加纵深感，`SettingsEnterScale`=1 关闭缩放）；`_settingsAnimToken` 递增令牌，动画完成回调只认最后一次，防快速连点状态错乱。

**退出（Back 返回）**：下层内容区立即可见 + 设置层 150ms 淡出下滑 + 缩至 0.98（`SettingsExitScale`），完成后折叠并复位（Opacity=1 / Y=0 / Scale=1）。**复位顺序关键**：先 `BeginAnimation(null)` 清掉 HoldEnd 保持值再写本地值，否则缩放复位被动画优先级压住不生效。进入动画必须显式 From（防读取到上次退场动画保持值）。缩放挂在 `SettingsLayerScale`（RenderTransform 内 ScaleTransform，位于 TranslateTransform 前）。

**退出（设置页内点击工具）——串行对齐**：工具切换退场（200ms）期间下层内容区**保持折叠**——退场动画（标题区/旧工具淡出）在折叠容器内不可见（设置层 60% 半透明遮不住，实测会"标题栏闪一下消失"）；等 `TransitioningContentControl.ExitCompleted`（退场完成、新内容切入淡入起点）再显示下层 + 设置层 150ms 快速退场，露出正在淡入的新内容。

## 切回前台动画（后台 → 前台入场）

窗口从后台恢复（最小化还原 / 托盘恢复 / 静默驻留唤起）时播放，`PlayReturnAnimations()`——
实为 `PlayContentEntrance(TimeSpan.Zero)`：冷启动入场与本动画共用同一编排方法（baseTime 为整条时间轴偏移）。

**触发机制**：`_wasBackground` 标志 + `Activated` 事件。置位点仅三处——最小化（StateChanged）、关闭到托盘（OnClosing 隐藏成功）、静默驻留（托盘创建成功）。**不监听 Deactivated**：点悬浮窗/系统弹窗导致的普通失焦不触发；正常启动不播放（启动遮罩链负责入场——OnWindowLoaded 编排前会把 `_wasBackground` 置 false，防静默启动首次唤起时本动画与冷启动入场叠加互踩，2026-08-19）。

**节奏**：2026-08-10 精调为现代动效三原则——① 位移曲线 `KeySpline(0.16,1 → 0.3,1)`（= CSS `cubic-bezier(.16,1,.3,1)`，Apple/Fluent emphasized，起步干脆、收尾长而柔，替代 CubicEase EaseOut 的偏硬尾段）；② 不透明度先于位移完成（淡入 280ms ≈ 位移时长的 55%，运动继续缓落）；③ 右侧 60ms 微错峰润色。位移时长左侧 500ms / 右侧 540ms。参数集中在 `MainWindow` 类级调参区（ReturnLeftFrom/ReturnRightFrom/ReturnFadeMs/ReturnLeftMoveMs/ReturnRightMoveMs/ReturnRightDelayMs，RightDelayMs=0 回完全同步）——`PlayContentEntrance` 与 `SetReturnStartState` 共享同源，起点与动画 From 改一处全同步（防止双写漂移）。曾尝试 200ms 大错峰 + QuinticEase（左侧先落位、右侧延迟），实测不理想已回退（2026-08-10）。

**实现要点**：NavPane/ContentPane 的 RenderTransform 常态为 0，动画必须显式 From（无 From 则原地不动）；纯渲染层无布局抖动；淡入统一作用于 `ContentSharpLayer` 容器（2026-08-19 起，左右栏不再各自淡入——该容器同时是冷启动对焦模糊的载体）；位移仍分别作用于 NavPane（X）/ContentScrollViewer（Y，不含设置层兄弟元素）；与内部内容切换动画叠加但互不冲突（不同元素动画属性）。

**首帧闪烁消除（清场帧拦截）**：还原瞬间 DWM 直接把最小化前缓存的最后一张窗口表面合成上屏——该位图在最小化时已定型，之后 `SetReturnStartState()` 无论多同步都改不了已上屏的缓存帧（录屏逐帧验证："后台置位 + 还原显示前再设"双保险仍闪一帧，2026-08-10 修复）。解法：`MinimizePreClearHook` 拦截 `WM_SYSCOMMAND / SC_MINIMIZE`（系统级最小化入口，如任务栏右键/点击最小化），先 `handled=true` 拦下最小化 → 同步置起点状态 → `WaitForRenderedFramesAsync(2)` 等"清场帧"（界面透明的起点状态）真正渲染提交进 DWM 缓存 → 再程序化 `WindowState=Minimized`（WPF 走 ShowWindow，不经 SC_MINIMIZE，无递归；`_preClearMinimize` 兼作重入保护）。自建标题栏最小化按钮（`MinimizeButton_Click`）直接设 `WindowState` 不产生 SC_MINIMIZE，须显式改调 `PreClearThenMinimizeAsync`（2026-08-10 实测：按钮路径闪烁即此遗漏）。还原时 DWM 亮出的是空窗帧，WPF 从起点播动画，无缝衔接。代价：最小化延迟约 2 帧（~33ms，不可感知）。已知边界：Win+D / Win+M 不经 SC_MINIMIZE 无法拦截，该路径首帧闪烁保留（系统级机制限制），仍由 `StateChanged` 兜底播动画。托盘隐藏/静默驻留路径不受 DWM 缓存影响（Hide 销毁表面，Show 首帧由 WPF 全新渲染），原有"Show 前同步起点"逻辑保留。

## 启动遮罩（两段式入场 + 对焦淡出编排，v1.8.2）

纯黑遮罩（与 DWM 首帧空窗帧同色，无缝衔接；**停留时长盖住 WPF 启动初期无法绘制的黑屏期——功能需求，勿缩短**）。`OnWindowLoaded` 驱动（静默启动首次托盘唤起同样生效，Loaded 首次显示才触发）。

**时间线**（时序骨架 2026-08-11 定稿；质感 2026-08-19 v1.8.2）：
- **0–1.3s 阶段 1**：文字从下方 40px 滑入（KeySpline emphasized）+ 三层交叉淡化对焦——Fader 整体淡入（EaseIn 1300ms，沿用原渐亮节奏）/ Sharp 清晰层同曲线淡入（双重衰减 → 清晰边缘晚到）/ Veil 模糊镜像（固定 Radius 8，Opacity 淡出 1500ms EaseIn——落定后才对上焦）
- **1.3–2.3s 阶段 2**：图标从文字背后（起点 = 终点 + 48px）同三层结构（模糊 6px、对焦 1200ms）向左滑入文字左侧（终点动态计算，兜底 -66）；文字同步右移 22px（= (图标宽 32 + 间距 12)/2，保证组合居中）
- **2.3–2.8s 定格 → 2.8s 起（`MaskFadeStartMs`）遮罩淡出** 500ms EaseOut，完成后 Collapsed，Acrylic 透出
- **2.92s 起（淡出 +120ms，`ContentEntranceDelayMs`）内容入场**：遮罩仍纯黑时 `SetReturnStartState()` 藏好起点 → `PlayContentEntrance(entranceStart)` 左右栏滑入 + ContentSharpLayer 淡入 + `ContentBlur` 对焦 6→0（650ms 二次 EaseOut，完成后 Effect 置 null）——动作前段隔半透黑幕隐约可见（"浮出"感），消除"片头播完硬切静态界面"

**模糊对焦的技术约束（2026-08-19 逐档渲染实测，blurstep-test 验证）**：
- WPF `BlurEffect.Radius` **量化到整数档**（floor；档内逐像素不变、跨整数边界突变，Performance/Quality 两档一致）——半径动画必跳档，缓动曲线只改变跳变时机
- 品牌区（原地对焦）→ **VisualBrush 模糊镜像覆盖层 Opacity 淡出**绕开（Opacity 连续）。覆盖层三要点：① **镜像源恒不透明**（VisualBrush 继承本源透明度——v1 直接镜像参与淡入的本体，模糊被拖暗、清晰层从底下透出 = 只见光晕不见虚焦）；② **交叉淡化**（Sharp/Veil 各自独立 Opacity，Fader 管整体渐亮）；③ 变换放组容器（镜像随动免绑定）
- 内容区（滑动中内容）**不能用覆盖层**——全亮度模糊镜像跟随位移拖尾 = 残影（v2 教训，Four 实测），回退初版直接半径动画、接受有限跳档

**协同约束**（保留）：图标位移与文字右移共用同一时长常量 + 同一 BeginTime + 同一缓动，严格同时开始同时结束；图标动画前先写入起点本地值（消除 From ≠ 实际位置的跳变）。调参：类级 `MaskFadeStartMs`/`MaskFadeMs`/`ContentBlurRadius`/`ContentBlurMs`/`ContentEntranceDelayMs`；方法内 `MaskPhase1Ms`/`MaskTitleFocusMs`/`MaskIconMoveMs`/`MaskIconFadeMs`/`MaskIconFocusMs`/`MaskIconTravelX`/`MaskTitleShiftX`/`MaskTitleRiseFrom`。

**注意**：启动动画**不在**光晕闸门清单（IsAnyGlowTrackedAnimationActive）内，无需清时钟——HoldEnd 保持终值即可（遮罩期 GlowLayer 被黑罩遮挡，无定格问题）。

## 搜索框（MainWindow 左侧顶部）

- 矢量放大镜 `Path`（替换 emoji 🔍，与标题栏齿轮同风格）
- focus 底部 Accent 绿线：`DataTrigger` 绑定 `SearchInput.IsFocused`，120ms 淡入 / 150ms 淡出（Win11 文本框语言）
- 无结果空态："无匹配工具" 提示（`NavGroupsControl.HasItems=False` 时显示）

## 相关文档

- 主程序层 → [02-main-app.md](02-main-app.md)
- 回归检查清单 → docs/EDGE_GLOW_REGRESSION_CHECKLIST.md
