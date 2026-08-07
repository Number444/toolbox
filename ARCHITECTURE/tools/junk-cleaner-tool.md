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

## 已知问题（未解决）

- **P1-7**：取消后重新扫描复用同一可取消 token，状态与数据脱节——清理中取消 → 部分已删、尺寸停留旧值；再点"清理已选"基于过期 FileCount/SizeBytes 决策
- **P2-1**：回收站类别硬编码 `C:\$Recycle.Bin` + FileCount>0 门槛——系统盘非 C 或权限不足 → 扫描为 0 → 勾选回收站也无法触发清空；触发则清空全部驱动器而扫描只算 C 盘
- 见 `docs/待解决-2026-07-31.md`

## 依赖（公共共享类）

| 类 | 用途 |
|----|------|
| ConfirmDialog | 删除/清空确认 |
| ThemeColors / GlowCardMarker | UI 一致性 |

## 相关文档

- 插件层总览 → [../04-plugins.md](../04-plugins.md)
- 问题清单 → docs/待解决-2026-07-31.md
