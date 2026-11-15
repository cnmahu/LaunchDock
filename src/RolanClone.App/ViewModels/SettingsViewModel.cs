using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Styling;
using RolanClone.Core.Model;
using RolanClone.Core.Security;
using RolanClone.Core.Storage;
using RolanClone.Windows.Startup;

namespace RolanClone.App.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
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
