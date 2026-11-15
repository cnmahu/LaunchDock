using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Templates;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using RolanClone.App.ViewModels;
using RolanClone.Core.Model;
using RolanClone.Core.Security;
using RolanClone.Core.Storage;
using RolanClone.Windows;
using RolanClone.Windows.Icons;
using RolanClone.Windows.Input;
using RolanClone.Windows.Launching;

namespace RolanClone.App.Views;

public partial class LauncherWindow : Window
{
    private const int SnapThreshold = 12;
    private const string ItemDragFormat = "rolanclone/item";

    private readonly AppConfig _config;
    private readonly LauncherViewModel _viewModel;
    private readonly DispatcherTimer _snapTimer;
    private GlobalHotkey? _hotkey;
    private GlobalMouseHook? _mouseHook;
    private SettingsWindow? _settingsWindow;
    private AppLibraryWindow? _libraryWindow;
    private ItemViewModel? _dragCandidate;
    private Point _dragStart;
    private bool _isLocked;
    private bool _suppressSnap;
    private bool _suppressHide;
    private bool _ctrlDown;

    public LauncherWindow() : this(ConfigStore.Load())
    {
    }

    public LauncherWindow(AppConfig config)
    {
        _config = config;
        InitializeComponent();

        _viewModel = new LauncherViewModel(config);
        DataContext = _viewModel;
        _viewModel.ApplySettings(config.Settings);
        ApplyLayout();
        ApplyMaterial();

        Width = Math.Max(MinWidth, config.Window.Width);
        Height = Math.Max(MinHeight, config.Window.Height);
        Topmost = config.Settings.TopMost;

        if (config.Window.Left is { } left && config.Window.Top is { } top)
        {
            Position = new PixelPoint(left, top);
        }

        _snapTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _snapTimer.Tick += OnSnapTimerTick;

        Opened += OnOpened;
        PositionChanged += OnPositionChanged;
        Deactivated += OnDeactivated;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle != IntPtr.Zero)
        {
            WindowFrame.Apply(handle, darkMode: false);
        }

        if (_config.Window.Left is null || _config.Window.Top is null)
        {
            CenterOnPrimaryScreen();
        }

        UpdateInputHooks();

        if (!string.IsNullOrWhiteSpace(_config.Settings.LockPasswordHash) && _config.Settings.AutoLock)
        {
            _isLocked = true;
        }

        UpdateLockOverlay();

        if (_config.Settings.StartHidden && _hotkey?.IsRegistered == true)
        {
            Hide();
        }
    }

    private void ApplyMaterial()
    {
        var settings = _config.Settings;
        var requested = Application.Current?.RequestedThemeVariant;
        var dark = requested == ThemeVariant.Dark
                   || (requested != ThemeVariant.Light && ActualThemeVariant == ThemeVariant.Dark);
        var solid = settings.Material == WindowMaterial.Solid;

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

        var baseAlpha = solid ? 1.0 : dark ? 0.88 : 0.72;
        var alpha = (byte)Math.Clamp((int)Math.Round(255 * baseAlpha * settings.Opacity), 40, 255);
        var baseColor = dark ? Color.FromRgb(24, 24, 24) : Color.FromRgb(255, 255, 255);
        Background = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
    }

    public void ApplySettings()
    {
        Topmost = _config.Settings.TopMost;
        _viewModel.ApplySettings(_config.Settings);
        ApplyLayout();
        ApplyMaterial();
        _libraryWindow?.ApplyWindowMaterial();

        // 便签目录可能被修改，打开时重新加载
        if (_viewModel.Notes.IsOpen)
        {
            _viewModel.Notes.Reload();
        }

        UpdateInputHooks();
        SaveConfig();
    }

    private void ApplyLayout()
    {
        var isList = _config.Settings.Layout == PanelLayout.Vertical;

        if (Resources[isList ? "ListItemsPanel" : "GridItemsPanel"] is ItemsPanelTemplate panel)
        {
            ItemList.ItemsPanel = panel;
        }

        if (Resources[isList ? "ListItemTemplate" : "GridItemTemplate"] is DataTemplate template)
        {
            ItemList.ItemTemplate = template;
        }
    }

    public void OpenSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _suppressHide = true;
        var window = new SettingsWindow(_config.Settings, ApplySettings, TryChangeHotkey, RunMaintenance);
        _settingsWindow = window;
        window.Closed += (_, _) =>
        {
            _settingsWindow = null;
            _suppressHide = false;
            ApplySettings();
        };
        window.Show(this);
    }

    public bool TryChangeHotkey(string gesture)
    {
        if (!GlobalHotkey.IsValid(gesture))
        {
            return false;
        }

        if (!_config.Settings.EnableHotkey)
        {
            _config.Settings.Hotkey = gesture;
            SaveConfig();
            return true;
        }

        var previous = _config.Settings.Hotkey;
        _hotkey?.Dispose();
        _hotkey = null;

        var candidate = TryRegisterHotkey(gesture);
        if (candidate is not null)
        {
            _hotkey = candidate;
            _config.Settings.Hotkey = gesture;
            SaveConfig();
            return true;
        }

        _hotkey = TryRegisterHotkey(previous);
        return false;
    }

    /// <summary>按设置启用/停用全局键盘快捷键与鼠标扩展键。</summary>
    private void UpdateInputHooks()
    {
        var settings = _config.Settings;

        if (!settings.EnableHotkey)
        {
            _hotkey?.Dispose();
            _hotkey = null;
        }
        else if (_hotkey is null)
        {
            _hotkey = TryRegisterHotkey(settings.Hotkey);
        }

        var needMouse = settings.ShowOnXButton1 || settings.ShowOnXButton2 || settings.ShowOnMiddleClick;
        if (!needMouse)
        {
            _mouseHook?.Dispose();
            _mouseHook = null;
            return;
        }

        if (_mouseHook is not null)
        {
            return;
        }

        var hook = new GlobalMouseHook();
        hook.XButton1Pressed += () => OnMouseShowRequested(_config.Settings.ShowOnXButton1);
        hook.XButton2Pressed += () => OnMouseShowRequested(_config.Settings.ShowOnXButton2);
        hook.MiddlePressed += () => OnMouseShowRequested(_config.Settings.ShowOnMiddleClick);
        _mouseHook = hook;
    }

    private void OnMouseShowRequested(bool enabled)
    {
        if (!enabled)
        {
            return;
        }

        Dispatcher.UIThread.Post(ShowPanel);
    }

    private string RunMaintenance(MaintenanceAction action)
    {
        switch (action)
        {
            case MaintenanceAction.RepairIcons:
            {
                IconService.ClearCache();
                var count = _viewModel.RefreshIcons();
                _libraryWindow?.RefreshIcons();
                return count == 0 ? "没有可修复的图标" : $"已重建 {count} 个图标";
            }
            case MaintenanceAction.SortByName:
            {
                var count = _viewModel.SortAllByName();
                SaveConfig();
                return count == 0 ? "没有可排序的条目" : $"已按名称排序 {count} 个条目";
            }
            case MaintenanceAction.RemoveDuplicates:
            {
                var count = _viewModel.RemoveDuplicates();
                SaveConfig();
                return count == 0 ? "没有发现重复项" : $"已清理 {count} 个重复项";
            }
            case MaintenanceAction.RemoveInvalid:
            {
                var count = _viewModel.RemoveInvalidItems();
                SaveConfig();
                return count == 0 ? "没有发现失效项" : $"已清理 {count} 个失效项";
            }
            case MaintenanceAction.RemoveEmptyTabs:
            {
                var count = _viewModel.RemoveEmptyTabs();
                SaveConfig();
                return count == 0 ? "没有空标签" : $"已清理 {count} 个空标签";
            }
            default:
                return "未知操作";
        }
    }

    private GlobalHotkey? TryRegisterHotkey(string gesture)
    {
        try
        {
            var hotkey = new GlobalHotkey(gesture);
            if (!hotkey.IsRegistered)
            {
                hotkey.Dispose();
                return null;
            }

            hotkey.Pressed += OnGlobalHotkeyPressed;
            return hotkey;
        }
        catch
        {
            return null;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _hotkey?.Dispose();
        _hotkey = null;
        _mouseHook?.Dispose();
        _mouseHook = null;
        _viewModel.Dispose();
    }

    private void CenterOnPrimaryScreen()
    {
        var screen = Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        var scale = RenderScaling;
        var width = (int)Math.Round(Width * scale);
        var height = (int)Math.Round(Height * scale);
        Position = new PixelPoint(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2);
    }

    private void OnPositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (_suppressSnap)
        {
            return;
        }

        _snapTimer.Stop();
        _snapTimer.Start();
    }

    private void OnSnapTimerTick(object? sender, EventArgs e)
    {
        _snapTimer.Stop();
        if (!_config.Settings.EdgeSnap || _config.Settings.LockPosition)
        {
            return;
        }

        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        var scale = RenderScaling;
        var width = (int)Math.Round(Width * scale);
        var height = (int)Math.Round(Height * scale);
        var current = Position;

        var x = current.X;
        var y = current.Y;

        if (Math.Abs(current.X - area.X) <= SnapThreshold)
        {
            x = area.X;
        }
        else if (Math.Abs(area.Right - (current.X + width)) <= SnapThreshold)
        {
            x = area.Right - width;
        }

        if (Math.Abs(current.Y - area.Y) <= SnapThreshold)
        {
            y = area.Y;
        }
        else if (Math.Abs(area.Bottom - (current.Y + height)) <= SnapThreshold)
        {
            y = area.Bottom - height;
        }

        if (x == current.X && y == current.Y)
        {
            return;
        }

        _suppressSnap = true;
        Position = new PixelPoint(x, y);
        _suppressSnap = false;
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key is Key.LeftCtrl or Key.RightCtrl)
        {
            _ctrlDown = true;
        }

        if (_isLocked)
        {
            return;
        }

        // Alt+Space 是唤出热键，避免空格被当前焦点按钮当成点击
        if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            BeginSearch();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (_viewModel.Notes.IsOpen)
            {
                _viewModel.Notes.Close();
            }
            else if (_viewModel.IsSearchActive)
            {
                EndSearch();
            }
            else if (_config.Settings.CloseOnEsc)
            {
                HidePanel();
            }

            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (e.Key is Key.LeftCtrl or Key.RightCtrl)
        {
            _ctrlDown = false;
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);

        if (_isLocked
            || _viewModel.IsSearchActive
            || string.IsNullOrEmpty(e.Text)
            || e.Text.Length != 1
            || char.IsControl(e.Text[0])
            || FocusManager?.GetFocusedElement() is TextBox)
        {
            return;
        }

        BeginSearch();
        _viewModel.SearchText = e.Text;
        SearchBox.CaretIndex = 1;
        e.Handled = true;
    }

    private void BeginSearch()
    {
        if (_viewModel.Notes.IsOpen)
        {
            _viewModel.Notes.Close();
        }

        _viewModel.BeginSearch();
        Dispatcher.UIThread.Post(() =>
        {
            SearchBox.Focus();
            SearchBox.CaretIndex = SearchBox.Text?.Length ?? 0;
        });
    }

    private void EndSearch()
    {
        _viewModel.EndSearch();
        Focus();
    }

    private void OnSearchClick(object? sender, RoutedEventArgs e) => BeginSearch();

    private void OnCloseSearchClick(object? sender, RoutedEventArgs e) => EndSearch();

    private void OnTabClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.Notes.IsOpen)
        {
            _viewModel.Notes.Close();
        }

        if (sender is Control { DataContext: TabViewModel tab })
        {
            _viewModel.Select(tab);
        }
    }

    private void OnNotesClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.Notes.Toggle();

        if (_viewModel.Notes.IsOpen)
        {
            if (_viewModel.IsSearchActive)
            {
                EndSearch();
            }

            Dispatcher.UIThread.Post(() => NoteTextBox.Focus(), DispatcherPriority.Input);
        }
    }

    private void OnNewNoteClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.Notes.AddNote();
        Dispatcher.UIThread.Post(() => NoteTextBox.Focus(), DispatcherPriority.Input);
    }

    private void OnAddTabClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.AddTab();
        SaveConfig();
    }

    private async void OnRenameTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: TabViewModel tab })
        {
            return;
        }

        _suppressHide = true;
        try
        {
            var dialog = new PromptWindow("重命名分组", "输入新的分组名称", tab.Name);
            var result = await dialog.ShowDialog<string?>(this);
            if (!string.IsNullOrWhiteSpace(result))
            {
                _viewModel.RenameTab(tab, result);
                SaveConfig();
            }
        }
        finally
        {
            _suppressHide = false;
        }
    }

    private void OnDeleteTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: TabViewModel tab })
        {
            _viewModel.RemoveTab(tab);
            SaveConfig();
        }
    }

    private async void OnNewLiveFolderTabClick(object? sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync();
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        _viewModel.AddLiveFolderTab(folder);
        SaveConfig();
    }

    private void OnRefreshLiveFolderClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: TabViewModel tab } && tab.IsLiveFolder)
        {
            _viewModel.RefreshLiveFolder(tab);
        }
    }

    private async void OnRebindLiveFolderClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: TabViewModel tab } || !tab.IsLiveFolder)
        {
            return;
        }

        var folder = await PickFolderAsync();
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        _viewModel.RebindLiveFolder(tab, folder);
        SaveConfig();
    }

    private void OnOpenLiveFolderSourceClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: TabViewModel tab })
        {
            return;
        }

        var path = tab.SourcePath;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // 打开目录失败时忽略
        }
    }

    private async Task<string?> PickFolderAsync()
    {
        var storage = StorageProvider;
        if (storage is null)
        {
            return null;
        }

        _suppressHide = true;
        try
        {
            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择文件夹",
                AllowMultiple = false
            });

            return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        }
        finally
        {
            _suppressHide = false;
        }
    }

    private void OnItemTapped(object? sender, TappedEventArgs e)
    {
        if (!_config.Settings.SingleClickRun)
        {
            return;
        }

        if (sender is Control { DataContext: ItemViewModel item })
        {
            LaunchItem(item, forceAdmin: false);
            e.Handled = true;
        }
    }

    private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_config.Settings.SingleClickRun)
        {
            return;
        }

        if (sender is Control { DataContext: ItemViewModel item })
        {
            LaunchItem(item, forceAdmin: false);
            e.Handled = true;
        }
    }

    private void OnItemOpenClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: ItemViewModel item })
        {
            LaunchItem(item, forceAdmin: false);
        }
    }

    private void OnItemRunAsAdminClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: ItemViewModel item })
        {
            LaunchItem(item, forceAdmin: true);
        }
    }

    private void LaunchItem(ItemViewModel item, bool forceAdmin)
    {
        if (!AppLauncher.Launch(item.Model, forceAdmin))
        {
            return;
        }

        item.Model.UseCount++;
        item.Model.LastUsed = DateTime.Now;
        SaveConfig();

        if (_config.Settings.HideAfterLaunch)
        {
            Dispatcher.UIThread.Post(HidePanel, DispatcherPriority.Background);
        }
    }

    private void OnItemOpenLocationClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: ItemViewModel item })
        {
            return;
        }

        var target = item.Target;
        if (item.Kind is ItemKind.Url or ItemKind.Uwp or ItemKind.Builtin || string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        try
        {
            var arguments = Directory.Exists(target) ? $"\"{target}\"" : $"/select,\"{target}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
        }
        catch
        {
            // 打开位置失败时忽略
        }
    }

    private async void OnItemCopyPathClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: ItemViewModel item })
        {
            return;
        }

        if (Clipboard is not null)
        {
            await Clipboard.SetTextAsync(item.Target);
        }
    }

    private async void OnItemRenameClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: ItemViewModel item })
        {
            return;
        }

        _suppressHide = true;
        try
        {
            var dialog = new PromptWindow("重命名项目", "输入新的显示名称", item.Name);
            var result = await dialog.ShowDialog<string?>(this);
            if (!string.IsNullOrWhiteSpace(result))
            {
                _viewModel.RenameItem(item, result);
                SaveConfig();
            }
        }
        finally
        {
            _suppressHide = false;
        }
    }

    private void OnItemDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: ItemViewModel item })
        {
            _viewModel.RemoveItem(item);
            SaveConfig();
        }
    }

    private void OnTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: ItemViewModel item })
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed && item.CanEdit)
        {
            _dragCandidate = item;
            _dragStart = point.Position;
        }
    }

    private async void OnTilePointerMoved(object? sender, PointerEventArgs e)
    {
#pragma warning disable CS0618 // 自定义拖拽格式仍走 DataObject / DoDragDrop API
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

        var data = new DataObject();
        data.Set(ItemDragFormat, item.Model.Id);

        try
        {
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
        }
        catch
        {
            // 拖拽被取消时忽略
        }
#pragma warning restore CS0618
    }

    private void OnTileDragOver(object? sender, DragEventArgs e)
    {
#pragma warning disable CS0618
        var hasItem = e.Data.Contains(ItemDragFormat);
#pragma warning restore CS0618
        if (!hasItem)
        {
            return;
        }

        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnTileDrop(object? sender, DragEventArgs e)
    {
#pragma warning disable CS0618
        if (!e.Data.Contains(ItemDragFormat))
        {
            return;
        }

        var sourceId = e.Data.Get(ItemDragFormat) as string;
#pragma warning restore CS0618

        if (sourceId is not null && sender is Control { DataContext: ItemViewModel target })
        {
            _viewModel.MoveItem(sourceId, target);
            SaveConfig();
            e.Handled = true;
        }
    }

    private void OnAddAppClick(object? sender, RoutedEventArgs e) => OpenAppLibrary();

    private void OpenAppLibrary()
    {
        if (_libraryWindow is { IsVisible: true })
        {
            _libraryWindow.Activate();
            return;
        }

        _suppressHide = true;

        var library = new AppLibraryWindow(_config);
        library.ItemActivated += model =>
        {
            _viewModel.AddItem(model);
            SaveConfig();
        };
        library.Closed += (_, _) =>
        {
            _libraryWindow = null;
            _suppressHide = false;
        };

        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        var area = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
        var x = Math.Clamp(Position.X + 60, area.X, Math.Max(area.X, area.Right - 720));
        var y = Math.Clamp(Position.Y + 60, area.Y, Math.Max(area.Y, area.Bottom - 580));
        library.Position = new PixelPoint(x, y);

        _libraryWindow = library;
        library.Show(this);
    }

    private async void OnAddShortcutClick(object? sender, RoutedEventArgs e)
    {
        var files = await PickFilesAsync("选择快捷方式", allowMultiple: true,
            new FilePickerFileType("快捷方式") { Patterns = new[] { "*.lnk", "*.url" } });
        AddPaths(files);
    }

    private async void OnAddLinkClick(object? sender, RoutedEventArgs e)
    {
        _suppressHide = true;
        try
        {
            var dialog = new PromptWindow("添加链接", "输入网址，例如 https://example.com", "https://");
            var result = await dialog.ShowDialog<string?>(this);
            if (string.IsNullOrWhiteSpace(result))
            {
                return;
            }

            var url = result.Trim();
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            _viewModel.AddItem(new ItemModel
            {
                DisplayName = url,
                Kind = ItemKind.Url,
                Target = url
            });
            SaveConfig();
        }
        finally
        {
            _suppressHide = false;
        }
    }

    private async Task<IReadOnlyList<string>> PickFilesAsync(
        string title, bool allowMultiple, params FilePickerFileType[] filters)
    {
        var storage = StorageProvider;
        if (storage is null)
        {
            return Array.Empty<string>();
        }

        _suppressHide = true;
        try
        {
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = allowMultiple,
                FileTypeFilter = filters
            });

            return files
                .Select(file => file.TryGetLocalPath())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>()
                .ToList();
        }
        finally
        {
            _suppressHide = false;
        }
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        var added = false;
        foreach (var path in paths)
        {
            _viewModel.AddItem(CreateItemFromPath(path));
            added = true;
        }

        if (added)
        {
            SaveConfig();
        }
    }

    private async void OnNewItemClick(object? sender, RoutedEventArgs e)
    {
        _suppressHide = true;
        try
        {
            var dialog = new NewItemWindow();
            var result = await dialog.ShowDialog<ItemModel?>(this);
            if (result is not null)
            {
                _viewModel.AddItem(result);
                SaveConfig();
            }
        }
        finally
        {
            _suppressHide = false;
        }
    }

    private void OnEmptyDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_config.Settings.HideOnEmptyDoubleClick)
        {
            HidePanel();
            e.Handled = true;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
#pragma warning disable CS0618 // Avalonia 11.3 仍在过渡到 DataTransfer API
        var acceptsFiles = e.Data.Contains(DataFormats.Files);
        var acceptsText = e.Data.Contains(DataFormats.Text);
        var acceptsLibrary = e.Data.Contains(AppLibraryDrag.Format);
#pragma warning restore CS0618
        e.DragEffects = acceptsFiles || acceptsText || acceptsLibrary
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
#pragma warning disable CS0618
        if (e.Data.Contains(AppLibraryDrag.Format))
        {
            var model = AppLibraryDrag.Deserialize(e.Data.Get(AppLibraryDrag.Format) as string);
            if (model is not null)
            {
                _viewModel.AddItem(model);
                SaveConfig();
                e.Handled = true;
            }

            return;
        }

        var paths = e.Data.GetFiles()?
                        .Select(file => file.TryGetLocalPath())
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .Cast<string>()
                        .ToList()
                    ?? new List<string>();

        if (paths.Count == 0)
        {
            var text = e.Data.GetText();
#pragma warning restore CS0618
            if (!string.IsNullOrWhiteSpace(text)
                && (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                _viewModel.AddItem(new ItemModel
                {
                    DisplayName = text,
                    Kind = ItemKind.Url,
                    Target = text
                });
                SaveConfig();
            }

            return;
        }

        foreach (var path in paths)
        {
            _viewModel.AddItem(CreateItemFromPath(path));
        }

        SaveConfig();
        e.Handled = true;
    }

    private static ItemModel CreateItemFromPath(string path)
    {
        var isDirectory = Directory.Exists(path);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var kind = isDirectory ? ItemKind.Folder
            : extension is ".url" or ".website" ? ItemKind.Url
            : ItemKind.Exe;

        var name = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = path;
        }

        return new ItemModel
        {
            DisplayName = name,
            Kind = kind,
            Target = path
        };
    }

    private void OnGlobalHotkeyPressed()
    {
        Dispatcher.UIThread.Post(ToggleVisibility);
    }

    private void ToggleVisibility()
    {
        if (IsVisible && IsActive)
        {
            HidePanel();
            return;
        }

        ShowPanel();
    }

    public void ShowPanel()
    {
        ApplyShowPosition();
        Show();
        Activate();
        UpdateLockOverlay();
        FocusLockBox();

        // 热键唤出后焦点回到窗口本身，避免空格键误触发上次点击的按钮
        if (!_isLocked)
        {
            Focus();
        }
    }

    private void ApplyShowPosition()
    {
        switch (_config.Settings.ShowPosition)
        {
            case ShowPosition.Center:
                CenterOnPrimaryScreen();
                break;
            case ShowPosition.Mouse:
                CenterOnCursor();
                break;
        }
    }

    private void CenterOnCursor()
    {
        if (!GetCursorPos(out var cursor))
        {
            return;
        }

        var screen = Screens.ScreenFromPoint(new PixelPoint(cursor.X, cursor.Y)) ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        var scale = RenderScaling;
        var width = (int)Math.Round(Width * scale);
        var height = (int)Math.Round(Height * scale);

        var x = Math.Clamp(cursor.X - width / 2, area.X, Math.Max(area.X, area.Right - width));
        var y = Math.Clamp(cursor.Y - 24, area.Y, Math.Max(area.Y, area.Bottom - height));
        Position = new PixelPoint(x, y);
    }

    private void HidePanel()
    {
        _viewModel.Notes.SaveNow();

        if (_config.Settings.AutoLock && !string.IsNullOrWhiteSpace(_config.Settings.LockPasswordHash))
        {
            _isLocked = true;
            LockPasswordBox.Text = string.Empty;
            LockErrorText.Text = string.Empty;
        }

        Hide();
    }

    private void UpdateLockOverlay()
    {
        LockOverlay.IsVisible = _isLocked;
        if (!_isLocked)
        {
            return;
        }

        LockPasswordBox.Text = string.Empty;
        LockErrorText.Text = string.Empty;
    }

    private void FocusLockBox()
    {
        if (!_isLocked)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_isLocked)
            {
                LockPasswordBox.Focus();
            }
        }, DispatcherPriority.Input);
    }

    private void OnUnlockClick(object? sender, RoutedEventArgs e) => TryUnlock();

    private void OnLockPasswordKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TryUnlock();
            e.Handled = true;
        }
    }

    private void TryUnlock()
    {
        if (PasswordHasher.Verify(LockPasswordBox.Text ?? string.Empty, _config.Settings.LockPasswordHash))
        {
            _isLocked = false;
            LockErrorText.Text = string.Empty;
            UpdateLockOverlay();
            Focus();
            return;
        }

        LockErrorText.Text = "密码不正确";
        LockPasswordBox.SelectAll();
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e) => OpenSettings();

    private void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        _suppressHide = true;

        var about = new AboutWindow();
        about.Closed += (_, _) => _suppressHide = false;
        about.Show(this);
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // 没有可用的全局热键时不自动隐藏，否则用户无法再把面板叫回来。
        if (!_config.Settings.HideOnLostFocus || _hotkey?.IsRegistered != true)
        {
            return;
        }

        if (_config.Settings.CtrlPreventHide && _ctrlDown)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (!_suppressHide && !IsActive)
            {
                HidePanel();
            }
        }, DispatcherPriority.Background);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();

    private void SaveConfig()
    {
        try
        {
            ConfigStore.Save(_config);
        }
        catch
        {
            // 配置写入失败不影响交互
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        _viewModel.Notes.SaveNow();
        _config.Window.Left = Position.X;
        _config.Window.Top = Position.Y;
        _config.Window.Width = Width;
        _config.Window.Height = Height;
        _config.Window.SelectedTabId = _viewModel.SelectedTab?.Id;

        SaveConfig();
    }
}
