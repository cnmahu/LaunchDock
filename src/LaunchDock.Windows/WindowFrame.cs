using LaunchDock.Windows.Interop;

namespace LaunchDock.Windows;

/// <summary>
/// 无边框窗口的原生外观处理：暗色边框、系统圆角、1px 透明边避免黑边。
/// 材质本身由 Avalonia 的 ExperimentalAcrylicBorder 负责。
/// </summary>
public static class WindowFrame
{
    public static void Apply(IntPtr hwnd, bool darkMode)
    {
        TrySetDarkMode(hwnd, darkMode);
        TryRoundCorners(hwnd);
        TryExtendFrame(hwnd);
    }

    private static void TrySetDarkMode(IntPtr hwnd, bool darkMode)
    {
        var value = darkMode ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaUseImmersiveDarkMode,
            ref value, sizeof(int));
    }

    private static void TryRoundCorners(IntPtr hwnd)
    {
        // DWMWCP_ROUND = 2
        var preference = 2;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaWindowCornerPreference,
            ref preference, sizeof(int));
    }

    private static void TryExtendFrame(IntPtr hwnd)
    {
        var margins = new NativeMethods.Margins { Left = 1, Right = 1, Top = 1, Bottom = 1 };
        NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins);
    }
}
