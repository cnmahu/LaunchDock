using LaunchDock.Core.Model;

namespace LaunchDock.Windows.Library;

/// <summary>应用库中的一个可添加条目。</summary>
public sealed record LibraryEntry(
    string Name,
    string Target,
    ItemKind Kind,
    string? Arguments = null,
    string? IconTarget = null)
{
    public ItemModel ToItemModel() => new()
    {
        DisplayName = Name,
        Kind = Kind,
        Target = Target,
        Arguments = Arguments,
        CustomIcon = IconTarget
    };
}
