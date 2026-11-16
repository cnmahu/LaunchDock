namespace LaunchDock.Core.Storage;

/// <summary>共享的文件命名工具：在目标目录内生成不冲突的路径。</summary>
public static class PathNaming
{
    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>
    /// 目录内已存在同名条目时追加 " (2)"、" (3)"… 序号。
    /// isDirectory 为 true 时把整个 name 当作目录名处理（目录名中的点不当作扩展名拆分）。
    /// </summary>
    public static string MakeUniquePath(string directory, string name, bool isDirectory = false)
    {
        var target = Path.Combine(directory, name);
        if (!Exists(target))
        {
            return target;
        }

        var stem = isDirectory ? name : Path.GetFileNameWithoutExtension(name);
        var extension = isDirectory ? string.Empty : Path.GetExtension(name);

        for (var i = 2; ; i++)
        {
            target = Path.Combine(directory, $"{stem} ({i}){extension}");
            if (!Exists(target))
            {
                return target;
            }
        }
    }
}
