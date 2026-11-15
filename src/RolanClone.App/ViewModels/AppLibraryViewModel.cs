using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Threading;
using RolanClone.Core.Model;
using RolanClone.Windows.Launching;
using RolanClone.Windows.Library;

namespace RolanClone.App.ViewModels;

/// <summary>应用库窗口与启动板之间传递拖拽内容。</summary>
public static class AppLibraryDrag
{
    public const string Format = "rolanclone/library";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string Serialize(ItemModel model) => JsonSerializer.Serialize(model, Options);

    public static ItemModel? Deserialize(string? payload)
    {
        try
        {
            return string.IsNullOrWhiteSpace(payload)
                ? null
                : JsonSerializer.Deserialize<ItemModel>(payload, Options);
        }
        catch
        {
            return null;
        }
    }
}

public sealed class AppLibraryViewModel
{
    public AppLibraryViewModel(AppConfig config)
    {
        Settings = config.Settings;

        LoadStandard();
        LoadSystemTools();
        LoadWebApps(config);
        LoadExtensions();
        ApplyAppearance();

        _ = LoadAsync();
    }

    private SettingsModel Settings { get; }

    public ObservableCollection<ItemViewModel> StandardFolders { get; } = new();
    public ObservableCollection<ItemViewModel> SystemTools { get; } = new();
    public ObservableCollection<ItemViewModel> DesktopApps { get; } = new();
    public ObservableCollection<ItemViewModel> StoreApps { get; } = new();
    public ObservableCollection<ItemViewModel> WebApps { get; } = new();
    public ObservableCollection<ItemViewModel> Extensions { get; } = new();

    private IEnumerable<ItemViewModel> AllItems() =>
        StandardFolders.Concat(SystemTools).Concat(DesktopApps)
            .Concat(StoreApps).Concat(WebApps).Concat(Extensions);

    /// <summary>应用库的图标大小、间距、名称位置跟随启动板设置。</summary>
    public void ApplyAppearance()
    {
        foreach (var item in AllItems())
        {
            item.ApplyAppearance(
                Settings.IconSizePx,
                Settings.LabelPosition != LabelPosition.None,
                Settings.LabelPosition == LabelPosition.Right,
                Settings.IconSpacing);
        }
    }

    /// <summary>维护页“修复图标”后重新加载库内图标。</summary>
    public void RefreshIcons()
    {
        foreach (var item in AllItems())
        {
            item.RefreshIcon();
        }
    }

    private void LoadStandard()
    {
        var index = 0;
        foreach (var entry in AppLibraryService.GetStandardFolders())
        {
            StandardFolders.Add(new ItemViewModel(entry.ToItemModel(), index++, isLive: true));
        }
    }

    private void LoadSystemTools()
    {
        var index = 0;
        foreach (var entry in AppLibraryService.GetSystemTools())
        {
            SystemTools.Add(new ItemViewModel(entry.ToItemModel(), index++, isLive: true));
        }
    }

    private void LoadWebApps(AppConfig config)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var item in config.Tabs.SelectMany(tab => tab.Items).Where(item => item.Kind == ItemKind.Url))
        {
            if (!seen.Add(item.Target))
            {
                continue;
            }

            WebApps.Add(new ItemViewModel(new ItemModel
            {
                DisplayName = item.DisplayName,
                Kind = ItemKind.Url,
                Target = item.Target
            }, index++, isLive: true));
        }
    }

    private void LoadExtensions()
    {
        var index = 0;
        foreach (var action in BuiltinActions.All)
        {
            Extensions.Add(new ItemViewModel(new ItemModel
            {
                DisplayName = action.Name,
                Kind = ItemKind.Builtin,
                Target = action.Key
            }, index++, isLive: true));
        }
    }

    private async Task LoadAsync()
    {
        var (desktop, store) = await Task.Run(() =>
            (AppLibraryService.GetDesktopApps(), AppLibraryService.GetStoreApps()));

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var index = 0;
            foreach (var entry in desktop)
            {
                DesktopApps.Add(new ItemViewModel(entry.ToItemModel(), index++, isLive: true));
            }

            index = 0;
            foreach (var entry in store)
            {
                StoreApps.Add(new ItemViewModel(entry.ToItemModel(), index++, isLive: true));
            }

            ApplyAppearance();
        });
    }
}
