using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace LaunchDock.Windows.Icons;

/// <summary>
/// 图标磁盘缓存，按“路径 + 修改时间 + 尺寸”生成键，避免每次启动重复提取。
/// </summary>
public static class IconService
{
    private static string? _cacheDirectoryOverrideForTests;

    /// <summary>仅测试使用：重定向图标缓存目录（传 null 复位）。生产代码不调用。</summary>
    internal static void SetCacheDirectoryForTests(string? directory) =>
        _cacheDirectoryOverrideForTests = directory;

    private static string DefaultCacheDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LaunchDock", "icons");

    internal static string CacheDirectory => _cacheDirectoryOverrideForTests ?? DefaultCacheDirectory;

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
        var (resolved, cached, pngPath) = Lookup(target, customIcon);
        if (resolved is null)
        {
            return null;
        }

        if (cached is not null)
        {
            return cached;
        }

        var data = ExtractOnSta(resolved, 256);
        if (data is null)
        {
            return null;
        }

        try
        {
            SaveToCache(pngPath!, data);
            return pngPath;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// GetIconPath 的异步版本：解析与缓存探测在线程池执行，等待 STA 工人提取时
    /// 调用方线程立即归还线程池（旧实现是阻塞等待），数百个图标并发加载
    /// 不会再把线程池占满。缓存命中时直接返回已存在的路径。
    /// </summary>
    public static async Task<string?> GetIconPathAsync(string target, string? customIcon = null)
    {
        var (resolved, cached, pngPath) = await Task.Run(() => Lookup(target, customIcon));

        if (resolved is null)
        {
            return null;
        }

        if (cached is not null)
        {
            return cached;
        }

        var data = await ExtractOnStaAsync(resolved, 256);
        if (data is null)
        {
            return null;
        }

        try
        {
            await Task.Run(() => SaveToCache(pngPath!, data));
            return pngPath;
        }
        catch
        {
            return null;
        }
    }

    private static void SaveToCache(string pngPath, byte[] data)
    {
        Directory.CreateDirectory(CacheDirectory);
        var temp = pngPath + ".tmp";
        File.WriteAllBytes(temp, data);
        File.Move(temp, pngPath, overwrite: true);
    }

    /// <summary>
    /// 定位缓存条目：解析目标、计算缓存键。resolved 为 null 表示目标不可解析；
    /// cached 非 null 表示缓存命中（此时 cached 与 pngPath 相同）。
    /// </summary>
    private static (string? Resolved, string? Cached, string? PngPath) Lookup(
        string target, string? customIcon)
    {
        var source = string.IsNullOrWhiteSpace(customIcon) ? target : customIcon;
        var resolved = ResolvePath(source);
        if (resolved is null)
        {
            return (null, null, null);
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
        return File.Exists(pngPath) ? (resolved, pngPath, pngPath) : (resolved, null, pngPath);
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

            // TrySetResult 防御进程关闭时的完成竞争
            request.Completion.TrySetResult(data);
        }
    }

    /// <summary>Shell 图标统一在 STA 线程提取；同步版本会阻塞调用线程直至提取完成。</summary>
    private static byte[]? ExtractOnSta(string path, int size)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return IconExtractor.ExtractPng(path, size);
        }

        var request = new ExtractionRequest(path, size);
        Requests.Add(request);
        return request.Completion.Task.GetAwaiter().GetResult();
    }

    /// <summary>异步版本：入队即返回，等待提取期间不占用任何线程。</summary>
    private static Task<byte[]?> ExtractOnStaAsync(string path, int size)
    {
        var request = new ExtractionRequest(path, size);
        Requests.Add(request);
        return request.Completion.Task;
    }

    private sealed class ExtractionRequest
    {
        public ExtractionRequest(string path, int size)
        {
            Path = path;
            Size = size;
            // 完成回调放到线程池执行，避免 continuation 挤占 STA 工人线程
            Completion = new TaskCompletionSource<byte[]?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public string Path { get; }
        public int Size { get; }
        public TaskCompletionSource<byte[]?> Completion { get; }
    }
}
