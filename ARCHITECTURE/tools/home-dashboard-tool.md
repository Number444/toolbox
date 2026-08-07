# 工具 · 首页仪表盘（HomeDashboardTool）

- **分类**：📊 首页（Home）
- **文件**：`Toolbox.Plugins/HomeDashboardTool.cs`（397 行）
- **状态**：★ 新增（2026-07）

## 功能

- 启动默认页
- 时间 / 磁盘 / 内存 / 网络 / 播放状态卡片
- 主卡内嵌快捷操作
- 卡片点击跳转对应工具（经 `ToolNavigation` 中转）

## 实现要点

- 定时器随页面显隐启停（不常驻轮询）
- 复用 `SystemInfoHelper`（内存/磁盘/运行时长/IPv4）
- 卡片 Border 标记 `GlowCardMarker`（卡片发光）

## 依赖（公共共享类）

| 类 | 用途 |
|----|------|
| SystemInfoHelper | 系统状态数据 |
| ToolNavigation | 卡片点击跳转工具 |
| ThemeColors / GlowCardMarker | UI 一致性 |

## 相关文档

- 插件层总览 → [../04-plugins.md](../04-plugins.md)
- 导航中转 → [../03-core.md](../03-core.md)
