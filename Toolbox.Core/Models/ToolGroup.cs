using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Toolbox.Models;

/// <summary>
/// 工具分组模型 —— 一个分类下的一组工具，含展开/折叠状态
/// </summary>
public class ToolGroup : INotifyPropertyChanged
{
    /// <summary>分类名称（对应 ToolCategory 常量）</summary>
    public string CategoryName { get; init; } = "";

    /// <summary>该分类下的工具列表</summary>
    public ObservableCollection<ITool> Tools { get; } = [];

    /// <summary>当前是否展开</summary>
    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ArrowText));
                OnPropertyChanged(nameof(HoverIcon));
            }
        }
    }

    /// <summary>展开/折叠箭头字符（展开 ▾，折叠 ▸）</summary>
    public string ArrowText => _isExpanded ? "▾" : "▸";

    /// <summary>
    /// 图标字符：鼠标悬停时显示箭头 ▾/▸，否则显示文件夹 📁
    /// 展开时默认显示箭头，折叠后默认显示文件夹，鼠标悬停转箭头
    /// </summary>
    private bool _isHovered;
    public bool IsHovered
    {
        get => _isHovered;
        set
        {
            if (_isHovered != value)
            {
                _isHovered = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HoverIcon));
            }
        }
    }

    /// <summary>显示的图标：悬停时箭头，否则文件夹</summary>
    public string HoverIcon => _isHovered ? ArrowText : "📁";

    /// <summary>分类名称颜色（折叠时灰色，展开时正常）</summary>
    public string CategoryColor => _isExpanded ? "TextPrimaryBrush" : "TextSecondaryBrush";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}