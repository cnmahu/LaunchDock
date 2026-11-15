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
        if (e.Key != Key.Enter)
        {
            return;
        }

        ApplyHotkey();
        e.Handled = true;
    }

    private void OnHotkeyLostFocus(object? sender, RoutedEventArgs e) => ApplyHotkey();

    private void ApplyHotkey()
    {
        var gesture = HotkeyBox.Text?.Trim() ?? string.Empty;
        if (gesture.Length == 0)
        {
            HotkeyBox.Text = _viewModel.Hotkey;
            HotkeyStatus.Text = "请输入热键组合";
            return;
        }

        if (gesture == _viewModel.Hotkey)
        {
            return;
        }

        if (!GlobalHotkey.IsValid(gesture))
        {
            HotkeyStatus.Text = "格式不正确，示例：Alt+Space、Ctrl+Alt+K";
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
