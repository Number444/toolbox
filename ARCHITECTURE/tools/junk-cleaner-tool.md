# 工具 · C盘垃圾清理（JunkCleanerTool）

- **分类**：⚙️ 系统维护（System）
- **文件**：`Toolbox.Plugins/JunkCleanerTool.cs`（1028 行，全项目最大工具文件）

## 功能

- 12 类分类扫描
- 回收站删除
- 受保护文件跳过
- 自定义确认弹窗 + 取消按钮

## 实现要点

- 12 类垃圾分类扫描 + 大小统计
- 清理前统一 `ConfirmDialog` 确认
- 主列表卡片标记 GlowCardMarker
- 扫描/清理异步执行，进度反馈

## 已知问题

> 状态以 `docs/待解决-2026-07-31.md` 为准（唯一事实源），本页不复述具体条目以避免状态过时（当前含 P2-1）。

## 依赖（公共共享类）

| 类 | 用途 |
|----|------|
| ConfirmDialog | 删除/清空确认 |
| ThemeColors / GlowCardMarker | UI 一致性 |

## 相关文档

- 插件层总览 → [../04-plugins.md](../04-plugins.md)
- 问题清单 → docs/待解决-2026-07-31.md
