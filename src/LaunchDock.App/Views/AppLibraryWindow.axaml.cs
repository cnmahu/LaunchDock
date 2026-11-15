using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using LaunchDock.App.Services;
using LaunchDock.App.ViewModels;
using LaunchDock.Core.Model;
using LaunchDock.Windows;

namespace LaunchDock.App.Views;

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

        // 云母用自绘底色（见 MicaBackdrop），窗口保持不透明才能用 ClearType 渲染文字
        TransparencyLevelHint = settings.Material switch
        {
            WindowMaterial.Acrylic => new[] { WindowTransparencyLevel.AcrylicBlur },
            _ => new[] { WindowTransparencyLevel.None }
        };

        UpdateBackground();
    }

    /// <summary>
    /// 云母 / 亚克力由 DWM 绘制在窗口背后，背景必须透明才能透出；实际级别没拿到时用不透明底色兜底。
    /// </summary>
    private void UpdateBackground()
    {
        var settings = _config.Settings;
        var dark = IsDarkTheme();

        if (settings.Material == WindowMaterial.Mica)
        {
            // 自绘云母：壁纸模糊 + 主题色调，拿不到壁纸时回退到不透明底色
            Background = MicaBackdrop.Create(dark) ?? new SolidColorBrush(dark ? Color.FromRgb(24, 24, 24) : Color.FromRgb(255, 255, 255));
            return;
        }

        var baseColor = dark ? Color.FromRgb(24, 24, 24) : Color.FromRgb(255, 255, 255);

        // 亚克力拿到模糊背板时才叠半透明底色，拿不到就用不透明底色兜底
        var acrylic = settings.Material == WindowMaterial.Acrylic
                      && ActualTransparencyLevel == WindowTransparencyLevel.AcrylicBlur;
        if (!acrylic)
        {
            Background = new SolidColorBrush(baseColor);
            return;
        }

        var baseAlpha = dark ? 0.88 : 0.72;
        var alpha = (byte)Math.Clamp((int)Math.Round(255 * baseAlpha * settings.Opacity), 40, 255);
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
