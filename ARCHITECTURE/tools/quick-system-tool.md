# 工具 · 快捷系统操作（QuickSystemTool）

- **分类**：⚙️ 系统维护（System）
- **文件**：`Toolbox.Plugins/QuickSystemTool.cs`
- **状态**：★ 新增（2026-07，替代原 RestartExplorerTool）

## 功能

- 锁屏
- 关闭显示器
- 睡眠
- 重启资源管理器

## 实现要点

- 锁屏/关显示器/睡眠复用公共 `SystemPowerHelper`（Lock / TurnOffMonitor / Sleep）
- 重启资源管理器：taskkill + explorer 流程（工具内自实现）
- 卡片式布局 + 主题色统一，卡片标记 GlowCardMarker

## 依赖（公共共享类）

| 类 | 用途 |
|----|------|
| SystemPowerHelper | 锁屏 / 关显示器 / 睡眠 |
| ThemeColors / GlowCardMarker | UI 一致性 |

## 相关文档

- 插件层总览 → [../04-plugins.md](../04-plugins.md)
- SystemPowerHelper → [../04-plugins.md](../04-plugins.md)
