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
| `WindowButtonStyle` | 46x38 透明→#3A3A3A 标题栏按钮，内 Border CornerRadius=0,0,6,6（异径圆角） |
| `CloseButtonStyle` | 继承+悬停#E81123 关闭按钮 |
| `ToggleSwitchStyle` | Win11 极简滑块开关（42x22 轨道 + 18x18 滑块 + 0.2s 滑动动画，轨道颜色同步渐变 #45475A↔#76B580） |
| `CapsuleToggleStyle` | 纯开关胶囊（60x26，悬浮窗工具用，动画同 ToggleSwitchStyle） |
| `ClassicCheckBoxStyle` | 方框+对勾传统复选框（备用；勾选时方框底色/描边 0.2s 渐变，对勾瞬时出现） |
| `CustomScrollBar` | 深色滚动条（容器 12px；Thumb 常态 6px 细条，hover/拖拽 150ms 展开 10px，颜色 #33→#55→#CCFFFFFF 三档；细条+光标光晕会压缩感知差，档距需 ≥40% alpha） |
| （隐式 `ComboBox`） | 弹层自绘入场：弃系统 Slide；透明度 150ms 快到位 + 缩放 0.96→1 / 位移 -6→0 走 240ms QuinticEase（原点 0.5,0 从本体绽放；动画须显式 From 否则 HoldEnd 锁终值导致重播失效）。不加 DropShadow——透明 Popup 中四角会堆积暗色尖角 |

## 相关文档

- 主程序层 → [02-main-app.md](02-main-app.md)
- 回归检查清单 → docs/EDGE_GLOW_REGRESSION_CHECKLIST.md
