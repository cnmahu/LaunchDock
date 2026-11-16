using System.Collections.Specialized;
using LaunchDock.Core.Model;
using LaunchDock.App.ViewModels;
using Avalonia.Headless.XUnit;
using Xunit;

namespace LaunchDock.App.Tests;

/// <summary>C1 回归：_refreshTokens 的加锁保护与去抖/取消语义。</summary>
public sealed class LauncherViewModelTests
{
    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "launchdock-vm-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [AvaloniaFact]
    public async Task ScheduleRefresh_TwiceWithinDebounce_AppliesSingleRefresh()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.txt"), "x");

            var vm = new LauncherViewModel(new AppConfig());
            vm.AddTab(); // 占位普通分组
            var tab = vm.AddLiveFolderTab(dir);
            var resets = 0;
            tab.Items.CollectionChanged += (_, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Reset)
                {
                    resets++;
                }
            };

            vm.ScheduleRefresh(tab);
            vm.ScheduleRefresh(tab); // 第二次应取消第一次的待执行刷新
            await Task.Delay(700);     // 越过 400ms 去抖窗口

            Assert.Equal(1, resets);
            Assert.Single(tab.Items);
            vm.Dispose();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [AvaloniaFact]
    public async Task ScheduleRefresh_ThenRemoveTab_DoesNotRefreshRemovedTab()
    {
        var dir = CreateTempDir();
        try
        {
            var vm = new LauncherViewModel(new AppConfig());
            vm.AddTab();
            var tab = vm.AddLiveFolderTab(dir); // 初始刷新：目录为空 → Items 为空

            // 刷新调度之后目录才有内容；若迟到刷新作用于已移除分组，Items 会变成 1
            File.WriteAllText(Path.Combine(dir, "b.txt"), "x");
            vm.ScheduleRefresh(tab);
            vm.RemoveTab(tab);

            await Task.Delay(700);

            Assert.Empty(tab.Items); // 已移除的分组不接收迟到刷新
            vm.Dispose();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [AvaloniaFact]
    public async Task ConcurrentWatcherScheduling_AndTabLifecycle_DoNotCorruptState()
    {
        // 修复前：_refreshTokens（普通 Dictionary）被线程池线程的 ScheduleRefresh
        // 与 UI 线程的 StopWatcher/RemoveTab 并发读写，高压下会击穿字典。
        var dir = CreateTempDir();
        try
        {
            var vm = new LauncherViewModel(new AppConfig());
            vm.AddTab(); // 占位普通分组，保证 AddLiveFolderTab/RemoveTab 能循环执行

            var churn = Task.Run(() =>
            {
                for (var i = 0; i < 200; i++)
                {
                    var file = Path.Combine(dir, $"f{i % 20}.txt");
                    try
                    {
                        File.WriteAllText(file, "x");
                        File.Delete(file);
                    }
                    catch
                    {
                        // 重试周期内的瞬时占用可忽略
                    }
                }
            });

            for (var i = 0; i < 30; i++)
            {
                var tab = vm.AddLiveFolderTab(dir);
                vm.RemoveTab(tab);
            }

            await churn;
            await Task.Delay(1000); // 让最后的去抖与 UI 回调完成
            vm.Dispose();

            Assert.Single(vm.Tabs); // 只剩占位分组，状态一致
            Assert.Equal("新分组", vm.Tabs[0].Name);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
