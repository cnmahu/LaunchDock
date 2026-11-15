using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using RolanClone.App.ViewModels;
using RolanClone.Core.Model;
using RolanClone.Windows;

namespace RolanClone.App.Views;

public partial class AppLibraryWindow : Window
{
    private readonly AppConfig _config;
    private readonly AppLibraryViewModel _viewModel;
    private ItemViewModel? _dragCandidate;
    private Point _dragStart;

    public AppLibraryWindow() : this(new AppConfig())
    {
    }

    public AppLibraryWindow(AppConfig config)
    {
        _config = config;
        InitializeComponent();
        _viewModel = new AppLibraryViewModel(config);
        DataContext = _viewModel;
        ApplyMaterial();

        // 云母是否真正生效要在窗口创建后才知道，降级到亚克力时需要补回底色
        PropertyChanged += (_, e) =>
        {
            if (e.Property == ActualTransparencyLevelProperty)
            {
                UpdateBackground();
            }
        };

        Opened += OnOpened;
        Activated += (_, _) => ApplyMaterial();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle != IntPtr.Zero)
        {
            WindowFrame.Apply(handle, IsDarkTheme());
        }
    }

    /// <summary>材质与不透明度跟随启动板设置；库内图标尺寸与间距固定，不受启动板影响。</summary>
    public void ApplyWindowMaterial() => ApplyMaterial();

    /// <summary>维护页修复图标后刷新库内图标。</summary>
    public void RefreshIcons() => _viewModel.RefreshIcons();

    /// <summary>双击条目时由启动板接住并加入当前分组。</summary>
    public event Action<ItemModel>? ItemActivated;

    private void ApplyMaterial()
    {
        var settings = _config.Settings;
        var dark = IsDarkTheme();

        TransparencyLevelHint = settings.Material switch
        {
            WindowMaterial.Mica => new[]
            {
                WindowTransparencyLevel.Mica,
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Transparent
            },
            WindowMaterial.Acrylic => new[]
            {
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Transparent
            },
            _ => new[] { WindowTransparencyLevel.Transparent }
        };

        UpdateBackground();
    }

    /// <summary>
    /// 云母由 DWM 直接绘制在窗口背景上，背景必须透明才能透出（色调跟随壁纸）；
    /// 亚克力 / 纯色模式才叠加底色，不透明度滑块也只对这两种模式生效。
    /// </summary>
    private void UpdateBackground()
    {
        var settings = _config.Settings;
        var dark = IsDarkTheme();

        if (settings.Material == WindowMaterial.Mica
            && ActualTransparencyLevel == WindowTransparencyLevel.Mica)
        {
            Background = Brushes.Transparent;
            return;
        }

        var baseAlpha = settings.Material == WindowMaterial.Solid ? 1.0 : dark ? 0.88 : 0.72;
        var alpha = (byte)Math.Clamp((int)Math.Round(255 * baseAlpha * settings.Opacity), 40, 255);
        var baseColor = dark ? Color.FromRgb(24, 24, 24) : Color.FromRgb(255, 255, 255);
        Background = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
    }

    private bool IsDarkTheme()
    {
        var requested = Application.Current?.RequestedThemeVariant;
        return requested == ThemeVariant.Dark
               || (requested != ThemeVariant.Light && ActualThemeVariant == ThemeVariant.Dark);
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

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
