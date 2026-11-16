using System.Collections.ObjectModel;
using Avalonia.Threading;
using LaunchDock.Core.Model;
using LaunchDock.Core.Storage;
using LaunchDock.Windows.Interop;

namespace LaunchDock.App.ViewModels;

public sealed class LauncherViewModel : ViewModelBase, IDisposable
{
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new();
    private readonly Dictionary<string, CancellationTokenSource> _refreshTokens = new();
    private TabViewModel? _selectedTab;
    private string _searchText = string.Empty;
    private bool _isSearchActive;

    public LauncherViewModel(AppConfig config)
    {
        Config = config;
        Notes = new NotesViewModel(() => NotesStore.Resolve(Config.Settings.NotesDirectory));

        for (var i = 0; i < config.Tabs.Count; i++)
        {
            Tabs.Add(new TabViewModel(config.Tabs[i], i));
        }

        Tabs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasTabs));

        foreach (var tab in Tabs.Where(tab => tab.IsLiveFolder))
        {
            RefreshLiveFolder(tab);
            StartWatcher(tab);
        }

        SelectedTab = Tabs.FirstOrDefault(tab => tab.Id == config.Window.SelectedTabId)
                      ?? Tabs.FirstOrDefault();
    }

    public AppConfig Config { get; }
    public NotesViewModel Notes { get; }
    public ObservableCollection<TabViewModel> Tabs { get; } = new();

    /// <summary>是否已有分组；首次启动时没有任何分组，界面显示空状态提示。</summary>
    public bool HasTabs => Tabs.Count > 0;

    /// <summary>网格实际展示的条目：普通状态下是当前分组，搜索状态下是跨分组结果。</summary>
    public ObservableCollection<ItemViewModel> VisibleItems { get; } = new();

    public TabViewModel? SelectedTab
    {
        get => _selectedTab;
        private set
        {
            if (!SetProperty(ref _selectedTab, value))
            {
                return;
            }

            foreach (var tab in Tabs)
            {
                tab.IsSelected = ReferenceEquals(tab, value);
            }

            UpdateVisibleItems();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                UpdateVisibleItems();
            }
        }
    }

    public bool IsSearchActive
    {
        get => _isSearchActive;
        set
        {
            if (SetProperty(ref _isSearchActive, value))
            {
                UpdateVisibleItems();
            }
        }
    }

    public void Select(TabViewModel tab) => SelectedTab = tab;

    public TabViewModel AddTab()
    {
        var model = new TabModel { Name = "新分组", Order = Tabs.Count };
        Config.Tabs.Add(model);

        var tab = new TabViewModel(model, Tabs.Count);
        Tabs.Add(tab);
        SelectedTab = tab;
        return tab;
    }

    public TabViewModel AddLiveFolderTab(string folderPath)
    {
        var name = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(name))
        {
            name = folderPath;
        }

        var model = new TabModel
        {
            Name = name,
            Kind = TabKind.LiveFolder,
            SourcePath = folderPath,
            Order = Tabs.Count
        };
        Config.Tabs.Add(model);

        var tab = new TabViewModel(model, Tabs.Count);
        Tabs.Add(tab);
        SelectedTab = tab;

        RefreshLiveFolder(tab);
        StartWatcher(tab);
        return tab;
    }

    public void RebindLiveFolder(TabViewModel tab, string folderPath)
    {
        if (!tab.IsLiveFolder)
        {
            return;
        }

        StopWatcher(tab.Id);
        tab.Model.SourcePath = folderPath;
        RefreshLiveFolder(tab);
        StartWatcher(tab);
    }

    /// <summary>把拖入的文件/文件夹复制进实时文件夹的源目录，重名时自动加序号。</summary>
    public async Task<int> CopyIntoLiveFolderAsync(TabViewModel tab, IReadOnlyList<string> paths)
    {
        if (!tab.IsLiveFolder)
        {
            return 0;
        }

        var destination = tab.Model.SourcePath;
        if (string.IsNullOrWhiteSpace(destination) || !Directory.Exists(destination))
        {
            return 0;
        }

        var copied = await Task.Run(() =>
        {
            var count = 0;
            foreach (var path in paths)
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        // 源目录与目标目录互相包含时跳过，避免无限递归复制
                        if (IsNested(path, destination))
                        {
                            continue;
                        }

                        CopyDirectory(path, MakeUniquePath(destination, Path.GetFileName(path), isDirectory: true));
                        count++;
                    }
                    else if (File.Exists(path))
                    {
                        if (IsNested(path, destination))
                        {
                            continue;
                        }

                        File.Copy(path, MakeUniquePath(destination, Path.GetFileName(path), isDirectory: false), overwrite: false);
                        count++;
                    }
                }
                catch
                {
                    // 单个条目复制失败不影响其它条目
                }
            }

            return count;
        });

        RefreshLiveFolder(tab);
        return copied;
    }

    /// <summary>重命名实时文件夹里的条目，直接作用于源目录中的文件。</summary>
    public bool RenameLiveItem(ItemViewModel item, string name)
    {
        var tab = FindLiveTabOf(item);
        if (tab is null)
        {
            return false;
        }

        var newName = name.Trim();
        var source = item.Model.Target;
        var parent = Path.GetDirectoryName(source);
        if (newName.Length == 0
            || newName == item.Name
            || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || string.IsNullOrWhiteSpace(parent))
        {
            return false;
        }

        var destination = Path.Combine(parent, newName);
        if (PathExists(destination))
        {
            return false;
        }

        try
        {
            if (Directory.Exists(source))
            {
                Directory.Move(source, destination);
            }
            else if (File.Exists(source))
            {
                File.Move(source, destination);
            }
            else
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        RefreshLiveFolder(tab);
        return true;
    }

    /// <summary>把实时文件夹里的条目移入回收站，并刷新分组。</summary>
    public bool DeleteLiveItem(ItemViewModel item)
    {
        var tab = FindLiveTabOf(item);
        if (tab is null)
        {
            return false;
        }

        if (!RecycleBin.Delete(item.Model.Target))
        {
            return false;
        }

        RefreshLiveFolder(tab);
        return true;
    }

    private TabViewModel? FindLiveTabOf(ItemViewModel item) =>
        Tabs.FirstOrDefault(tab => tab.IsLiveFolder && tab.Items.Contains(item));

    private static bool IsNested(string path, string folder)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullFolder = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return fullPath.Equals(fullFolder, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(fullFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || fullFolder.StartsWith(fullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string MakeUniquePath(string directory, string name, bool isDirectory)
    {
        var target = Path.Combine(directory, name);
        if (!PathExists(target))
        {
            return target;
        }

        var stem = isDirectory ? name : Path.GetFileNameWithoutExtension(name);
        var extension = isDirectory ? string.Empty : Path.GetExtension(name);

        for (var i = 2; ; i++)
        {
            target = Path.Combine(directory, $"{stem} ({i}){extension}");
            if (!PathExists(target))
            {
                return target;
            }
        }
    }

    private static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    public void RefreshLiveFolder(TabViewModel tab)
    {
        if (!tab.IsLiveFolder)
        {
            return;
        }

        tab.Items.Clear();

        var path = tab.Model.SourcePath;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            if (ReferenceEquals(SelectedTab, tab))
            {
                UpdateVisibleItems();
            }

            return;
        }

        List<FileSystemInfo> entries;
        try
        {
            entries = new DirectoryInfo(path).EnumerateFileSystemInfos()
                .OrderByDescending(entry => entry is DirectoryInfo)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .Take(200)
                .ToList();
        }
        catch
        {
            if (ReferenceEquals(SelectedTab, tab))
            {
                UpdateVisibleItems();
            }

            return;
        }

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var kind = entry is DirectoryInfo
                ? ItemKind.Folder
                : string.Equals(entry.Extension, ".url", StringComparison.OrdinalIgnoreCase)
                    ? ItemKind.Url
                    : ItemKind.File;

            var item = new ItemViewModel(new ItemModel
            {
                DisplayName = entry.Name,
                Kind = kind,
                Target = entry.FullName,
                Order = i
            }, i, isLive: true);

            ApplyAppearance(item);
            tab.Items.Add(item);
        }

        if (ReferenceEquals(SelectedTab, tab))
        {
            UpdateVisibleItems();
        }
    }

    private void StartWatcher(TabViewModel tab)
    {
        StopWatcher(tab.Id);

        var path = tab.Model.SourcePath;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName
                               | NotifyFilters.DirectoryName
                               | NotifyFilters.LastWrite
                               | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            watcher.Created += (_, _) => ScheduleRefresh(tab);
            watcher.Deleted += (_, _) => ScheduleRefresh(tab);
            watcher.Changed += (_, _) => ScheduleRefresh(tab);
            watcher.Renamed += (_, _) => ScheduleRefresh(tab);

            _watchers[tab.Id] = watcher;
        }
        catch
        {
            // 目录被占用或权限不足时退化为手动刷新
        }
    }

    private void StopWatcher(string tabId)
    {
        if (_watchers.Remove(tabId, out var watcher))
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        if (_refreshTokens.Remove(tabId, out var token))
        {
            token.Cancel();
            token.Dispose();
        }
    }

    private void ScheduleRefresh(TabViewModel tab)
    {
        if (_refreshTokens.Remove(tab.Id, out var previous))
        {
            previous.Cancel();
            previous.Dispose();
        }

        var tokenSource = new CancellationTokenSource();
        _refreshTokens[tab.Id] = tokenSource;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400, tokenSource.Token);
            }
            catch
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_refreshTokens.TryGetValue(tab.Id, out var current) && ReferenceEquals(current, tokenSource))
                {
                    RefreshLiveFolder(tab);
                }
            });
        });
    }

    public void Dispose()
    {
        foreach (var tabId in _watchers.Keys.ToList())
        {
            StopWatcher(tabId);
        }

        _watchers.Clear();
        _refreshTokens.Clear();
    }

    public void RemoveTab(TabViewModel tab)
    {
        if (Tabs.Count <= 1)
        {
            return;
        }

        StopWatcher(tab.Id);

        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        Config.Tabs.Remove(tab.Model);

        if (ReferenceEquals(SelectedTab, tab))
        {
            SelectedTab = Tabs[Math.Clamp(index - 1, 0, Tabs.Count - 1)];
        }
    }

    public void RenameTab(TabViewModel tab, string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length > 0)
        {
            tab.Name = trimmed;
        }
    }

    public void AddItem(ItemModel model)
    {
        if (SelectedTab is null)
        {
            AddTab();
        }

        if (SelectedTab is null || SelectedTab.Model.Kind != TabKind.Normal)
        {
            return;
        }

        model.Order = SelectedTab.Model.Items.Count;
        SelectedTab.Model.Items.Add(model);
        SelectedTab.Items.Add(new ItemViewModel(model, SelectedTab.Items.Count));
        UpdateVisibleItems();
    }

    public void BeginSearch()
    {
        IsSearchActive = true;
        UpdateVisibleItems();
    }

    public void EndSearch()
    {
        SearchText = string.Empty;
        IsSearchActive = false;
    }

    public void ApplySettings(SettingsModel settings)
    {
        foreach (var item in Tabs.SelectMany(tab => tab.Items))
        {
            ApplyAppearance(item);
        }

        UpdateVisibleItems();
    }

    /// <summary>使用次数变化后刷新排序。</summary>
    public void RefreshItemsOrder() => UpdateVisibleItems();

    private void ApplyAppearance(ItemViewModel item)
    {
        var settings = Config.Settings;

        if (settings.Layout == PanelLayout.Vertical)
        {
            item.ApplyAppearance(28, true, true, 0, isList: true);
            return;
        }

        item.ApplyAppearance(
            settings.IconSizePx,
            settings.LabelPosition != LabelPosition.None,
            settings.LabelPosition == LabelPosition.Right,
            settings.IconSpacing);
    }

    public void RemoveItem(ItemViewModel item)
    {
        foreach (var tab in Tabs)
        {
            if (!tab.Items.Remove(item))
            {
                continue;
            }

            tab.Model.Items.Remove(item.Model);
            for (var i = 0; i < tab.Model.Items.Count; i++)
            {
                tab.Model.Items[i].Order = i;
            }

            break;
        }

        UpdateVisibleItems();
    }

    public void RenameItem(ItemViewModel item, string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        item.Name = trimmed;
        UpdateVisibleItems();
    }

    /// <summary>把条目移动到同组的另一个条目之前（拖拽排序）。</summary>
    public void MoveItem(string sourceId, ItemViewModel target)
    {
        var tab = Tabs.FirstOrDefault(t => t.Items.Any(item => item.Id == sourceId));
        if (tab is null || tab.Model.Kind != TabKind.Normal)
        {
            return;
        }

        var source = tab.Items.FirstOrDefault(item => item.Id == sourceId);
        var targetIndex = tab.Items.IndexOf(target);
        if (source is null || targetIndex < 0 || ReferenceEquals(source, target))
        {
            return;
        }

        var sourceIndex = tab.Items.IndexOf(source);
        tab.Items.RemoveAt(sourceIndex);
        tab.Model.Items.Remove(source.Model);

        if (sourceIndex < targetIndex)
        {
            targetIndex--;
        }

        targetIndex = Math.Clamp(targetIndex, 0, tab.Items.Count);
        tab.Items.Insert(targetIndex, source);
        tab.Model.Items.Insert(targetIndex, source.Model);

        for (var i = 0; i < tab.Model.Items.Count; i++)
        {
            tab.Model.Items[i].Order = i;
        }

        UpdateVisibleItems();
    }

    public int RefreshIcons()
    {
        var count = 0;
        foreach (var item in Tabs.SelectMany(tab => tab.Items))
        {
            item.RefreshIcon();
            count++;
        }

        return count;
    }

    public int SortAllByName()
    {
        var count = 0;
        foreach (var tab in Tabs.Where(tab => tab.Model.Kind == TabKind.Normal))
        {
            var ordered = tab.Items
                .OrderBy(item => item.Name, StringComparer.CurrentCulture)
                .ToList();

            tab.Items.Clear();
            tab.Model.Items.Clear();

            for (var i = 0; i < ordered.Count; i++)
            {
                var item = ordered[i];
                item.Model.Order = i;
                tab.Items.Add(item);
                tab.Model.Items.Add(item.Model);
            }

            count += ordered.Count;
        }

        UpdateVisibleItems();
        return count;
    }

    public int RemoveDuplicates()
    {
        var removed = 0;
        foreach (var tab in Tabs.Where(tab => tab.Model.Kind == TabKind.Normal))
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in tab.Items.ToList())
            {
                var key = $"{item.Model.Kind}|{item.Model.Target}";
                if (seen.Add(key))
                {
                    continue;
                }

                tab.Items.Remove(item);
                tab.Model.Items.Remove(item.Model);
                removed++;
            }

            NormalizeOrder(tab);
        }

        UpdateVisibleItems();
        return removed;
    }

    public int RemoveInvalidItems()
    {
        var removed = 0;
        foreach (var tab in Tabs.Where(tab => tab.Model.Kind == TabKind.Normal))
        {
            foreach (var item in tab.Items.ToList())
            {
                if (IsTargetValid(item.Model))
                {
                    continue;
                }

                tab.Items.Remove(item);
                tab.Model.Items.Remove(item.Model);
                removed++;
            }

            NormalizeOrder(tab);
        }

        UpdateVisibleItems();
        return removed;
    }

    public int RemoveEmptyTabs()
    {
        var removed = 0;
        foreach (var tab in Tabs
                     .Where(tab => tab.Model.Kind == TabKind.Normal && tab.Items.Count == 0)
                     .ToList())
        {
            if (Tabs.Count <= 1)
            {
                break;
            }

            Tabs.Remove(tab);
            Config.Tabs.Remove(tab.Model);
            removed++;
        }

        if (removed > 0)
        {
            if (SelectedTab is null || !Tabs.Contains(SelectedTab))
            {
                SelectedTab = Tabs.FirstOrDefault();
            }

            UpdateVisibleItems();
        }

        return removed;
    }

    private static void NormalizeOrder(TabViewModel tab)
    {
        for (var i = 0; i < tab.Model.Items.Count; i++)
        {
            tab.Model.Items[i].Order = i;
        }
    }

    private static bool IsTargetValid(ItemModel model) => model.Kind switch
    {
        ItemKind.Exe or ItemKind.File or ItemKind.Folder => TargetExists(model.Target),
        _ => true
    };

    private static bool TargetExists(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        // 裸命令名（如 explorer.exe）交给 Shell 解析，不视为失效
        if (!Path.IsPathRooted(target) && !target.Contains('\\') && !target.Contains('/'))
        {
            return true;
        }

        return File.Exists(target) || Directory.Exists(target);
    }

    private void UpdateVisibleItems()
    {
        VisibleItems.Clear();

        if (IsSearchActive && !string.IsNullOrWhiteSpace(SearchText))
        {
            var query = SearchText.Trim();
            var matches = Tabs.SelectMany(tab => tab.Items)
                .Where(item => Matches(item, query))
                .OrderByDescending(item => item.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                .ThenBy(item => item.Name.Length)
                .Take(64);

            foreach (var item in matches)
            {
                VisibleItems.Add(item);
            }

            return;
        }

        if (SelectedTab is null)
        {
            return;
        }

        var items = SelectedTab.Items;

        // 实时文件夹保持目录本身顺序；普通分组可按使用频率由多到少排列
        if (!SelectedTab.IsLiveFolder && Config.Settings.AutoSortByUsage)
        {
            foreach (var item in items
                         .OrderByDescending(item => item.Model.UseCount)
                         .ThenBy(item => item.Model.Order))
            {
                VisibleItems.Add(item);
            }

            return;
        }

        foreach (var item in items)
        {
            VisibleItems.Add(item);
        }
    }

    private static bool Matches(ItemViewModel item, string query) =>
        item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || item.Initials.Contains(query, StringComparison.OrdinalIgnoreCase)
        || item.Target.Contains(query, StringComparison.OrdinalIgnoreCase);
}
