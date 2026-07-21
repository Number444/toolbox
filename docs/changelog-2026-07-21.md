# Toolbox 更新日志（自上次 Push 以来）

> 对比基准：origin/master（b3b7122）→ HEAD（a657bfa）
> 生成日期：2026-07-21

---

## 本次 push 的 4 个 commit

| commit | 说明 |
|--------|------|
| `02a803c` | docs: 双层光晕系统架构同步 + 更新日志 |
| `e82d97c` | refactor: ShutdownTool 卡片式布局 + 发光径向渐变 |
| `3b74e5e` | refactor: EdgeGlowLayer 迁移至 Core + 工具卡片化 + GlowCardMarker |
| `a657bfa` | docs: 架构行数/描述同步 |

---

## 新增与变更

### 1. EdgeGlowLayer 迁移至 Toolbox.Core（3b74e5e）

引擎从 `Toolbox/Helpers/` 迁至 `Toolbox.Core/Helpers/EdgeGlowLayer.cs`，作为核心基础设施供主窗口和插件共同引用。
- **当前使用**：仅主窗口 `MainWindow.xaml`（`<corehelpers:EdgeGlowLayer>`）
- **悬浮窗接入**：因理解错误已回退，暂未共用。Core 的定位是"插件今后可用"。
- **新增收录**：TextBox 无需标记即被收录；卡片容器通过 `GlowCardMarker.IsGlowCard` 附加属性显式 opt-in
- **已标记卡片**：ShutdownTool(2) / RestartExplorer(1) / Screensaver(1) / QrCode(2) / JunkCleaner 主列表 / 设置页卡片 / NeteaseMusicTool 设置卡片

### 2. 描边改径向渐变（e82d97c）

描边从纯色 `SolidColorBrush` 改为以光标为中心的 `RadialGradientBrush`（`MappingMode=Absolute`）：
- 10 色标：`alpha × (1-offset)^0.6 × 1.3`（截断 255），近光侧形成过曝平台、背光侧归零
- 叠加控件级距离衰减：`(1 - d/120)²`
- `MaxLitRadius = 100px`，大卡片照亮弧段不会超过此范围
- 新增参数：`GradientFalloffExponent = 0.6`、`GradientBoost = 1.3`

### 3. PushClip 裁剪修复假亮边（3b74e5e）

长卡片滚动时侧边透出修复（合并为一条）：
- **PushClip**：裁剪到滚动视口相交区域，不把视口边缘误当成控件边缘
- **PushOpacityMask**：被滚出一侧叠加 32px 渐隐遮罩，边缘没入视口前淡出
- **遮挡快速路径**：改用可见区域判定

### 4. 异径圆角描边（3b74e5e）

标题栏按钮 `CornerRadius` 从 `6` 改为 `0,0,6,6`（上方直角、下方圆角）。`EdgeGlowLayer` 用 `StreamGeometry` 逐角构造异径圆角矩形描边。

### 5. 四个工具卡片化（3b74e5e）

| 工具 | 变化 | 行数 |
|------|------|:--:|
| ShutdownTool | 卡片式布局（快捷关机卡片 + 自定义时长卡片）、主题色常量化 | 241 |
| QrCodeTool | 统一卡片容器 + 竖排按钮 | 264 |
| ScreensaverTool | 卡片容器 + 统一色常量 | 186 |
| RestartExplorerTool | 卡片容器 + 统一色常量 | 111 |

逻辑与文案未动，仅改布局与配色。

### 6. 设置页新增开关（b3b7122）

`AppSettings` 新增 `MouseHaloEnabled` / `ControlGlowEnabled`（默认 `true`），设置页新增两个 ToggleSwitch。

---

## UI 润色（同批）

| 变更 | 说明 |
|------|------|
| **标题栏按钮圆角** | Border CornrRadius `0,0,6,6`（上方直角、下方圆角） |
| **遮罩透明度归档** | 各区域统一到 20%~60% 档位：搜索框 `#66`、内容 `#66`、输入框 `#80`、导航 `#99`、设置 `#99` |
| **设置层不透明度** | `#4D323232`(30%) → `#99323232`(60%)，模态浮层压住下层 |
| **滚动条滑块加粗** | 容器 8→12px，滑块 6→10px，圆角 3→5px，`#21FFFFFF`→`#33FFFFFF` |
| **工具标题间距收紧** | 描述→分隔线 20→10px，分隔线→内容 20→12px |
| **悬浮窗遮罩** | OpacityOverlay `#731A1A1A`→`#801A1A1A`（微调） |
| **设置页卡片发光** | SettingsView 设置卡片加 `GlowCardMarker.IsGlowCard="True"` |

---

## 文件变更统计

| 变更类型 | 文件数 | 行数 |
|:--------:|:-----:|:----:|
| 新增 | 3（EdgeGlowLayer/Core、GlowCardMarker、changelog） | +523 |
| 修改 | 11 | +325 / -430 |
| 删除 | 1（Helpers/EdgeGlowLayer.cs） | -298 |
| **总计** | **16** | **+848 / -430** |
