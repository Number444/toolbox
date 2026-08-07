# 工具 · 软件卸载管理器（SoftwareUninstallTool）

- **分类**：📁 文件管理（File）
- **文件**：`Toolbox.Plugins/SoftwareUninstallTool.cs`（660 行）
- **配套服务**：`Toolbox.Plugins/Services/SoftwareUninstallService.cs`（293 行）

## 功能

- 注册表扫描已安装软件
- 图标提取
- 双击卸载
- 轮询检测卸载完成

## 实现要点

- 数据模型 `InstalledSoftware` + 排序模式 `SortMode`
- 注册表扫描 + 图标提取 + UAC 提权卸载（SoftwareUninstallService）
- 双击卸载 + 轮询检测（卸载完成后刷新列表）

## 已知问题（未解决）

- **P1-5**：软件卸载列表刷新无互斥/版本号，慢加载覆盖快加载——快速点两次刷新 → 先启动的慢加载后完成覆盖新数据；卸载轮询期间刷新 → 列表换新实例，轮询结束 Remove 按引用移除失败 → 已卸载软件残留显示
- 见 `docs/待解决-2026-07-31.md`

## 测试覆盖

- `SoftwareUninstallToolTests.cs`

## 依赖（公共共享类）

| 类 | 用途 |
|----|------|
| SoftwareUninstallService | 扫描 + 卸载执行 |
| ThemeColors / GlowCardMarker | UI 一致性 |

## 相关文档

- 插件层总览 → [../04-plugins.md](../04-plugins.md)
- 问题清单 → docs/待解决-2026-07-31.md
