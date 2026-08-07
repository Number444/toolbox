# 05 · 测试项目（Toolbox.Tests）

> xUnit 测试框架，覆盖核心服务、工具辅助、悬浮窗、软件卸载。

## 测试文件

| 文件 | 覆盖 |
|------|------|
| AppSettingsTests.cs | 全局设置读写 |
| QrCodeToolTests.cs | 二维码生成辅助 |
| SoftwareUninstallToolTests.cs | 软件卸载扫描逻辑 |
| NeteaseMusicToolTests.cs | 悬浮窗相关 |
| SystemInfoHelperTests.cs | 系统信息辅助 |

## 基线状态

- **80/80 全绿 baseline**（2026-07 修复既有失败后恢复）

## 测试策略说明

- Core 纯逻辑（JsonSettingsFile、密码生成器种子算法、SMTC 会话过滤）优先补单测，为后续重构提供安全网
- UI 层不直接单测（WPF 依赖），逻辑抽离到 Service/Helper 层再测

## 相关文档

- 开发规范 → [09-tool-dev.md](09-tool-dev.md)
