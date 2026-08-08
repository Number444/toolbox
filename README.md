# Toolbox

一个基于 .NET 9 的 Windows 桌面工具箱，采用三层插件式架构。

## 特性

- **插件式架构**：核心抽象层（Toolbox.Core）+ 工具实现层（Toolbox.Plugins）+ WPF 界面层，新增工具无需改动主程序
- **毛玻璃界面**：Acrylic 原生背景（Windows 11），深浅色主题，自定义标题栏与滚动条
- **音乐悬浮窗**：贴边自动缩入的桌面悬浮窗，支持封面显示与多档背景效果（透明 / Acrylic）
- **局域网远程控制**：浏览器控制关机/锁屏/查状态，设备管理 + 操作审计
- **纯 Win32 系统托盘**：不依赖 WinForms

## 内置工具

首页仪表盘、定时关机、屏保启动、快捷系统操作（锁屏/睡眠/重启资源管理器）、C 盘垃圾清理、二维码生成、网络信息、软件卸载管理、密码生成器、截图识字（OCR，PaddleOCR 按需下载）、网易云音乐悬浮窗、局域网远程控制。

## 项目结构

```
Toolbox/
├── Toolbox.sln             解决方案
├── Toolbox.Core/           核心抽象层：ITool 接口、模型、Win32 声明
├── Toolbox.Plugins/        工具实现层
├── Toolbox.Tests/          xUnit 测试
├── Toolbox/                主程序（WPF UI，MVVM）
├── ARCHITECTURE/           架构文档树（索引见 ARCHITECTURE/README.md）
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

## 许可

[MIT](LICENSE) © Number444
