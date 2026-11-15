namespace RolanClone.Core.Model;

public enum TabKind
{
    Normal,
    LiveFolder,
    Bridge
}

public enum ItemKind
{
    Exe,
    File,
    Folder,
    Url,
    Command,
    Uwp,
    Builtin
}

public sealed class AppConfig
{
    public int SchemaVersion { get; set; } = 1;
    public List<TabModel> Tabs { get; set; } = new();
    public SettingsModel Settings { get; set; } = new();
    public WindowStateModel Window { get; set; } = new();
}

public sealed class TabModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新分组";
    public int Order { get; set; }
    public TabKind Kind { get; set; } = TabKind.Normal;
    public string? SourcePath { get; set; }
    public bool IsLocked { get; set; }
    public string? PasswordHash { get; set; }
    public List<ItemModel> Items { get; set; } = new();
}

public sealed class ItemModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = "";
    public ItemKind Kind { get; set; } = ItemKind.Exe;
    public string Target { get; set; } = "";
    public string? Arguments { get; set; }
    public string? WorkingDirectory { get; set; }
    public string? CustomIcon { get; set; }
    public bool RunAsAdmin { get; set; }
    public int UseCount { get; set; }
    public DateTime? LastUsed { get; set; }
    public int Order { get; set; }
}

public sealed class WindowStateModel
{
    /// <summary>物理像素坐标；为空表示首次运行，使用居中显示。</summary>
    public int? Left { get; set; }
    public int? Top { get; set; }
    public double Width { get; set; } = 1026;
    public double Height { get; set; } = 797;
    public string? SelectedTabId { get; set; }
}
