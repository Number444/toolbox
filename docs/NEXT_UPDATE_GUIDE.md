# 下次更新指导方案(源自 c64b564 审查)

> 版本:v1.0(2026-07-29)——**已于 2026-07-29 全部执行完毕**(构建 0 警告,测试 87/87 通过)
> 来源:对提交 `c64b564`(系统总览仪表盘 + 快捷系统操作)的可维护性/鲁棒性/性能三维度审查。
> 总体结论:提交整体满足三高要求,无高严重度问题;以下 5 条为中低严重度的改进项,下次更新时执行。

---

## M1(中,可维护)公网 IP 获取逻辑双份,超时策略不一致

- **现状**:`NetworkInfoTool`(自带 `PublicIpSources` + `Ipv4Regex` + `Http`,HttpClient 无显式超时,默认 100s)与 `SystemInfoHelper.GetPublicIPv4Async()`(同款双源+正则,5s 超时)是两份独立实现。查源列表、正则、fallback 顺序改一处不会同步另一处。
- **方案**:`NetworkInfoTool.LoadPublicIpAsync` 改为调用 `SystemInfoHelper.GetPublicIPv4Async()`,删除其私有的源数组、正则与 HttpClient。保留 UI 三态(加载中/失败/成功)与复制按钮逻辑不变。
- **验证**:网络信息工具与首页网络卡的公网 IP 显示一致;断网/拔网线时两者均 5s 内降级为失败态。

## M2(低,可维护)警示红不统一 + 硬编码颜色散点

- **现状**:应用里存在两种"警示红"——首页关机键用 `ThemeColors.Danger`(#F07070),`ConfirmDialog` 确认键硬编码 #D04040;另 `HomeDashboardTool` 分割竖线硬编码 #454545,`ConfirmDialog` 内部多处硬编码(#2D2D2D/#C0C0C0/#3D3D3D 等)。
- **方案**:`ThemeColors` 增补 `BorderSubtle`(#454545)与 `DangerButton`(#D04040)两个常量;`ConfirmDialog` 与 `HomeDashboardTool` 分割竖线换用常量。红色二选一:若统一用 Danger,ConfirmDialog 确认键改 Danger;若保留深红按钮观感,关机键改 DangerButton。**先让用户体验两种红再定**。
- **验证**:全局 grep 插件层不再出现 `0x2D2D2D`/`0xD04040`/`0x454545` 字面值(ThemeColors.cs 本身除外)。

## R1(低,鲁棒)`_cachedInfo` 跨线程读写无同步声明

- **现状**:`MusicFloatWindowManager._cachedInfo` 由 SMTC 事件(后台线程)写入,c64b564 新增的 `PeekNowPlaying()` 从 UI 线程读取。引用赋值在 .NET 中天然原子,最坏后果是短暂读到"新曲名+旧艺术家"的混合快照(1s 后自愈),不会崩溃。
- **方案**:给 `_cachedInfo` 加 `volatile` 修饰并在 `PeekNowPlaying` 注释中固化"允许短暂混合快照"的设计说明;无需加锁。
- **验证**:切歌瞬间首页播放卡显示不出现异常文本(观察即可)。

## R2(低,鲁棒)`TurnOffMonitor` 恒返回 true

- **现状**:`SystemPowerHelper.TurnOffMonitor()` 对 `SendMessage` 的返回值不做校验,实际失败(如远程桌面会话)也会向 UI 报告"显示器已关闭"。
- **方案**:返回类型改 `void` 并在注释写明"系统不提供成败探测,调用即视为已下发";调用方(QuickSystemTool/首页)不再对该操作报成功文字,或改为"已下发关闭显示器指令"。
- **验证**:远程桌面场景点击不报错、提示语不撒谎。

## T1(低,测试)`SystemInfoHelper` 纯函数无单测

- **方案**:为 `FormatUptime`(天/小时/分钟三档)、`FormatGb` 补 4-5 个 xUnit 用例(纯函数,不依赖 UI/系统状态)。`GetLocalIPv4`/`GetMemoryUsagePercent` 依赖本机环境,不测。
- **验证**:`dotnet test` 保持全绿,用例数增加。

---

## 明确不改(审查中排除的误报)

- `ToolNavigation` 静态事件不退订:订阅方是主窗口,生命周期=应用生命周期,无泄漏。
- `FetchPublicIpOnce` 的 `async void`:内部异常已被 `GetPublicIPv4Async` 全捕获,且写已脱离可视树的 TextBlock 在 WPF 中安全。
- 1s DispatcherTimer:回调仅字符串格式化与属性读取,开销可忽略;页面不可见即停表。
- `HomeDashboardTool` 397 行单文件:符合项目"一个工具一个文件"的既有约定(JunkCleanerTool 1024 行在先)。
