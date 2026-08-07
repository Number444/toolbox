# 工具 · 网络信息（NetworkInfoTool）

- **分类**：🌐 网络与开发（Network）
- **文件**：`Toolbox.Plugins/NetworkInfoTool.cs`（274 行）
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

## 已知问题（未解决）

- **P2-5**：网卡枚举无异常保护，热插拔竞态可从 CreateContent 冒泡——`GetIPProperties()`/`GetPhysicalAddress()` 无 try/catch，拔网线瞬间可能 NetworkInformationException 逃逸
- 见 `docs/待解决-2026-07-31.md`

## 相关文档


- 插件层总览 → [../04-plugins.md](../04-plugins.md)
