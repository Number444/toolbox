# 工具 · 密码生成器（PasswordGeneratorTool）

- **分类**：🔤 文本与数据（Text）
- **文件**：`Toolbox.Plugins/PasswordGeneratorTool.cs`（563 行）
- **状态**：★ 新增（2026-07）

## 功能

- 名字种子 + SHA256 确定性生成
- 长度 / 字符集选择
- 历史记录

## 实现要点

- 确定性生成：相同种子 + 参数 → 相同密码（可复现）
- 历史记录持久化（JsonSettingsFile）
- 卡片式布局 + 主题色统一，卡片标记 GlowCardMarker

## 依赖（公共共享类）

| 类 | 用途 |
|----|------|
| JsonSettingsFile | 历史记录持久化 |
| ThemeColors / GlowCardMarker | UI 一致性 |

## 相关文档

- 插件层总览 → [../04-plugins.md](../04-plugins.md)
