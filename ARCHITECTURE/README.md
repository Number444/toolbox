# Toolbox 架构文档集合

> 本文档集合将原 `ARCHITECTURE.md` 按项目树结构拆分为多文档索引体系。
> **原 `ARCHITECTURE.md` 保留不动**（迁移过渡期双轨并行，后续再决定去留）。

## 文档树（对应项目结构）

```
Toolbox/
├── ARCHITECTURE/                     ← 本集合
│   ├── README.md                     总索引（本文件）
│   ├── 01-overview.md                项目概览 · 分层模型 · 依赖关系
│   ├── 02-main-app.md                主程序层（App/MainWindow/Helpers/Services/Views/ViewModels + 发布流程）
│   ├── 03-core.md                    Toolbox.Core 核心抽象层
│   ├── 04-plugins.md                 插件层总览 + 工具索引（完整工具清单见此处）
│   ├── 05-tests.md                   测试项目
│   ├── 06-flows.md                   ★ 横切：关键流程（启动/设置/切换/悬浮窗状态机）
│   ├── 07-ui-system.md               ★ 横切：光晕系统/主题资源/滚动条
│   ├── 08-settings.md                ★ 横切：AppSettings / AudioflowSettings 配置项
│   ├── 09-tool-dev.md                ★ 横切：新工具开发指南（引用现有规范）
│   └── tools/                        叶子节点：每个工具一个文档（清单见 04-plugins.md 索引）
│
├── ARCHITECTURE.md                   ← 原架构文档（保留不动，迁移过渡期）
├── Toolbox.Core/                     核心抽象层
├── Toolbox.Plugins/                  工具实现层
├── Toolbox.Tests/                    单元测试
├── Toolbox/                          主程序（WPF UI）
├── setup/                            Inno Setup 安装脚本
└── docs/                             项目文档（changelog / 开发规范 / 清单）
```

## 快速导航

| 想找什么 | 去哪 |
|---------|------|
| 项目整体结构、分层、依赖 | [01-overview.md](01-overview.md) |
| 主窗口/托盘/光晕/工具注册 | [02-main-app.md](02-main-app.md) |
| ITool 接口、Core 基础设施 | [03-core.md](03-core.md) |
| 有哪些工具、各工具详情 | [04-plugins.md](04-plugins.md) → [tools/](tools/) |
| 启动/设置/切换/悬浮窗流程 | [06-flows.md](06-flows.md) |
| 鼠标光晕、边缘发光、主题色 | [07-ui-system.md](07-ui-system.md) |
| 设置项与持久化 | [08-settings.md](08-settings.md) |
| 新工具怎么写 | [09-tool-dev.md](09-tool-dev.md) + docs/TOOL_DEVELOPMENT_GUIDELINE.md |

## 约定

- **不记录易漂移的精确数值**：行数、调用点计数、测试数量一律不写精确值；需要表达规模时用层级（百行级/千行级）或省略。事实状态（分类/依赖/路径/功能）必须准确
- **状态类信息以 `docs/待解决-*.md` 为唯一事实源**：叶子文档引用而非复述，避免状态过时二次污染
- 跨文档引用一律用**文件名 + 锚点**或工具名（不用行号引用）
- 叶子文档对应"工具/模块"粒度，`Helpers/Services/Controls` 等共享类归入所在层概览文档
- 新增工具时：在 `tools/` 新增一个叶子文档，并在 `04-plugins.md` 工具索引表登记（详见 09-tool-dev.md）；README 树形图不逐列工具，无需维护
