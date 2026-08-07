# 工具 · 定时关机（ShutdownTool）

- **分类**：⚙️ 系统维护（System）
- **文件**：`Toolbox.Plugins/ShutdownTool.cs`（251 行）

## 功能

- 卡片式布局 + 快捷按钮重排序
- 6 个快捷按钮 + 自定义分钟 + 取消关机
- 通过 `shutdown.exe /s /t {秒}` 实现定时关机，`shutdown.exe /a` 取消

## 实现要点

- 卡片式布局，主题色统一
- 快捷按钮覆盖常用时长，自定义分钟输入走 `int.TryParse` 校验

## 已知问题（P1-6，未解决）

- 自定义分钟数无上限：`int.TryParse` 任意正整数 + `minutes * 60` 未 checked；
  输入 ≥ 35,791,395 分钟 → seconds 为负 → `shutdown /s /t 负数` 行为未定义（可能立即关机）
- 见 `docs/待解决-2026-07-31.md` P1-6

## 依赖（公共共享类）

| 类 | 用途 |
|----|------|
| ThemeColors / GlowCardMarker / ConfirmDialog | UI 一致性 |

## 相关文档

- 插件层总览 → [../04-plugins.md](../04-plugins.md)
