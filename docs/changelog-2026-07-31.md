# Toolbox 更新日志（2026-07-31）

> 当日工作日志：架构修复、代码级 P1 修复、OCR 全链路性能修复、v1.4 版本号与安装包。

---

## 1. 架构修复（4 条 P1，已 commit）

按 `docs/架构修复设计-2026-07-31.md`（已归档至待删除）分阶段执行，每一步均 build + 测试验证：

- **P1-9 命名空间隔离**：插件程序集统一为 `Toolbox.Plugins.*`（原 `Toolbox.Services`/`Toolbox.Helpers` 借用主程序命名空间，破坏依赖方向）
- **P1-10 接口化 + 下沉**：`DwmHelper` 下沉 `Toolbox.Core.Helpers`；`FloatSizeMode` 迁移 `Toolbox.Core.Models`；新增 `IMusicFloatController` 接口 + `MusicFloatControllerHost` 静态注册点，`MainWindow` 不再编译期绑定插件类型
- **P1-11 单策略加载**：删除三策略死代码，`ToolRegistry` 唯一策略 `Assembly.Load("Toolbox.Plugins")` + 反射注册悬浮窗控制器
- **P1-12 Win32 P/Invoke 收拢**：新建 `Toolbox.Core/Helpers/Win32Native.cs`，集中 DWM/WCA/SetWindowPos/GetCursorPos 等 9 处重复 P/Invoke

踩坑记录：`using 命名空间` 不导入静态类成员，裸调用需 `using static Toolbox.Core.Helpers.Win32Native;`（wpftmp 项目必现）。

## 2. 代码级 P1 修复（9 条，已 commit）

- **P1-1** 悬浮窗 Show/Hide 循环泄漏窗口实例（HWND 被 OS 强引用无法 GC）
- **P1-2** 引擎下载先删后下 → staging 临时目录 + 原子替换，失败不再丢失可用引擎
- **P1-3** 下载取消真实生效（CancellationToken 贯通 + 按钮触发）
- **P1-4** PaddleOCR Dispose 恢复 `SetDllDirectory(null)`（进程级污染修复）
- **P1-5** 软件卸载列表刷新版本号防覆盖 + 按 DisplayName 移除
- **P1-6** 定时关机分钟数上限 144000（防 int 溢出为负）
- **P1-7** JunkCleaner 重新扫描换新 CancellationToken + 退出 Cleaning 状态
- **P1-13** 卸载轮询异常兜底 + async void 通用 catch
- **P1-14** ResetPosition 先 ForceRestore 解除 Dock（消除"内容不可见 + 触发条悬空"死态）

验证：编译 0 错误 + 测试 101/101 全绿。

## 3. OCR 全链路性能修复（7 项，待 commit）

对"点击下载模型 → 模型就绪"全链路做 2 路对抗性复核（8 条问题 6 确认 2 部分成立）后修复，改动 `EngineDownloader.cs` + `OcrTool.cs`：

- **OCR-1**：PaddleOCRSharp 包从下载 3 次合并为 1 次（72.2MB×3 → ×1），总流量 **317MB → 173MB（-45%）**
- **OCR-2**：下载/解压/替换/引擎初始化全部移出 UI 线程 → 消除 5-15 秒界面冻结，下载全程 UI 响应
- **OCR-3**：首次打开 OCR 页面引擎后台懒加载（原同步加载冻结数百毫秒至数秒）
- **OCR-4**：8KB → 64KB 缓冲区 + 进度按百分比变化节流（原每 8KB 一次跨线程上报，约 3 万次冗余）
- **OCR-5**：网络失败自动重试 3 次（退避）+ 响应体读取独立 20 分钟超时（修复挂死无限冻结）
- **OCR-6**：下载中按钮禁用 + `_downloading` 防重入（原可并发下载 + 弹窗叠放）
- **OCR-7**：解压进度 `/200` 公式修正为 `/100` + 进度全链单调（0→92→93→95→97→100）

**判定不做**：OCR-8（识别 PNG 临时文件往返）——反射确认 `DetectText` 仅 `IntPtr`/`String`/`Bitmap` 重载，无 byte[]/MemoryStream 内存直传；改造需新增 System.Drawing.Common 依赖，收益数十毫秒，不值。

验证过程中的关键实测：`HttpClient` + `ResponseHeadersRead` 模式下 `Timeout` 不覆盖响应体读取（推翻原"164KB/s 必超时"推断）；下载量经 nuget.org HEAD 实测核实（PaddleOCRSharp 72.2MB、Paddle.Runtime 98.6MB、Newtonsoft.Json 2.4MB）。

## 4. v1.4 版本号与安装包

- 主界面版本号 v1.3 → **v1.4**
- 重新编译安装包 `setup/Toolbox_Setup.exe`（55.9 MB，未执行完整发布流程，无新 commit；ISS `AppVersion` 维持 1.0.0 不随主界面同步）

## 5. 设置页新增"删除 OCR 引擎"（待 commit）

- **入口**：设置页新增"OCR 高精度引擎"卡片（与设置项卡片同款：深灰圆角 + GlowCardMarker），红色"删除 OCR 引擎"按钮（`DangerBrush`，与退出按钮一致）+ 状态文字（显示已下载/未下载、占用大小、引擎目录路径）
- **流程**：点击 → `ConfirmDialog` 确认（弹窗内展示引擎目录与占用大小）→ 经 `MainViewModel.Tools` 找到 `OcrTool` 实例调用新增的 `OcrTool.UnloadEngine()`（释放 PaddleOCR 原生资源 + 恢复 `SetDllDirectory`，否则已加载的 DLL 被进程锁定导致 `Directory.Delete` 必失败）→ 递归删除引擎目录 → 状态文字反馈结果
- **失败兜底**：文件仍被占用（如原生 DLL 未能释放）→ 提示"请重启 Toolbox 后再试"；引擎未下载 → 提示"未检测到已下载的引擎"
- **配套改动**：`MainViewModel` 新增 `Tools` 只读属性暴露工具实例列表；`OcrTool.UpdateEngineUi` 从局部函数提升为类级私有方法
- **实现踩坑**：① `ConfirmDialog` 不设置 `DialogResult`，`ShowDialog()` 返回值恒为 null，必须读 `Confirmed` 属性判断；② public 方法不能声明在局部函数区内，`UnloadEngine` 初版误放 CreateContent 内被编译器拒绝
- 验证：编译 0 错误 + 测试 101/101 全绿。**待真机验证**：引擎加载状态下删除是否一次成功（取决于 PaddleOCREngine.Dispose 是否释放原生 DLL 锁定；若不释放会提示重启后重试，属预期兜底）

## 6. 引擎下载源加速：华为云镜像优先 + 自动回退（待 commit）

- **背景实测**（2026-07-31 本机无代理测速，2MB Range）：nuget.org 官方直连 **86 KB/s**（重定向+连接 4.9s），华为云 NuGet 镜像 **4.5-5.1 MB/s**（快约 50 倍）；腾讯云无 NuGet 镜像（404），百度/阿里/清华/中科大均无公共 NuGet 镜像
- **改动**：`EngineDownloader` 新增多下载源列表（v3-flatcontainer 统一格式）：① 华为云 `repo.huaweicloud.com/artifactory/api/nuget/v3/nuget-remote`（优先）② 官方 `api.nuget.org/v3-flatcontainer`（回退）。每源内保留 3 次退避重试，源耗尽自动切换，全部失败抛最后异常
- **验证**：编译 0 错误 + 测试 101/101；华为云镜像实际下载 3 个包均验证可达（HTTP 206 + Range 支持）
- **注意**：未来升级引擎版本时若华为云镜像尚未同步新版本会 404 → 自动回退官方源，行为安全

## 7. ARCHITECTURE.md 全量同步（待 commit）

按今日全部更新同步架构文档（经 wc 实测行数 + grep 实测光晕标记）：

- **目录结构**：SettingsView 描述补 OCR 引擎卡片；EngineDownloader 补多下载源；docs/ 树重构（移出已归档的悬浮窗结构/ROBUSTNESS_PLAN，新增 changelog-2026-07-31 与待解决清单）
- **行数表**：11 个文件实测更新（OcrTool 611→687、EngineDownloader 275→288、SettingsView.xaml 99→137、SettingsView.xaml.cs 34→143、MainViewModel 179→182、JunkCleaner 1024→1028、SoftwareUninstallTool 608→660、MusicFloatWindowManager 514→539、ShutdownTool 241→251、SoftwareUninstallService 284→293）
- **新增段落**："2026-07-31 OCR 引擎链路性能修复"（3 次下载合并/移出 UI 线程/多源/防重入/设置页删除引擎）；设置流程补 IsVisibleChanged 状态刷新
- **修正**：GlowCardMarker 10→12 处（实测含 SettingsView ×2）；工具表 OcrTool 行数；发布体积 ~54MB→~56MB；工具计数统一 11 个
- 待解决清单 D1-D6 文档待同步项全部勾除

## 8. 文档治理

- 过时/已使用完文档归档至 `docs/待删除/`：架构修复设计、ROBUSTNESS_PLAN（遗留 P2-6/P2-7 合并为待解决清单 P2-18/P2-19）、music-float-window-structure 等
- `docs/待解决-2026-07-31.md`：36 条问题对抗性复核（30 确认 / 3 反驳 / 1 部分 / 2 降级），修复条目逐条勾除

---

## 文件变更统计

| 变更类型 | 文件数 | 说明 |
|:--------:|:-----:|------|
| 新增 | 4 | Win32Native.cs、IMusicFloatController.cs、MusicFloatControllerHost.cs、changelog-2026-07-31.md |
| 修改 | 12+ | MainWindow.xaml(.cs)、ToolRegistry、EngineDownloader、OcrTool、DownloadDialog、PaddleOcrWrapper、9 条 P1 修复文件等 |
| 删除 | 4 | 死代码策略、PaddleOcrWrapper 旧加载路径等 |

> 状态：本地领先 origin/master 8 个提交（架构 + P1 修复已 commit，OCR 修复与 v1.4 未 commit），push 待用户手动执行。
