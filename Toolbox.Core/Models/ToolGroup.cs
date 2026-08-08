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

    /// <summary>
    /// 当前是否展开——驱动 XAML 触发器（分组头箭头/标题变亮）与渲染式展开动画。
    /// 箭头方向由旋转动画表达（MainWindow.AnimateGroupArrow：折叠 0° ▸ / 展开 90° ▾）。
    /// </summary>
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
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
