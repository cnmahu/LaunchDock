using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(LaunchDock.App.Tests.TestAppBuilder))]

namespace LaunchDock.App.Tests;

/// <summary>
/// Headless 测试宿主：提供可泵动的 UI 调度器与 Skia 位图支持，
/// 不加载任何真实窗口/样式，仅服务于 ViewModel 层测试。
/// </summary>
public sealed class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<HeadlessApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public sealed class HeadlessApp : Application
{
}
