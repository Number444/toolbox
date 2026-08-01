# Toolbox

一个基于 .NET 9 的 Windows 桌面工具箱，采用三层插件式架构。

## 特性

- **插件式架构**：核心抽象层（Toolbox.Core）+ 工具实现层（Toolbox.Plugins）+ WPF 界面层，新增工具无需改动主程序
- **毛玻璃界面**：Acrylic / Mica / Aero 三种背景模式，深浅色主题，自定义标题栏与滚动条
- **音乐悬浮窗**：贴边自动缩入的桌面悬浮窗，支持封面显示与多档背景效果（透明 / Acrylic / Aero）
- **纯 Win32 系统托盘**：不依赖 WinForms

## 内置工具

定时关机、屏保、重启资源管理器、强制删除文件、二维码生成、软件卸载管理、网络信息、密码生成器、OCR 文字识别（PaddleOCR 按需下载）、垃圾清理等。

## 项目结构

```
Toolbox/
├── Toolbox.sln             解决方案
├── Toolbox.Core/           核心抽象层：ITool 接口、模型、Win32 声明
├── Toolbox.Plugins/        工具实现层
├── Toolbox.Tests/          xUnit 测试
├── Toolbox/                主程序（WPF UI，MVVM）
└── setup/                  Inno Setup 安装脚本
```

详细架构说明见 [ARCHITECTURE.md](ARCHITECTURE.md)。

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
