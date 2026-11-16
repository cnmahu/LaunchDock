using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using LaunchDock.App.ViewModels;
using LaunchDock.Core.Model;
using LaunchDock.Windows.Icons;
using Xunit;

namespace LaunchDock.App.Tests;

/// <summary>ItemViewModel 图标加载竞态回归（注入加载器实现确定性时序控制）。</summary>
public sealed class ItemViewModelTests
{
    private static Bitmap CreateBitmap(byte b, byte g, byte r)
    {
        var bitmap = new WriteableBitmap(new PixelSize(1, 1), new Vector(96, 96), PixelFormat.Bgra8888);
        using (var frame = bitmap.Lock())
        {
            unsafe
            {
                var pixel = (byte*)frame.Address;
                pixel[0] = b;
                pixel[1] = g;
                pixel[2] = r;
                pixel[3] = 255;
            }
        }

        return bitmap;
    }

    private static ItemModel Model() => new()
    {
        DisplayName = "测试条目",
        Kind = ItemKind.Exe,
        Target = @"C:\fake\target.exe"
    };

    [AvaloniaFact]
    public async Task LoadIcon_Completes_IconAssigned()
    {
        var bitmap = CreateBitmap(0, 0, 255);
        var vm = new ItemViewModel(Model(), 0, (target, customIcon) => Task.FromResult<Bitmap?>(bitmap));

        await Task.Delay(200); // 等待 UI 调度器泵完成异步赋值

        Assert.True(vm.HasIcon);
        Assert.Same(bitmap, vm.Icon);
    }

    [AvaloniaFact]
    public async Task LoadIcon_StaleLateResult_DoesNotOverwriteNewerIcon()
    {
        var stale = CreateBitmap(0, 0, 255);   // 第一轮（构造时）加载，被门控挂起
        var fresh = CreateBitmap(255, 0, 0);   // 第二轮（RefreshIcon）加载，立即完成
        var staleGate = new TaskCompletionSource<Bitmap?>();
        var callCount = 0;

        var vm = new ItemViewModel(Model(), 0, (target, customIcon) =>
            Interlocked.Increment(ref callCount) == 1
                ? staleGate.Task
                : Task.FromResult<Bitmap?>(fresh));

        vm.RefreshIcon();
        await Task.Delay(200);
        Assert.Same(fresh, vm.Icon);

        staleGate.SetResult(stale);            // 第一轮现在才迟到完成
        await Task.Delay(200);

        Assert.Same(fresh, vm.Icon);           // 陈旧结果不得覆盖新图标
    }

    [AvaloniaFact]
    public async Task RefreshIcon_ClearsIconImmediately()
    {
        var bitmap = CreateBitmap(0, 255, 0);
        var vm = new ItemViewModel(Model(), 0, (target, customIcon) => Task.FromResult<Bitmap?>(bitmap));

        await Task.Delay(200);
        Assert.Same(bitmap, vm.Icon);

        vm.RefreshIcon();
        Assert.Null(vm.Icon);                  // 重载前立即清空
    }

    [AvaloniaFact]
    public async Task DefaultLoader_RealAsyncPipeline_AssignsIconEndToEnd()
    {
        // P1 端到端回归：真实 IconService 异步提取 + STA 工人 + 位图解码 + UI 线程赋值全链路
        var workDir = Path.Combine(Path.GetTempPath(), "launchdock-e2e-" + Guid.NewGuid().ToString("N"));
        var cacheDir = workDir + "-cache";
        Directory.CreateDirectory(cacheDir);
        try
        {
            var file = Path.Combine(workDir, "note.txt");
            Directory.CreateDirectory(workDir);
            File.WriteAllText(file, "x");

            IconService.SetCacheDirectoryForTests(cacheDir);
            var vm = new ItemViewModel(new ItemModel
            {
                DisplayName = "真实提取",
                Kind = ItemKind.File,
                Target = file
            }, 0);

            for (var i = 0; i < 100 && vm.Icon is null; i++)
            {
                await Task.Delay(50); // 轮询期间 headless 调度器持续泵 UI 回调
            }

            Assert.NotNull(vm.Icon);
            Assert.True(vm.HasIcon);
        }
        finally
        {
            IconService.SetCacheDirectoryForTests(null);
            try
            {
                Directory.Delete(cacheDir, true);
                Directory.Delete(workDir, true);
            }
            catch
            {
                // 清理失败不影响测试结果
            }
        }
    }
}
