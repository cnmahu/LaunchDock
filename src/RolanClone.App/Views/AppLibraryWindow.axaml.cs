using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RolanClone.App.ViewModels;
using RolanClone.Core.Model;

namespace RolanClone.App.Views;

public partial class AppLibraryWindow : Window
{
    private readonly AppLibraryViewModel _viewModel;
    private ItemViewModel? _dragCandidate;
    private Point _dragStart;

    public AppLibraryWindow() : this(new AppConfig())
    {
    }

    public AppLibraryWindow(AppConfig config)
    {
        InitializeComponent();
        _viewModel = new AppLibraryViewModel(config);
        DataContext = _viewModel;

        Activated += (_, _) => _viewModel.ApplyAppearance();
    }

    /// <summary>设置变化时由启动板调用，保持两边图标大小与间距一致。</summary>
    public void ApplyAppearance() => _viewModel.ApplyAppearance();

    /// <summary>维护页修复图标后刷新库内图标。</summary>
    public void RefreshIcons() => _viewModel.RefreshIcons();

    /// <summary>双击条目时由启动板接住并加入当前分组。</summary>
    public event Action<ItemModel>? ItemActivated;

    private void OnCloseTipClick(object? sender, RoutedEventArgs e) => TipBar.IsVisible = false;

    private void OnTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: ItemViewModel item })
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed)
        {
            _dragCandidate = item;
            _dragStart = point.Position;
        }
    }

    private async void OnTilePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragCandidate is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            _dragCandidate = null;
            return;
        }

        var delta = point.Position - _dragStart;
        if (Math.Abs(delta.X) < 6 && Math.Abs(delta.Y) < 6)
        {
            return;
        }

        var item = _dragCandidate;
        _dragCandidate = null;

#pragma warning disable CS0618 // 自定义拖拽格式仍走 DataObject API
        var data = new DataObject();
        data.Set(AppLibraryDrag.Format, AppLibraryDrag.Serialize(item.Model));

        try
        {
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
        }
        catch
        {
            // 拖拽被取消时忽略
        }
#pragma warning restore CS0618
    }

    private void OnTileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: ItemViewModel item })
        {
            return;
        }

        ItemActivated?.Invoke(Clone(item.Model));
        e.Handled = true;
    }

    private static ItemModel Clone(ItemModel source) => new()
    {
        DisplayName = source.DisplayName,
        Kind = source.Kind,
        Target = source.Target,
        Arguments = source.Arguments,
        CustomIcon = source.CustomIcon
    };
}
