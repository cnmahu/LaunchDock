using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using RolanClone.Core.Model;
using RolanClone.Windows.Icons;

namespace RolanClone.App.ViewModels;

public sealed class ItemViewModel : ViewModelBase
{
    private static readonly string[] Palette =
    {
        "#2F6FEB", "#12A594", "#F76B15", "#8E4EC6",
        "#E5484D", "#0FA968", "#F5A524", "#3E63DD"
    };

    private Bitmap? _icon;
    private double _iconSize = 48;
    private double _tileWidth = 100;
    private double _tileHeight = 96;
    private Thickness _tileMargin = new(12);
    private bool _showLabel = true;
    private Orientation _labelOrientation = Orientation.Vertical;
    private HorizontalAlignment _labelAlignment = HorizontalAlignment.Center;

    public ItemViewModel(ItemModel model, int colorIndex, bool isLive = false)
    {
        Model = model;
        IsLive = isLive;
        Initials = BuildInitials(model.DisplayName);
        Accent = new SolidColorBrush(Color.Parse(Palette[colorIndex % Palette.Length]));
        _ = LoadIconAsync(model.Target, model.CustomIcon);
    }

    public ItemModel Model { get; }
    public string Id => Model.Id;

    /// <summary>文件夹分组里的临时条目，不对应配置，不能重命名或删除。</summary>
    public bool IsLive { get; }

    public bool CanEdit => !IsLive;

    public string Name
    {
        get => Model.DisplayName;
        set
        {
            if (Model.DisplayName == value)
            {
                return;
            }

            Model.DisplayName = value;
            OnPropertyChanged();
        }
    }

    public string Target => Model.Target;
    public ItemKind Kind => Model.Kind;
    public string Initials { get; }
    public IBrush Accent { get; }

    public string ToolTip => Kind is ItemKind.Url ? Target : $"{Kind} · {Target}";

    public Bitmap? Icon
    {
        get => _icon;
        private set
        {
            if (SetProperty(ref _icon, value))
            {
                OnPropertyChanged(nameof(HasIcon));
            }
        }
    }

    public bool HasIcon => _icon is not null;

    public double IconSize
    {
        get => _iconSize;
        private set => SetProperty(ref _iconSize, value);
    }

    public double TileHeight
    {
        get => _tileHeight;
        private set => SetProperty(ref _tileHeight, value);
    }

    public double TileWidth
    {
        get => _tileWidth;
        private set => SetProperty(ref _tileWidth, value);
    }

    public Thickness TileMargin
    {
        get => _tileMargin;
        private set => SetProperty(ref _tileMargin, value);
    }

    public bool ShowLabel
    {
        get => _showLabel;
        private set => SetProperty(ref _showLabel, value);
    }

    public Orientation LabelOrientation
    {
        get => _labelOrientation;
        private set => SetProperty(ref _labelOrientation, value);
    }

    public HorizontalAlignment LabelAlignment
    {
        get => _labelAlignment;
        private set => SetProperty(ref _labelAlignment, value);
    }

    /// <summary>
    /// 按设置计算图标的固定尺寸与间距。格子尺寸只由图标大小和间距决定，
    /// 不跟随面板宽度变化（由 WrapPanel 负责换行）。
    /// </summary>
    public void ApplyAppearance(double iconSize, bool showLabel, bool labelOnRight, double spacing, bool isList = false)
    {
        IconSize = iconSize;
        ShowLabel = showLabel;
        LabelOrientation = labelOnRight ? Orientation.Horizontal : Orientation.Vertical;
        LabelAlignment = labelOnRight ? HorizontalAlignment.Left : HorizontalAlignment.Center;

        if (isList)
        {
            TileWidth = double.NaN;
            TileHeight = Math.Max(iconSize + 16, 40);
            TileMargin = new Thickness(2, 1);
            return;
        }

        TileWidth = labelOnRight ? iconSize + 120 : iconSize + 52;
        TileHeight = labelOnRight ? Math.Max(iconSize + 16, 44) : iconSize + 48;
        TileMargin = new Thickness(spacing / 2);
    }

    public void RefreshIcon()
    {
        Icon = null;
        _ = LoadIconAsync(Model.Target, Model.CustomIcon);
    }

    private async Task LoadIconAsync(string target, string? customIcon)
    {
        var bitmap = await Task.Run(() =>
        {
            var path = IconService.GetIconPath(target, customIcon);
            if (path is null)
            {
                return null;
            }

            try
            {
                return new Bitmap(path);
            }
            catch
            {
                return null;
            }
        });

        if (bitmap is not null)
        {
            await Dispatcher.UIThread.InvokeAsync(() => Icon = bitmap);
        }
    }

    private static string BuildInitials(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return "?";
        }

        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return string.Concat(parts[0][0], parts[1][0]).ToUpperInvariant();
        }

        return trimmed.Length <= 2 ? trimmed : trimmed[..2];
    }
}
