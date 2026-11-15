using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using LaunchDock.Core.Model;
using LaunchDock.Core.Security;
using LaunchDock.Core.Storage;
using LaunchDock.Windows.Fonts;
using LaunchDock.Windows.Startup;

namespace LaunchDock.App.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    public const string DefaultFontFamily = "Segoe UI";

    /// <summary>中文回退字体：放在 Segoe UI 之后，只接管 Segoe UI 缺失的 CJK 字形。
    /// 注意 Skia 不提供 "Microsoft YaHei Light" 这个族名，细体只能通过 FontWeight.Light 取到。</summary>
    public const string DefaultCjkFamily = "Microsoft YaHei";

    /// <summary>默认字体链：拉丁字母走 Segoe UI，中文走 <see cref="DefaultCjkFamily"/>。</summary>
    public const string DefaultFamilyChain = DefaultFontFamily + ", " + DefaultCjkFamily;

    public const double MinFontSize = 10;
    public const double MaxFontSize = 18;
    public const double DefaultFontSize = 12;

    /// <summary>字号 token 表：key 为资源名，value 为缩放前的原始字号。</summary>
    private static readonly (string Key, double Base)[] FontSizeTokens =
    {
        ("FsBase", DefaultFontSize),
        ("Fs11", 11), ("Fs11.5", 11.5), ("Fs12", 12), ("Fs12.5", 12.5),
        ("Fs13", 13), ("Fs13.5", 13.5), ("Fs14", 14), ("Fs15", 15),
        ("Fs16", 16), ("Fs18", 18), ("Fs24", 24)
    };

    private static List<string>? _installedFonts;

    /// <summary>系统已安装字体（仅保留 Avalonia/Skia 可按名匹配的字体，进程内缓存一次）。</summary>
    public static List<string> InstalledFonts => _installedFonts ??= LoadInstalledFonts();

    private static List<string> LoadInstalledFonts()
    {
        var gdiFonts = SystemFontLister.GetInstalled();
        try
        {
            // Skia 暴露的字体族集合即“可按名称匹配”的集合；
            // GDI 独有的族（如按字重拆分的“等线 Light”）在 Avalonia 里无法命中，直接过滤掉
            var skiaFamilies = SkiaSharp.SKFontManager.Default.FontFamilies;
            return gdiFonts
                .Where(n => skiaFamilies.Contains(n, StringComparer.OrdinalIgnoreCase))
                .OrderBy(n => n, StringComparer.CurrentCulture)
                .ToList();
        }
        catch
        {
            return gdiFonts.OrderBy(n => n, StringComparer.CurrentCulture).ToList();
        }
    }

    /// <summary>
    /// 构造 FontFamily 字符串。
    /// Avalonia 对组合字体的匹配要求“解析结果名包含请求名”，而本地化字体名（如“宋体”）
    /// 与 Skia 报告的字体名（"SimSun"）不一致会导致匹配失败回退默认字体；
    /// 这里把两种名称都放进回退链，任意语言环境的报告形式都能命中。
    /// </summary>
    private static string BuildFontFamilyString(string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return DefaultFamilyChain;
        }

        var names = new List<string> { requested };
        try
        {
            var skiaFamilies = SkiaSharp.SKFontManager.Default.FontFamilies;
            if (skiaFamilies.Contains(requested, StringComparer.OrdinalIgnoreCase))
            {
                using var typeface = SkiaSharp.SKFontManager.Default.MatchFamily(
                    requested, SkiaSharp.SKFontStyle.Normal);
                var reportedName = typeface?.FamilyName;
                if (!string.IsNullOrEmpty(reportedName) &&
                    !names.Contains(reportedName, StringComparer.OrdinalIgnoreCase))
                {
                    names.Add(reportedName);
                }
            }
        }
        catch
        {
            // Skia 探测失败时仅使用原始名称，交由回退链兜底
        }

        names.Add(DefaultFontFamily);
        names.Add(DefaultCjkFamily);
        return string.Join(", ", names);
    }

    private readonly SettingsModel _settings;
    private readonly Action _onChanged;
    private readonly Func<MaintenanceAction, string>? _runMaintenance;
    private string _maintenanceStatus = string.Empty;

    public SettingsViewModel(SettingsModel settings, Action onChanged,
        Func<MaintenanceAction, string>? runMaintenance = null)
    {
        _settings = settings;
        _onChanged = onChanged;
        _runMaintenance = runMaintenance;
    }

    /// <summary>字体下拉框选项：首项为默认字体，其后为系统字体。</summary>
    public IReadOnlyList<string> FontChoices { get; } =
        new[] { $"默认（{DefaultFontFamily} / {DefaultCjkFamily}）" }.Concat(InstalledFonts).ToArray();

    public string MaintenanceStatus
    {
        get => _maintenanceStatus;
        private set => SetProperty(ref _maintenanceStatus, value);
    }

    public void RunMaintenance(MaintenanceAction action)
    {
        MaintenanceStatus = _runMaintenance?.Invoke(action) ?? "当前不可用";
    }

    public string VersionText =>
        $"启坞 LaunchDock {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}";

    public string ConfigPath => ConfigStore.ConfigPath;

    public string NotesDirectoryPath => NotesStore.Resolve(_settings.NotesDirectory);

    public bool HasCustomNotesDirectory => !string.IsNullOrWhiteSpace(_settings.NotesDirectory);

    public void SetNotesDirectory(string? path)
    {
        _settings.NotesDirectory = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
        OnPropertyChanged(nameof(NotesDirectoryPath));
        OnPropertyChanged(nameof(HasCustomNotesDirectory));
        _onChanged();
    }

    public bool TopMost
    {
        get => _settings.TopMost;
        set => SetBool(() => _settings.TopMost, v => _settings.TopMost = v, value);
    }

    public bool Blur
    {
        get => _settings.Blur;
        set => SetBool(() => _settings.Blur, v => _settings.Blur = v, value);
    }

    public double Opacity
    {
        get => _settings.Opacity;
        set
        {
            // Slider 初始化时可能先推入 0，忽略这个值避免把不透明度重置为最小值
            if (value <= 0.01)
            {
                return;
            }

            var clamped = Math.Clamp(value, 0.5, 1.0);
            if (Math.Abs(_settings.Opacity - clamped) < 0.001)
            {
                return;
            }

            _settings.Opacity = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OpacityLabel));
            _onChanged();
        }
    }

    public string OpacityLabel => $"{Math.Round(_settings.Opacity * 100)}%";

    public int ThemeIndex
    {
        get => (int)_settings.Theme;
        set
        {
            var theme = (AppTheme)Math.Clamp(value, 0, 2);
            if (_settings.Theme == theme)
            {
                return;
            }

            _settings.Theme = theme;
            ApplyTheme(theme);
            OnPropertyChanged();
            _onChanged();
        }
    }

    public int MaterialIndex
    {
        get => (int)_settings.Material;
        set
        {
            var material = (WindowMaterial)Math.Clamp(value, 0, 2);
            if (_settings.Material == material)
            {
                return;
            }

            _settings.Material = material;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public static void ApplyTheme(AppTheme theme)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    /// <summary>
    /// 把字体设置写入应用级资源（AppFontFamily / AppFontWeight / FsXX），
    /// 所有通过 DynamicResource 引用这些资源的界面文字随之更新。
    /// </summary>
    public static void ApplyFont(SettingsModel settings)
    {
        if (Application.Current is null)
        {
            return;
        }

        var resources = Application.Current.Resources;

        var fontFamily = new FontFamily(BuildFontFamilyString(settings.FontFamily));
        resources["AppFontFamily"] = fontFamily;
        // Fluent 主题控件的 ControlTheme 用这个资源设置字体，必须同步覆盖
        resources["ContentControlThemeFontFamily"] = fontFamily;

        resources["AppFontWeight"] = settings.FontWeight switch
        {
            AppFontWeight.Light => FontWeight.Light,
            AppFontWeight.Regular => FontWeight.Normal,
            AppFontWeight.Medium => FontWeight.Medium,
            AppFontWeight.SemiBold => FontWeight.SemiBold,
            AppFontWeight.Bold => FontWeight.Bold,
            _ => FontWeight.Normal
        };

        var scale = Math.Clamp(settings.FontSize, MinFontSize, MaxFontSize) / DefaultFontSize;
        foreach (var (key, baseSize) in FontSizeTokens)
        {
            resources[key] = Math.Round(baseSize * scale, 1);
        }

        // 资源变动对已打开窗口里的控件模板不一定及时生效，逐个窗口再显式推一次
        if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var window in desktop.Windows)
            {
                window.Resources["ContentControlThemeFontFamily"] = fontFamily;
                window.FontFamily = fontFamily;
            }
        }
    }

    public int LayoutIndex
    {
        get => _settings.Layout == PanelLayout.Vertical ? 1 : 0;
        set
        {
            var target = value == 1 ? PanelLayout.Vertical : PanelLayout.Horizontal;
            if (_settings.Layout == target)
            {
                return;
            }

            _settings.Layout = target;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public double IconSizePx
    {
        get => _settings.IconSizePx;
        set
        {
            var rounded = Math.Clamp(Math.Round(value), 24, 96);
            if (Math.Abs(_settings.IconSizePx - rounded) < 0.5)
            {
                return;
            }

            _settings.IconSizePx = (int)rounded;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IconSizeLabel));
            _onChanged();
        }
    }

    public string IconSizeLabel => $"{_settings.IconSizePx} px";

    public double IconSpacing
    {
        get => _settings.IconSpacing;
        set
        {
            var rounded = Math.Clamp(Math.Round(value), 0, 48);
            if (Math.Abs(_settings.IconSpacing - rounded) < 0.5)
            {
                return;
            }

            _settings.IconSpacing = rounded;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IconSpacingLabel));
            _onChanged();
        }
    }

    public string IconSpacingLabel => $"{_settings.IconSpacing:0} px";

    public int LabelPositionIndex
    {
        get => _settings.LabelPosition switch
        {
            LabelPosition.Bottom => 0,
            LabelPosition.Right => 1,
            _ => 2
        };
        set
        {
            var target = value switch
            {
                0 => LabelPosition.Bottom,
                1 => LabelPosition.Right,
                _ => LabelPosition.None
            };
            if (_settings.LabelPosition == target)
            {
                return;
            }

            _settings.LabelPosition = target;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public bool EdgeSnap
    {
        get => _settings.EdgeSnap;
        set => SetBool(() => _settings.EdgeSnap, v => _settings.EdgeSnap = v, value);
    }

    public bool LockPosition
    {
        get => _settings.LockPosition;
        set => SetBool(() => _settings.LockPosition, v => _settings.LockPosition = v, value);
    }

    public bool HideOnLostFocus
    {
        get => _settings.HideOnLostFocus;
        set => SetBool(() => _settings.HideOnLostFocus, v => _settings.HideOnLostFocus = v, value);
    }

    public bool HideAfterLaunch
    {
        get => _settings.HideAfterLaunch;
        set => SetBool(() => _settings.HideAfterLaunch, v => _settings.HideAfterLaunch = v, value);
    }

    public bool HideOnEmptyDoubleClick
    {
        get => _settings.HideOnEmptyDoubleClick;
        set => SetBool(() => _settings.HideOnEmptyDoubleClick, v => _settings.HideOnEmptyDoubleClick = v, value);
    }

    public bool CtrlPreventHide
    {
        get => _settings.CtrlPreventHide;
        set => SetBool(() => _settings.CtrlPreventHide, v => _settings.CtrlPreventHide = v, value);
    }

    public bool StartHidden
    {
        get => _settings.StartHidden;
        set => SetBool(() => _settings.StartHidden, v => _settings.StartHidden = v, value);
    }

    public int CloseBehaviorIndex
    {
        get => _settings.CloseBehavior == CloseBehavior.Exit ? 1 : 0;
        set
        {
            var target = value == 1 ? CloseBehavior.Exit : CloseBehavior.MinimizeToTray;
            if (_settings.CloseBehavior == target)
            {
                return;
            }

            _settings.CloseBehavior = target;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public int FontFamilyIndex
    {
        get
        {
            var family = _settings.FontFamily;
            if (string.IsNullOrWhiteSpace(family))
            {
                return 0;
            }

            var index = InstalledFonts.FindIndex(
                f => string.Equals(f, family, StringComparison.OrdinalIgnoreCase));
            return index < 0 ? 0 : index + 1;
        }
        set
        {
            var family = value <= 0 || value - 1 >= InstalledFonts.Count
                ? null
                : InstalledFonts[value - 1];
            if (string.Equals(family, _settings.FontFamily, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _settings.FontFamily = family;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public double FontSize
    {
        get => _settings.FontSize;
        set
        {
            var clamped = Math.Clamp(Math.Round(value), MinFontSize, MaxFontSize);
            if (Math.Abs(_settings.FontSize - clamped) < 0.01)
            {
                return;
            }

            _settings.FontSize = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FontSizeLabel));
            _onChanged();
        }
    }

    public string FontSizeLabel => $"{_settings.FontSize:0}";

    public int FontWeightIndex
    {
        get => (int)_settings.FontWeight;
        set
        {
            var target = (AppFontWeight)Math.Clamp(value, 0, 4);
            if (_settings.FontWeight == target)
            {
                return;
            }

            _settings.FontWeight = target;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public bool AutoStart
    {
        get => _settings.AutoStart;
        set
        {
            if (_settings.AutoStart == value)
            {
                return;
            }

            _settings.AutoStart = value;
            AutostartManager.SetEnabled(value);
            OnPropertyChanged();
            _onChanged();
        }
    }

    public string Hotkey
    {
        get => _settings.Hotkey;
        set
        {
            if (_settings.Hotkey == value)
            {
                return;
            }

            _settings.Hotkey = value;
            OnPropertyChanged();
        }
    }

    public bool EnableHotkey
    {
        get => _settings.EnableHotkey;
        set => SetBool(() => _settings.EnableHotkey, v => _settings.EnableHotkey = v, value);
    }

    public int ShowPositionIndex
    {
        get => (int)_settings.ShowPosition;
        set
        {
            var target = (ShowPosition)Math.Clamp(value, 0, 2);
            if (_settings.ShowPosition == target)
            {
                return;
            }

            _settings.ShowPosition = target;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public int OpenModeIndex
    {
        get => _settings.SingleClickRun ? 0 : 1;
        set
        {
            var target = value != 1;
            if (_settings.SingleClickRun == target)
            {
                return;
            }

            _settings.SingleClickRun = target;
            OnPropertyChanged();
            _onChanged();
        }
    }

    public bool ShowOnXButton1
    {
        get => _settings.ShowOnXButton1;
        set => SetBool(() => _settings.ShowOnXButton1, v => _settings.ShowOnXButton1 = v, value);
    }

    public bool ShowOnXButton2
    {
        get => _settings.ShowOnXButton2;
        set => SetBool(() => _settings.ShowOnXButton2, v => _settings.ShowOnXButton2 = v, value);
    }

    public bool ShowOnMiddleClick
    {
        get => _settings.ShowOnMiddleClick;
        set => SetBool(() => _settings.ShowOnMiddleClick, v => _settings.ShowOnMiddleClick = v, value);
    }

    public bool CloseOnEsc
    {
        get => _settings.CloseOnEsc;
        set => SetBool(() => _settings.CloseOnEsc, v => _settings.CloseOnEsc = v, value);
    }

    public bool HasPassword => !string.IsNullOrWhiteSpace(_settings.LockPasswordHash);

    public string PasswordStatus => HasPassword ? "已设置密码" : "未设置密码";

    public bool AutoLock
    {
        get => _settings.AutoLock;
        set => SetBool(() => _settings.AutoLock, v => _settings.AutoLock = v, value);
    }

    public void SetPassword(string password)
    {
        _settings.LockPasswordHash = PasswordHasher.Hash(password);
        if (!_settings.AutoLock)
        {
            _settings.AutoLock = true;
            OnPropertyChanged(nameof(AutoLock));
        }

        OnPropertyChanged(nameof(HasPassword));
        OnPropertyChanged(nameof(PasswordStatus));
        _onChanged();
    }

    public void ClearPassword()
    {
        _settings.LockPasswordHash = null;
        OnPropertyChanged(nameof(HasPassword));
        OnPropertyChanged(nameof(PasswordStatus));
        _onChanged();
    }

    private void SetBool(Func<bool> get, Action<bool> set, bool value, [CallerMemberName] string? propertyName = null)
    {
        if (get() == value)
        {
            return;
        }

        set(value);
        OnPropertyChanged(propertyName);
        _onChanged();
    }
}
