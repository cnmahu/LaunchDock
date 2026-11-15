using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RolanClone.App.ViewModels;
using RolanClone.Core.Model;
using RolanClone.Core.Storage;
using RolanClone.Windows;
using RolanClone.Windows.Input;
using RolanClone.Windows.Shortcuts;

namespace RolanClone.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private readonly Func<string, bool> _tryChangeHotkey;
    private string? _hotkeyBeforeRecording;

    public SettingsWindow() : this(new SettingsModel(), () => { }, _ => false)
    {
    }

    public SettingsWindow(SettingsModel settings, Action onChanged, Func<string, bool> tryChangeHotkey,
        Func<MaintenanceAction, string>? runMaintenance = null)
    {
        InitializeComponent();

        _tryChangeHotkey = tryChangeHotkey;
        _viewModel = new SettingsViewModel(settings, onChanged, runMaintenance);
        DataContext = _viewModel;

        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle != IntPtr.Zero)
        {
            WindowFrame.Apply(handle, darkMode: false);
        }
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        var about = new AboutWindow();
        about.Show(this);
    }

    private void OnOpenConfigFolderClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(ConfigStore.ConfigDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = ConfigStore.ConfigDirectory,
                UseShellExecute = true
            });
        }
        catch
        {
            // 打开目录失败时忽略
        }
    }

    private async void OnChangeNotesDirectoryClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择便签存储目录",
            AllowMultiple = false
        });

        if (folders.Count == 0)
        {
            return;
        }

        var path = folders[0].TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            _viewModel.SetNotesDirectory(path);
        }
    }

    private void OnOpenNotesDirectoryClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_viewModel.NotesDirectoryPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = _viewModel.NotesDirectoryPath,
                UseShellExecute = true
            });
        }
        catch
        {
            // 打开目录失败时忽略
        }
    }

    private void OnResetNotesDirectoryClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.SetNotesDirectory(null);
    }

    private void OnHotkeyKeyDown(object? sender, KeyEventArgs e)
    {
        e.Handled = true;

        // 用物理键判断：不受中文输入法（IME）拦截影响
        var key = Enum.TryParse<Key>(e.PhysicalKey.ToString(), out var physical) && physical != Key.None
            ? physical
            : e.Key;

        // Esc：取消本次录制，回滚到进入录制前的热键
        if (key == Key.Escape)
        {
            var restore = _hotkeyBeforeRecording;
            if (!string.IsNullOrEmpty(restore)
                && restore != _viewModel.Hotkey
                && _tryChangeHotkey(restore))
            {
                _viewModel.Hotkey = restore;
            }
            HotkeyBox.Text = _viewModel.Hotkey;
            HotkeyStatus.Text = string.Empty;
            return;
        }

        // 只按了修饰键：等待主键
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
        {
            return;
        }

        var keyName = key switch
        {
            >= Key.A and <= Key.Z => ((char)('A' + (key - Key.A))).ToString(),
            >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
            >= Key.F1 and <= Key.F24 => $"F{1 + (key - Key.F1)}",
            Key.Space => "Space",
            Key.Enter => "Enter",
            Key.Tab => "Tab",
            Key.OemTilde => "`",
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            Key.OemBackslash => "\\",
            _ => null
        };

        if (keyName is null)
        {
            HotkeyStatus.Text = "不支持的按键：请使用 字母 / 数字 / 符号 / F1-F24 / Space / Enter / Tab";
            return;
        }

        var gesture = string.Empty;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            gesture += "Ctrl+";
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            gesture += "Alt+";
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            gesture += "Shift+";
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            gesture += "Win+";
        }

        HotkeyBox.Text = gesture + keyName;
        ApplyHotkey();
    }

    private void OnHotkeyGotFocus(object? sender, GotFocusEventArgs e)
    {
        _hotkeyBeforeRecording = _viewModel.Hotkey;
    }

    private void OnHotkeyTextInput(object? sender, TextInputEventArgs e)
    {
        // 录制模式：吞掉所有文本输入（含 IME 组合输入），防止字符混入热键框
        e.Handled = true;
    }

    private void OnHotkeyLostFocus(object? sender, RoutedEventArgs e)
    {
        _hotkeyBeforeRecording = null;
        ApplyHotkey();
    }

    private void ApplyHotkey()
    {
        var gesture = HotkeyBox.Text?.Trim() ?? string.Empty;
        if (gesture.Length == 0)
        {
            HotkeyBox.Text = _viewModel.Hotkey;
            HotkeyStatus.Text = "请按下热键组合";
            return;
        }

        if (gesture == _viewModel.Hotkey)
        {
            return;
        }

        if (!GlobalHotkey.IsValid(gesture))
        {
            HotkeyStatus.Text = "需至少一个修饰键（Ctrl/Alt/Shift/Win）加一个按键，如 Alt+Space";
            return;
        }

        if (_tryChangeHotkey(gesture))
        {
            _viewModel.Hotkey = gesture;
            HotkeyStatus.Text = $"已应用：{gesture}";
        }
        else
        {
            HotkeyBox.Text = _viewModel.Hotkey;
            HotkeyStatus.Text = "该热键已被占用，已恢复原热键";
        }
    }

    private void OnPinTaskbarClick(object? sender, RoutedEventArgs e)
    {
        ShortcutStatus.Text = ShortcutService.CreateTaskbarShortcut()
            ? "已创建任务栏快捷方式，若未立即显示请重启资源管理器"
            : "创建任务栏快捷方式失败";
    }

    private void OnCreateDesktopIconClick(object? sender, RoutedEventArgs e)
    {
        ShortcutStatus.Text = ShortcutService.CreateDesktopShortcut()
            ? "已在桌面创建快捷方式"
            : "创建桌面快捷方式失败";
    }

    private async void OnSetPasswordClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new PromptWindow("设置密码", "输入新的解锁密码", string.Empty, isPassword: true);
        var result = await dialog.ShowDialog<string?>(this);
        if (!string.IsNullOrEmpty(result))
        {
            _viewModel.SetPassword(result);
        }
    }

    private void OnClearPasswordClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.ClearPassword();
    }

    private void OnMaintenanceClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string tag }
            || !Enum.TryParse<MaintenanceAction>(tag, out var action))
        {
            return;
        }

        _viewModel.RunMaintenance(action);
    }
}
