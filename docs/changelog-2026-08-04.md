# Toolbox 更新日志（2026-08-04）

> 当日工作日志：08-02/08-03 健全性修复补记 + 架构文档树同步与修正 + 发布。

---

## 1. 健全性修复（2026-08-02/08-03，补记）

毛玻璃方向放弃后，恢复并修复的非毛玻璃健康项（此前未记录当日日志，本次补记）：

- **JunkCleanerTool 清理进度反馈与状态栏视觉优化**（`7746754`，rebase 后 `d1c2d12`）：扫描/清理进度回调 + 50ms 节流（Environment.TickCount64）、Task.Run 前预填充初始状态、预统计 EnumerateFiles try-catch、Dispatcher.BeginInvoke 线程安全上报、busy 时 Margin 6px / idle 时 -1px 视觉切换
- **失败可见性 10 项健全性修复**（`8e3f9ad`，rebase 后 `a50e709`，2026-08-02 全项目审查 P0+核心 P1）：
  - P0-1 单实例激活标题统一（`WindowTitle` 常量 = MainWindow.xaml Title）
  - P0-2 托盘失败窗口不可达 → `SystemTrayHelper.Show` 返回 bool，失败不隐藏窗口
  - P0-3 反射注册独立 try-catch（不拖垮全部工具发现）
  - P0-4 DisableAsync 静默成功 → 任务不存在跳过删除/删除失败返回 false
  - P0-5 引擎替换失败回滚（脚本精确分支 + throw 上抛）
  - P0-6 NaN 坐标序列化失败 → `JsonNumberHandling.AllowNamedFloatingPointLiterals`（读写复用同一 options）
  - P1-1 UAC 取消文案区分（`LastUserCancelled`，UI 显示"已取消授权"）
  - P1-2 QuickSystemTool 系统命令假成功 → WaitForExit 超时/非 0 退出码均降级提示（含 08-03 超时漏判修复）
  - P1-3 Dispatcher 弹窗节流（CrashThrottle 10s 冷却 + 连续 5 次退出）
  - P1-4 启动自检 0 工具提示 / P1-5 OCR 引擎加载失败提示 / P1-6 CreateContent 异常错误占位 / P1-7 Loaded 初始化拆分 + Shutdown 保护 / P1-8 更新后引擎未启动反馈

## 2. 架构文档树同步与修正（2026-08-04，rebase 后 `59bd575`）

- **同步远程**：`ARCHITECTURE/` 文档树（21 篇）+ `docs/REMOTE_CONTROL_TOOL_DESIGN.md`（远程控制工具设计）从云端拉取落地
- **全链路 review 结论**：结构骨架扎实（文档声称的类/文件 100% 真实存在），问题集中在状态标注与数据新鲜度
- **修正**（17 文件）：
  - 总原则入 README 约定：**不记录易漂移精确数值**（行数/计数/测试量）；状态类信息以 `docs/待解决-*.md` 为唯一事实源，叶子文档引用不复述
  - 源头修正：勾除待解决清单 P2-2/P2-15；ocr/software-uninstall/netease-music/network-info/junk-cleaner/password-generator 已知问题段全部改引用式
  - 事实纠错：qrcode 分类 Network→Text（叶子 + 索引两处）；password 依赖改直写 passwords.json；shutdown 删 ConfirmDialog 依赖
  - 补全：02-main-app 追加发布流程章节（自原 ARCHITECTURE.md 迁移）
  - 抗漂移：02/03/04/全部工具叶子删除行数列与（N 行）标注；05-tests 删 80/80 改引用式；07 GlowCard 去计数；README 树改引用式；09-tool-dev 补第 4 步
- **git 整合**：本地 3 提交 rebase 到远程文档提交之上（19 个 add/add 冲突按修正版解决），线性历史 push 到云端

## 3. 发布

- 版本号维持 1.0.0（`setup/ToolboxSetup.iss` 不变）
- 产物：`setup/Toolbox_Setup.exe`（self-contained 单文件）
