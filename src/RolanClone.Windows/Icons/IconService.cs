using System.Security.Cryptography;
using System.Text;

namespace RolanClone.Windows.Icons;

/// <summary>
/// 图标磁盘缓存，按“路径 + 修改时间 + 尺寸”生成键，避免每次启动重复提取。
/// </summary>
public static class IconService
{
    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RolanClone", "icons");

    // 应用库可能一次请求数百个图标，限制同时进行的 Shell 提取数量。
    private static readonly SemaphoreSlim ExtractionGate = new(4, 4);

    /// <summary>清空磁盘图标缓存，下次读取时重新从 Shell 提取。</summary>
    public static void ClearCache()
    {
        try
        {
            if (Directory.Exists(CacheDirectory))
            {
                Directory.Delete(CacheDirectory, true);
            }
        }
        catch
        {
            // 缓存被占用时忽略，后续写入会自动覆盖
        }
    }

    /// <summary>返回缓存 PNG 的完整路径；无法提取时返回 null。</summary>
    public static string? GetIconPath(string target, string? customIcon = null)
    {
        var source = string.IsNullOrWhiteSpace(customIcon) ? target : customIcon;
        var resolved = ResolvePath(source);
        if (resolved is null)
        {
            return null;
        }

        long stamp;
        if (File.Exists(resolved))
        {
            stamp = File.GetLastWriteTimeUtc(resolved).Ticks;
        }
        else if (Directory.Exists(resolved))
        {
            stamp = Directory.GetLastWriteTimeUtc(resolved).Ticks;
        }
        else
        {
            // shell:AppsFolder 之类的虚拟路径无法取文件时间
            stamp = 0;
        }

        var key = Convert.ToHexString(SHA1.HashData(
            Encoding.UTF8.GetBytes($"{resolved.ToLowerInvariant()}|{stamp}|256|v2"))).ToLowerInvariant();

        var pngPath = Path.Combine(CacheDirectory, key + ".png");
        if (File.Exists(pngPath))
        {
            return pngPath;
        }

        byte[]? data;
        ExtractionGate.Wait();
        try
        {
            data = IconExtractor.ExtractPng(resolved, 256);
        }
        catch
        {
            data = null;
        }
        finally
        {
            ExtractionGate.Release();
        }

        if (data is null)
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(CacheDirectory);
            var temp = pngPath + ".tmp";
            File.WriteAllBytes(temp, data);
            File.Move(temp, pngPath, overwrite: true);
            return pngPath;
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolvePath(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        target = Environment.ExpandEnvironmentVariables(target.Trim());

        // 标准文件夹的 shell 命名空间路径
        if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
            || target.StartsWith("::", StringComparison.Ordinal))
        {
            return target;
        }

        // UWP AUMID（包族名!AppId）：交给 shell:AppsFolder 解析包图标。
        if (target.Contains('!'))
        {
            return "shell:AppsFolder\\" + target;
        }

        if (Path.IsPathRooted(target))
        {
            return File.Exists(target) || Directory.Exists(target) ? target : null;
        }

        if (target.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return null;
        }

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim(), target);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

