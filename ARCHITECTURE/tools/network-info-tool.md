# 工具 · 网络信息（NetworkInfoTool）

- **分类**：🌐 网络与开发（Network）
- **文件**：`Toolbox.Plugins/NetworkInfoTool.cs`
- **状态**：★ 新增（2026-07）

## 功能

- IP / MAC / 网关 / DNS
- 公网 IP 异步获取
- 逐项复制

## 实现要点

- 局域网信息经 .NET `NetworkInterface` 公共 API 读取（工具内自实现）
- 公网 IP 异步请求外部服务，try-catch + 超时
- 卡片式布局 + 主题色统一，卡片标记 GlowCardMarker

## 依赖（公共共享类）

| 类 | 用途 |
|----|------|
| ThemeColors / GlowCardMarker | UI 一致性 |

## 已知问题

> 状态以 `docs/待解决-2026-07-31.md` 为准（唯一事实源），本页不复述具体条目以避免状态过时（当前含 P2-5）。

## 相关文档


- 插件层总览 → [../04-plugins.md](../04-plugins.md)
