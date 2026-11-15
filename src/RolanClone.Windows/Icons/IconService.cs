using System.Collections.Concurrent;
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

    // .msc 这类文件的图标由内嵌图标处理程序提供，必须在 STA 线程提取，
    // 线程池线程是 MTA，直接提取只会得到通用的“空白文档”图标。
    private static readonly BlockingCollection<ExtractionRequest> Requests = new();
    private static readonly Thread[] Workers = StartWorkers();

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
            Encoding.UTF8.GetBytes($"{resolved.ToLowerInvariant()}|{stamp}|256|v3"))).ToLowerInvariant();

        var pngPath = Path.Combine(CacheDirectory, key + ".png");
        if (File.Exists(pngPath))
        {
            return pngPath;
        }

        var data = ExtractOnSta(resolved, 256);

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

    private static Thread[] StartWorkers()
    {
        var workers = new Thread[2];
        for (var index = 0; index < workers.Length; index++)
        {
            var worker = new Thread(ProcessRequests)
            {
                IsBackground = true,
                Name = $"IconExtractionSTA{index}"
            };
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
            workers[index] = worker;
        }

        return workers;
    }

    private static void ProcessRequests()
    {
        foreach (var request in Requests.GetConsumingEnumerable())
        {
            byte[]? data;
            try
            {
                data = IconExtractor.ExtractPng(request.Path, request.Size);
            }
            catch
            {
                data = null;
            }

            request.Complete(data);
        }
    }

    /// <summary>Shell 图标统一在 STA 线程提取，调用方线程会等待结果。</summary>
    private static byte[]? ExtractOnSta(string path, int size)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return IconExtractor.ExtractPng(path, size);
        }

        using var completed = new ManualResetEventSlim();
        byte[]? result = null;
        Requests.Add(new ExtractionRequest(path, size, data =>
        {
            result = data;
            completed.Set();
        }));
        completed.Wait();
        return result;
    }

    private sealed class ExtractionRequest
    {
        public ExtractionRequest(string path, int size, Action<byte[]?> complete)
        {
            Path = path;
            Size = size;
            Complete = complete;
        }

        public string Path { get; }
        public int Size { get; }
        public Action<byte[]?> Complete { get; }
    }
}

