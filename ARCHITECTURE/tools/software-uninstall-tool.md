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

- **P2-6**：软件去重比较器不一致——seenNames 用 OrdinalIgnoreCase 而 list.Find 大小写敏感，大小写不同的重复条目新版被丢弃
- **P2-7**：同一软件可被重复双击启动多个卸载进程（`_pendingUninstall` 只记录不拦截，轮询期间再双击 → 第二个 UAC + 卸载进程）
- **P2-15**：卸载轮询阶段注册表键被删/应用退出期无兜底（`SoftwareUninstallService` 读注册表无 try/catch）
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
