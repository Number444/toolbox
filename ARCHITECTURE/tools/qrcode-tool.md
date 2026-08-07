# 工具 · 二维码生成（QrCodeTool）

- **分类**：🌐 网络与开发（Network）
- **文件**：`Toolbox.Plugins/QrCodeTool.cs`（265 行）+ `Toolbox.Plugins/QrCodeHelper.cs`

## 功能

- 文本 / URL 实时生成二维码
- 深色圆角卡片式 + 竖排按钮
- 保存 + 复制

## 实现要点

- 使用 QRCoder 库（QrCodeHelper 封装）
- 输入实时刷新生成
- 深色主题卡片式布局，主题色统一

## 测试覆盖

- `QrCodeToolTests.cs`（二维码生成辅助）

## 依赖（公共共享类）

| 类 | 用途 |
|----|------|
| QrCodeHelper | QRCoder 库封装 |
| ThemeColors / GlowCardMarker | UI 一致性 |

## 相关文档

- 插件层总览 → [../04-plugins.md](../04-plugins.md)
- 测试 → [../05-tests.md](../05-tests.md)
