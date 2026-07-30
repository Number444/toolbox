using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace Toolbox.Helpers;

/// <summary>
/// PaddleOCRSharp 动态加载封装 —— 不依赖任何 NuGet 引用，运行时 Assembly.LoadFrom 加载。
/// 引擎文件需提前下载到指定目录（包含 PaddleOCRSharp.dll、原生 DLL、models/）。
/// 加载失败时 IsAvailable 返回 false，调用方应回退到 Windows.Media.Ocr。
/// </summary>
public sealed class PaddleOcrWrapper : IDisposable
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    private dynamic? _engine;
    private bool _loaded;

    /// <summary>引擎是否已成功加载可用</summary>
    public bool IsAvailable => _loaded;

    /// <summary>引擎目录路径（加载时传入）</summary>
    public string? EnginePath { get; private set; }

    /// <summary>加载失败时的具体原因（用于 UI 提示）</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// 验证引擎目录的完整性（不实际加载），返回 null 表示完整，否则返回缺失项描述。
    /// </summary>
    public static string? ValidateDirectory(string enginePath)
    {
        if (!Directory.Exists(enginePath))
            return "引擎目录不存在";
        if (!File.Exists(Path.Combine(enginePath, "PaddleOCRSharp.dll")))
            return "缺少 PaddleOCRSharp.dll";
        if (!File.Exists(Path.Combine(enginePath, "paddle_inference.dll")))
            return "缺少 paddle_inference.dll（推理运行时未安装完整）";
        if (!File.Exists(Path.Combine(enginePath, "PaddleOCR.dll")))
            return "缺少 PaddleOCR.dll（桥接层未安装）";
        if (!File.Exists(Path.Combine(enginePath, "Newtonsoft.Json.dll")))
            return "缺少 Newtonsoft.Json.dll（桥接层依赖未安装）";
        var modelsDir = Path.Combine(enginePath, "models");
        if (!Directory.Exists(Path.Combine(modelsDir, "PP-OCRv5_mobile_det_infer")))
            return "缺少检测模型";
        if (!Directory.Exists(Path.Combine(modelsDir, "PP-OCRv5_mobile_rec_infer")))
            return "缺少识别模型";
        if (!Directory.Exists(Path.Combine(modelsDir, "PP-OCRv5_mobile_cls_infer")))
            return "缺少方向分类模型";
        if (!File.Exists(Path.Combine(modelsDir, "ppocr_keys.txt")))
            return "缺少字符字典";
        return null;
    }

    /// <summary>
    /// 加载 PaddleOCR 引擎。
    /// enginePath 为引擎根目录，结构：
    ///   enginePath/
    ///   ├── PaddleOCRSharp.dll         (C# 封装)
    ///   ├── paddle_inference.dll 等    (原生推理库)
    ///   └── models/                   (模型文件)
    /// </summary>
    public bool Load(string enginePath)
    {
        if (_loaded) return true;

        // 完整性检查
        var missing = ValidateDirectory(enginePath);
        if (missing != null)
        {
            LastError = missing;
            return false;
        }

        var dllPath = Path.Combine(enginePath, "PaddleOCRSharp.dll");

        try
        {
            // 让原生 DLL 解析器能找到 enginePath 下的 Paddle Inference DLL
            SetDllDirectory(enginePath);

            var asm = Assembly.LoadFrom(dllPath);

            // 配置模型路径（PP-OCRv5 内置模型，正斜杠兼容 C++ 库）
            var configType = asm.GetType("PaddleOCRSharp.OCRModelConfig")
                ?? throw new InvalidOperationException("PaddleOCRSharp.OCRModelConfig 类型未找到");
            dynamic config = Activator.CreateInstance(configType)!;
            string modelRoot = (Path.Combine(enginePath, "models") + "/").Replace('\\', '/');
            config.det_infer = (modelRoot + "PP-OCRv5_mobile_det_infer").Replace('\\', '/');
            config.rec_infer = (modelRoot + "PP-OCRv5_mobile_rec_infer").Replace('\\', '/');
            config.cls_infer = (modelRoot + "PP-OCRv5_mobile_cls_infer").Replace('\\', '/');
            config.keys = (modelRoot + "ppocr_keys.txt").Replace('\\', '/');

            var paramType = asm.GetType("PaddleOCRSharp.OCRParameter")
                ?? throw new InvalidOperationException("PaddleOCRSharp.OCRParameter 类型未找到");
            dynamic param = Activator.CreateInstance(paramType)!;

            var engineType = asm.GetType("PaddleOCRSharp.PaddleOCREngine")
                ?? throw new InvalidOperationException("PaddleOCRSharp.PaddleOCREngine 类型未找到");
            _engine = Activator.CreateInstance(engineType, config, param);

            _loaded = true;
            EnginePath = enginePath;
            LastError = null;
            return true;
        }
        catch (TargetInvocationException ex)
        {
            var inner = ex.InnerException;
            LastError = inner != null
                ? $"引擎初始化失败：{inner.GetType().Name}: {inner.Message}"
                : $"引擎初始化失败：{ex.Message}";
            return false;
        }
        catch (DllNotFoundException ex)
        {
            LastError = $"原生 DLL 缺失：{ex.Message}";
            return false;
        }
        catch (InvalidOperationException ex)
        {
            LastError = $"API 不兼容：{ex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            LastError = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    /// <summary>识别图片文件，返回纯文本（失败返回 null）</summary>
    public string? RecognizeFile(string imagePath)
    {
        if (!_loaded || _engine == null) return null;
        try
        {
            dynamic result = _engine.DetectText(imagePath);
            return result?.Text as string;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PaddleOCR 识别失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>识别 BitmapSource（临时存为 PNG 后调用引擎），失败返回 null</summary>
    public string? RecognizeBitmap(BitmapSource bitmap)
    {
        if (!_loaded || _engine == null) return null;

        var tempPath = Path.Combine(Path.GetTempPath(), $"paddle_ocr_{Guid.NewGuid()}.png");
        try
        {
            using var stream = new FileStream(tempPath, FileMode.Create);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(stream);
            stream.Close();

            dynamic result = _engine.DetectText(tempPath);
            return result?.Text as string;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PaddleOCR 识别失败: {ex.Message}");
            return null;
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* 清理失败不影响主流程 */ }
        }
    }

    public void Dispose()
    {
        if (_engine != null)
        {
            try { ((IDisposable)_engine).Dispose(); } catch { }
            _engine = null;
        }
        _loaded = false;
    }
}
