# Toolbox

一个基于 .NET 9 的 Windows 桌面工具箱，三层插件式架构，自带统一动效体系的深色毛玻璃界面。

## 特性

- **插件式架构**：核心抽象层（Toolbox.Core）+ 工具实现层（Toolbox.Plugins）+ WPF 界面层，新增工具无需改动主程序
- **毛玻璃界面**：Acrylic 原生背景（Windows 11，Win10 自动降级），深色主题 + 自定义标题栏
- **统一动效体系**：开屏品牌动画（虚焦对焦 + 编排入场）、内容切换两段式过渡（退场/进场）、按钮叠层动画、开关轨道渐变、滚动条动态展开、弹层自绘入场——时长/缓动全局统一（见架构文档 07）
- **开机自启静默启动**：自启时不弹主界面，后台托盘驻留 + 悬浮窗照常；双击 exe / 托盘单击即可唤起
- **音乐悬浮窗**：贴边自动缩入的桌面悬浮窗，支持封面显示与多档背景效果（透明 / Acrylic）
- **任务栏音乐控件**：嵌入任务栏的迷你播放信息（封面 + 歌名/歌手跑马灯），点击弹出 Mica 媒体卡片，内置播放控制
- **局域网远程控制**：浏览器控制关机/锁屏/查状态，设备管理 + 操作审计
- **局域网文件传输**：手机与电脑双向传大文件（流式不落内存），与远程控制同端口同页面
- **纯 Win32 系统托盘**：不依赖 WinForms

## 快速上手

- **设置**：主界面齿轮按钮 → 设置页（开机自启 / 自启静默 / 悬浮窗 / 鼠标光晕 / 远程控制）
- **托盘**：最小化到托盘驻留，单击托盘图标恢复主窗口；托盘菜单可退出
- **静默启动**：开机自启默认静默（后台托盘 + 悬浮窗），可在设置页关闭"自启时不显示主窗口"
- **悬浮窗**：网易云音乐悬浮窗可贴边自动缩入，支持游戏模式点击穿透

## 内置工具

首页仪表盘、定时关机、屏保启动、快捷系统操作（锁屏/睡眠/重启资源管理器）、C 盘垃圾清理、二维码生成、网络信息、软件卸载管理、密码生成器、截图识字（OCR，PaddleOCR 按需下载）、网易云音乐悬浮窗（含任务栏嵌入式控件）、局域网远程控制、局域网文件传输。

## 项目结构

```
Toolbox/
├── Toolbox.sln             解决方案
├── Toolbox.Core/           核心抽象层：ITool 接口、模型、Win32 声明
├── Toolbox.Plugins/        工具实现层
├── Toolbox.Tests/          xUnit 测试
├── Toolbox/                主程序（WPF UI，MVVM）
├── ARCHITECTURE/           架构文档树（索引见 ARCHITECTURE/README.md）
├── docs/                   更新日志与开发规范
└── setup/                  Inno Setup 安装脚本
```

详细架构说明见 [ARCHITECTURE/README.md](ARCHITECTURE/README.md)。

## 构建

```bash
# 调试
dotnet build Toolbox.csproj

# 发布（self-contained 单文件，win-x64）
dotnet publish Toolbox.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "setup/publish"
```

要求：.NET 9 SDK，Windows 10 19041+。

## 更新日志

按日记录于 [docs/](docs/)（如 `changelog-2026-08-10.md`）。

## 许可

[MIT](LICENSE) © Number444
