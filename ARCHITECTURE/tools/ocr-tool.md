# 工具 · 截图识字（OcrTool）— OCR 引擎子系统

- **分类**：🔤 文本与数据（Text）
- **文件**：`Toolbox.Plugins/OcrTool.cs`（687 行）
- **状态**：★ 新增（2026-07），OCR 引擎子系统全链路

## 功能

- 截图 / 图片离线提取文字
- 导入方式：文件选择 / 拖入虚线框 / 粘贴
- 双引擎：
  - **Windows 内置 OCR**（离线识别，不上传网络）
  - **PaddleOCR 高精度引擎**（可选，首次使用自动下载模型）
- 设置页 OCR 引擎管理卡片：状态显示 / 确认删除

## 子系统文件

| 文件 | 行数 | 职责 |
|------|:----:|------|
| OcrTool.cs | 687 | 工具主体（后台加载/下载防重入/UnloadEngine） |
| DownloadDialog.cs | 210 | 引擎下载进度对话框（进度条 + 取消） |
| Helpers/OcrHelper.cs | 89 | Windows 内置 OCR 引擎封装 |
| Helpers/PaddleOcrWrapper.cs | 189 | PaddleOCR 高精度引擎包装（原生库加载/释放） |
| Helpers/EngineDownloader.cs | 288 | 引擎/模型下载、校验与解压（多下载源 + 重试 + 进度节流） |
| Helpers/ImageFileHelper.cs | 83 | 图片文件校验与格式判断 |

## 关键链路优化（2026-07-31）

- PaddleOCRSharp 包下载由完整下载 3 次合并为 1 次（总流量 317MB→173MB，-45%）
- 下载/解压/替换/引擎初始化全部移出 UI 线程（消除 5-15s 界面冻结）
- 首次打开页面引擎后台懒加载
- 8KB 缓冲 → 64KB + 进度按百分比节流
- 网络失败自动重试 3 次 + 响应体读取独立超时
- 下载中防重入（按钮禁用）
- 下载源多路（华为云 NuGet 镜像优先，实测 ~5MB/s vs 官方 86KB/s）
- 设置页引擎管理：状态检测每次进入设置页刷新（IsVisibleChanged）；删除前经 `OcrTool.UnloadEngine` 释放原生 DLL 锁；`MainViewModel` 新增 `Tools` 只读列表供定位工具实例

## 依赖（公共共享类）

| 类 | 用途 |
|----|------|
| OcrHelper / PaddleOcrWrapper / EngineDownloader / ImageFileHelper | OCR 子系统内部 |
| ConfirmDialog | 引擎删除确认 |
| ThemeColors / GlowCardMarker | UI 一致性 |

## 相关文档

- 插件层总览 → [../04-plugins.md](../04-plugins.md)
- 设置页 → [../02-main-app.md](../02-main-app.md)
