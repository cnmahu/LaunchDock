namespace LaunchDock.Core.Storage;

/// <summary>便签文件的存放目录：默认在程序所在目录的 Notes 子目录，可在设置中改写。</summary>
public static class NotesStore
{
    private const string FolderName = "Notes";

    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, FolderName);

    public static string Resolve(string? configured) =>
        string.IsNullOrWhiteSpace(configured) ? DefaultDirectory : configured;
}
