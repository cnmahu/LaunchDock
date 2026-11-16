using Avalonia;
using LaunchDock.App.Views;
using LaunchDock.Core.Model;
using Xunit;

namespace LaunchDock.App.Tests;

/// <summary>M1 第一步提取出的纯计算方法测试（静态调用，不实例化窗口）。</summary>
public sealed class LauncherWindowStaticTests
{
    // 工作区 1920x1040（模拟 1080p 去掉任务栏），窗口 800x600，吸附阈值 12px
    private static readonly PixelRect Area = new(0, 0, 1920, 1040);
    private const int Width = 800;
    private const int Height = 600;

    private static PixelPoint? Snap(int x, int y) =>
        LauncherWindow.ComputeSnapPosition(new PixelPoint(x, y), Area, Width, Height);

    private static void AssertSnap(int x, int y, int expectedX, int expectedY)
    {
        var result = Snap(x, y);
        Assert.True(result.HasValue, $"({x}, {y}) 应产生吸附坐标");
        Assert.Equal(new PixelPoint(expectedX, expectedY), result.Value);
    }

    [Fact]
    public void ComputeSnapPosition_LeftEdge_SnapsToLeft()
    {
        AssertSnap(5, 300, 0, 300);
    }

    [Fact]
    public void ComputeSnapPosition_RightEdge_SnapsToRight()
    {
        AssertSnap(1110, 300, 1120, 300); // 1920 - 800 = 1120
    }

    [Fact]
    public void ComputeSnapPosition_TopEdge_SnapsToTop()
    {
        AssertSnap(500, 4, 500, 0);
    }

    [Fact]
    public void ComputeSnapPosition_BottomEdge_SnapsToBottom()
    {
        AssertSnap(500, 428, 500, 440); // 1040 - 600 = 440
    }

    [Fact]
    public void ComputeSnapPosition_Corner_SnapsBothAxes()
    {
        AssertSnap(3, 2, 0, 0);
    }

    [Fact]
    public void ComputeSnapPosition_AtExactThreshold_Snaps()
    {
        AssertSnap(12, 300, 0, 300); // 恰好 12px：<= 边界
    }

    [Fact]
    public void ComputeSnapPosition_BeyondThreshold_ReturnsNull()
    {
        Assert.Null(Snap(13, 300));
        Assert.Null(Snap(500, 300));
    }

    [Fact]
    public void ComputeSnapPosition_NegativeOriginWorkingArea_SnapsCorrectly()
    {
        // 副屏在主屏左侧时工作区坐标为负
        var area = new PixelRect(-1920, 0, 1920, 1040);
        var result = LauncherWindow.ComputeSnapPosition(
            new PixelPoint(-1915, 100), area, Width, Height);
        Assert.True(result.HasValue);
        Assert.Equal(new PixelPoint(-1920, 100), result.Value);
    }

    [Theory]
    [InlineData(@"C:\apps\tool.exe", ItemKind.Exe, "tool")]
    [InlineData(@"C:\links\site.url", ItemKind.Url, "site")]
    [InlineData(@"C:\links\site.URL", ItemKind.Url, "site")]   // 扩展名大小写不敏感
    [InlineData(@"C:\links\site.website", ItemKind.Url, "site")]
    [InlineData(@"C:\docs\readme.txt", ItemKind.Exe, "readme")] // 默认按 Exe（Shell 启动）
    [InlineData(@"C:\docs\README", ItemKind.Exe, "README")]     // 无扩展名
    public void CreateItemFromPath_ClassifiesKindAndName(string path, ItemKind kind, string name)
    {
        var model = LauncherWindow.CreateItemFromPath(path);
        Assert.Equal(kind, model.Kind);
        Assert.Equal(name, model.DisplayName);
        Assert.Equal(path, model.Target);
    }

    [Fact]
    public void CreateItemFromPath_ExistingDirectory_ClassifiesAsFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "launchdock-snap-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var model = LauncherWindow.CreateItemFromPath(dir);
            Assert.Equal(ItemKind.Folder, model.Kind);
            Assert.Equal(Path.GetFileName(dir), model.DisplayName);
            Assert.Equal(dir, model.Target);
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    [Fact]
    public void CreateItemFromPath_EmptyFileName_FallsBackToFullPath()
    {
        // 目录路径结尾带分隔符时取不到文件名，应回退为完整路径
        var model = LauncherWindow.CreateItemFromPath(@"C:\");
        Assert.Equal(ItemKind.Folder, model.Kind);
        Assert.Equal(@"C:\", model.DisplayName);
    }
}
