using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Toolbox.Models;
using Toolbox.Services;

namespace Toolbox.ViewModels;

/// <summary>
/// 主窗口 ViewModel —— 管理工具分组列表、选中状态、搜索过滤
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    private readonly ToolRegistry _registry = new();
    private ITool? _selectedTool;
    private UIElement? _cachedContent;
    private ITool? _cachedTool;
    private string _searchText = "";

    /// <summary>按分类分组的全量工具列表（不随搜索改变）</summary>
    public ObservableCollection<ToolGroup> AllGroups { get; } = [];

    /// <summary>绑定到 UI 的可见分组列表（搜索过滤后）</summary>
    public ObservableCollection<ToolGroup> VisibleGroups { get; } = [];

    public ITool? SelectedTool
    {
        get => _selectedTool;
        set
        {
            if (_selectedTool != value)
            {
                _selectedTool = value;
                _cachedContent = null; // 清除缓存，下次访问时重新创建
                _cachedTool = null;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentContent));
                OnPropertyChanged(nameof(SelectedToolName));
                OnPropertyChanged(nameof(SelectedToolDescription));
            }
        }
    }

    public UIElement? CurrentContent
    {
        get
        {
            if (_selectedTool == null) return null;
            // 相同工具只创建一次 UI，避免 TransitioningContentControl 因新对象而反复淡入
            if (_selectedTool != _cachedTool || _cachedContent == null)
            {
                _cachedContent = _selectedTool.CreateContent();
                _cachedTool = _selectedTool;
            }
            return _cachedContent;
        }
    }

    public string SelectedToolName => _selectedTool?.Name ?? "";
    public string SelectedToolDescription => _selectedTool?.Description ?? "";

    /// <summary>搜索关键词——变更时自动过滤分组</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText != value)
            {
                _searchText = value;
                OnPropertyChanged();
                ApplyFilter();
            }
        }
    }

    /// <summary>显示的工具总数（用于状态栏）</summary>
    public int VisibleToolCount => VisibleGroups.Sum(g => g.Tools.Count);

    /// <summary>全量工具总数</summary>
    public int TotalToolCount => AllGroups.Sum(g => g.Tools.Count);

    // =========================

    public MainViewModel()
    {
        _registry.DiscoverTools();
        BuildGroups();
        ApplyFilter();

        // 默认选中第一个工具
        if (VisibleGroups.Count > 0 && VisibleGroups[0].Tools.Count > 0)
            _selectedTool = VisibleGroups[0].Tools[0];
    }

    /// <summary>将发现的所有工具按分类分组</summary>
    private void BuildGroups()
    {
        // 按 ToolCategory.All 的顺序建立分组
        var categoryOrder = ToolCategory.All.ToList();

        var grouped = _registry.Tools
            .GroupBy(t => t.Category)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var cat in categoryOrder)
        {
            if (!grouped.TryGetValue(cat, out var tools)) continue;

            var group = new ToolGroup
            {
                CategoryName = cat,
                // 仅"系统维护"默认展开，其他分类默认收起
                IsExpanded = cat == ToolCategory.System
            };
            foreach (var tool in tools)
                group.Tools.Add(tool);

            AllGroups.Add(group);
        }
    }

    /// <summary>根据搜索关键词过滤分组</summary>
    private void ApplyFilter()
    {
        VisibleGroups.Clear();

        var keyword = _searchText?.Trim().ToLower() ?? "";

        foreach (var group in AllGroups)
        {
            if (string.IsNullOrEmpty(keyword))
            {
                // 无搜索关键词 → 显示全量
                VisibleGroups.Add(group);
                continue;
            }

            // 筛选匹配 Name 或 Description 的工具
            var matched = group.Tools
                .Where(t => t.Name.ToLower().Contains(keyword)
                         || t.Description.ToLower().Contains(keyword))
                .ToList();

            if (matched.Count == 0) continue;

            // 克隆一个只含匹配工具的分组（浅拷贝属性）
            var filteredGroup = new ToolGroup
            {
                CategoryName = group.CategoryName,
                IsExpanded = true // 搜索时强制展开
            };
            foreach (var tool in matched)
                filteredGroup.Tools.Add(tool);

            VisibleGroups.Add(filteredGroup);
        }

        OnPropertyChanged(nameof(VisibleToolCount));
        OnPropertyChanged(nameof(TotalToolCount));
    }

    // =========================

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}