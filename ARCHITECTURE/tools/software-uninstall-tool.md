# 工具 · 软件卸载管理器（SoftwareUninstallTool）

- **分类**：📁 文件管理（File）
- **文件**：`Toolbox.Plugins/SoftwareUninstallTool.cs`
- **配套服务**：`Toolbox.Plugins/Services/SoftwareUninstallService.cs`

## 功能

- 注册表扫描已安装软件
- 图标提取
- 双击卸载
- 轮询检测卸载完成

## 实现要点

- 数据模型 `InstalledSoftware` + 排序模式 `SortMode`
- 注册表扫描 + 图标提取 + UAC 提权卸载（SoftwareUninstallService）
- 双击卸载 + 轮询检测（卸载完成后刷新列表）

## 已知问题

> 状态以 `docs/待解决-2026-07-31.md` 为准（唯一事实源），本页不复述具体条目以避免状态过时。P2-15（轮询异常兜底）已修复，P2-6/P2-7 见清单。

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
