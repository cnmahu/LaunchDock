using LaunchDock.Windows.Input;
using Xunit;

namespace LaunchDock.Windows.Tests;

/// <summary>
/// TryParseGesture 解析核心的边界回归（经公共 IsValid 覆盖）。
/// IsValid 与 ParseGesture 共用同一解析核心，此处固化既有行为。
/// </summary>
public sealed class GlobalHotkeyGestureTests
{
    [Theory]
    [InlineData("Alt+Space")]
    [InlineData("alt+space")]          // 大小写容忍
    [InlineData("  Ctrl + A  ")]        // 片段空格容忍
    [InlineData("Ctrl+Shift+A")]        // 多修饰键
    [InlineData("Ctrl+`")]              // 符号键（VK_OEM_3）
    [InlineData("Ctrl+-")]             // 符号键（减号）
    [InlineData("Ctrl+\\")]            // 符号键（反斜杠）
    [InlineData("Ctrl+F1")]             // 功能键下界
    [InlineData("Ctrl+F24")]            // 功能键上界
    [InlineData("Win+D")]               // Win 修饰键
    [InlineData("control+escape")]     // control / escape 别名
    [InlineData("Alt+1")]               // 数字键
    [InlineData("Alt+A+B")]             // 多按键片段：旧行为为有效（取最后一个）
    public void IsValid_AcceptsKnownGestures(string gesture) =>
        Assert.True(GlobalHotkey.IsValid(gesture));

    [Theory]
    [InlineData("")]                    // 空串
    [InlineData("   ")]                 // 分割后无片段
    [InlineData("A")]                   // 无修饰键
    [InlineData("Ctrl+Shift")]          // 无按键
    [InlineData("Ctrl+Foo")]            // 未知片段
    [InlineData("Ctrl+F0")]             // 功能键号下界之外
    [InlineData("Ctrl+F25")]            // 功能键号上界之外
    [InlineData("Ctrl+F100")]           // 片段超长（>3 字符）
    [InlineData("Ctrl++")]              // 空按键片段（拆分后被移除）
    [InlineData("Ctrl+Shift+Win+Alt")]  // 只有修饰键
    public void IsValid_RejectsInvalidGestures(string gesture) =>
        Assert.False(GlobalHotkey.IsValid(gesture));
}

/// <summary>C2 回归：Dispose 必须真正注销热键，且幂等。</summary>
public sealed class GlobalHotkeyDisposeTests
{
    private const string Gesture = "Ctrl+Alt+Shift+F13";

    [Fact]
    public void Dispose_ReleasesHotkeyForReRegistration()
    {
        using var first = new GlobalHotkey(Gesture);
        Assert.True(first.IsRegistered); // 前提：该冷门组合未被其它程序占用

        first.Dispose();
        first.Dispose();                 // 幂等：重复释放不抛异常

        using var second = new GlobalHotkey(Gesture);
        Assert.True(second.IsRegistered,
            "Dispose 后同一热键必须可以重新注册（证明旧热键已注销、旧线程已退出消息循环）");
    }
}
