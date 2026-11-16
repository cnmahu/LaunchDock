using LaunchDock.Windows.Icons;
using Xunit;

namespace LaunchDock.Windows.Tests;

/// <summary>
/// P1 异步图标管线回归测试：缓存目录重定向到临时目录，不污染真实
/// %LOCALAPPDATA%。提取走真实 STA 工人与 Shell 互操作（Windows 集成路径）。
/// </summary>
public sealed class IconServiceTests : IDisposable
{
    private readonly string _cacheDir;
    private readonly string _workDir;

    public IconServiceTests()
    {
        _cacheDir = Path.Combine(Path.GetTempPath(), "launchdock-icon-cache-" + Guid.NewGuid().ToString("N"));
        _workDir = Path.Combine(Path.GetTempPath(), "launchdock-icon-src-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cacheDir);
        Directory.CreateDirectory(_workDir);
        IconService.SetCacheDirectoryForTests(_cacheDir);
    }

    public void Dispose()
    {
        IconService.SetCacheDirectoryForTests(null);
        try
        {
            Directory.Delete(_cacheDir, recursive: true);
        }
        catch
        {
            // 清理失败不影响测试结果
        }

        try
        {
            Directory.Delete(_workDir, recursive: true);
        }
        catch
        {
            // 同上
        }
    }

    private string MakeFile(string name)
    {
        var path = Path.Combine(_workDir, name);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public async Task GetIconPathAsync_ExtractsAndWritesCache()
    {
        var file = MakeFile("a.txt");

        var png = await IconService.GetIconPathAsync(file);

        Assert.NotNull(png);
        Assert.True(File.Exists(png), "提取结果应写入缓存目录");
        Assert.StartsWith(_cacheDir, png);
        Assert.True(new FileInfo(png).Length > 0);
    }

    [Fact]
    public async Task GetIconPathAsync_ThenSyncVersion_ReturnsSameCachedPath()
    {
        var file = MakeFile("b.txt");

        var asyncPath = await IconService.GetIconPathAsync(file);
        var syncPath = IconService.GetIconPath(file); // 应命中异步版写入的缓存

        Assert.NotNull(asyncPath);
        Assert.Equal(asyncPath, syncPath);
    }

    [Fact]
    public async Task GetIconPathAsync_MissingTarget_ReturnsNull()
    {
        var missing = Path.Combine(_workDir, "missing-subdir", "file.txt");

        var png = await IconService.GetIconPathAsync(missing);

        Assert.Null(png);
    }

    [Fact]
    public async Task GetIconPathAsync_CustomIconOverridesTarget()
    {
        var target = MakeFile("c.txt");
        var icon = MakeFile("d.txt");

        var png = await IconService.GetIconPathAsync(target, icon);
        Assert.NotNull(png);

        // 缓存键只由 customIcon 决定：不同 target、同一 customIcon 命中同一缓存文件
        var otherTarget = MakeFile("e.txt");
        var png2 = await IconService.GetIconPathAsync(otherTarget, icon);
        Assert.Equal(png, png2);
    }

    [Fact]
    public async Task GetIconPathAsync_TwentyConcurrentRequests_AllComplete()
    {
        // 回归线程池饥饿：旧实现里每个等待提取的调用都占着一个阻塞线程；
        // 新管线等待期间线程归还线程池，全部请求应正常完成
        var files = Enumerable.Range(0, 20).Select(i => MakeFile($"g{i}.txt")).ToList();

        var results = await Task.WhenAll(files.Select(file => IconService.GetIconPathAsync(file)));

        Assert.All(results, png => Assert.False(string.IsNullOrEmpty(png)));
        Assert.Equal(files.Count, results.Distinct().Count());
    }

    [Fact]
    public void GetIconPath_SyncVersion_StillWorks()
    {
        var file = MakeFile("h.txt");

        var png = IconService.GetIconPath(file);

        Assert.NotNull(png);
        Assert.True(File.Exists(png));
    }
}
