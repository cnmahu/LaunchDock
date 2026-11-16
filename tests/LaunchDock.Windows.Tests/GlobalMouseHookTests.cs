using LaunchDock.Windows.Input;
using Xunit;

namespace LaunchDock.Windows.Tests;

/// <summary>C2 回归：低级鼠标钩子的安装与释放。</summary>
public sealed class GlobalMouseHookTests
{
    [Fact]
    public void Install_DisposeIsIdempotent_AndHookCanBeReinstalled()
    {
        var first = new GlobalMouseHook();
        Assert.True(first.IsHooked);

        first.Dispose();
        first.Dispose();                  // 幂等

        Assert.False(first.IsHooked);

        var second = new GlobalMouseHook();
        Assert.True(second.IsHooked);
        second.Dispose();
    }
}
