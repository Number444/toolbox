# Toolbox 更新日志（自上次 Push 以来）

> 对比基准：004db07（架构审查修复）→ HEAD（b3b7122）
> 生成日期：2026-07-21

---

## 新增功能

### 1. C盘垃圾清理工具（bfec545 / 8172106 / c124006）
- 新增完整的 **JunkCleanerTool**，扫描 12 类系统垃圾
- 后续修复：目录遍历死循环、UI 优化、取消按钮、自定义确认弹窗、间距微调

### 2. 双层鼠标光晕系统（b3b7122）

#### HaloLayer — 鼠标跟随呼吸光晕
- `MainWindow.xaml` 新增 `<Canvas HaloLayer>`，内含 140×140px Ellipse
- 10 色标径向渐变（`#40FFFFFF`→`#00FFFFFF` 等差递减）
- `Storyboard` 循环驱动缩放呼吸动画（0.9↔1.1，SineEase 1.5s）
- `GetCursorPos`（Win32）逐帧读取 + `lerp(0.12)` 插值滞后跟随
- 使用 Win32 API 而非 WPF `Mouse.GetPosition`：解决 HTCAPTION 非客户区不更新问题

#### EdgeGlowLayer — 控件边缘发光叠加层
- 新文件 `Helpers/EdgeGlowLayer.cs`（298 行），`FrameworkElement` 子类
- 仅 `ButtonBase` / `ComboBox` 可发光，取模板首个 Border 的 CornerRadius 贴合
- 距离驱动强度：`alpha = t² × 0.9`，范围 120px
- 5 点采样遮挡检测：HitTestAt 增加 `IsHitTestVisible` 过滤，避免自身拦截命中
- 250ms 节流重建目标清单，工具切换/设置层显隐时 0ms 清除
- 配套样式：Button/ToggleButton CornerRadius=6，ComboBoxItem CornerRadius=4

### 3. 光晕设置开关（b3b7122）
- `AppSettings` 新增 `MouseHaloEnabled` / `ControlGlowEnabled`（默认 true）
- `SettingsView.xaml` 新增两个 ToggleSwitch，支持运行时开关

### 4. 版本号提升
- 状态栏从 v1.0 → **v1.1**（e97d9c0）

---

## 已修正问题

- **JunkCleanerTool 目录遍历死循环**（8172106）：修复递归扫描算法
- **JunkCleanerTool UI 优化**（ca82db1 / c124006）：紧凑间距、确认弹窗、取消按钮
- **QrCodeTool 样式重构**（87aee9b）：深色圆角主题 + 竖排按钮布局
- **SoftwareUninstallTool 视觉优化**（ca82db1）
- **遮挡检测误杀**（b3b7122）：HitTestAt 增加 `IsHitTestVisible` filter，EdgeGlowLayer 自身不再拦截命中

---

## 文件变更统计

| 文件 | 变更 | 行数变化 |
|------|------|:--------:|
| `Helpers/EdgeGlowLayer.cs` | **新增** | +298 |
| `MainWindow.xaml` | 新增光晕层 + 渐变色标 | +14 |
| `MainWindow.xaml.cs` | InitHalo + EdgeGlow 集成 | +44 |
| `App.xaml` | Button/ComboBoxItem CornerRadius | +5 |
| `AppSettings.cs` | MouseHaloEnabled + ControlGlowEnabled | +39 |
| `SettingsView.xaml` | 2 个 ToggleSwitch | +12 |
| `ARCHITECTURE.md` | 架构同步 + 光晕章节 | +大量 |
| **总计** | **7 个文件** | **+412 / -3** |

---

## 后续待办

- EdgeGlowLayer 遮挡检测：5 点采样 + `return false`（任意一点可见即放行）逻辑在部分遮挡场景下可能透光，需评估是否改为中心点检测
