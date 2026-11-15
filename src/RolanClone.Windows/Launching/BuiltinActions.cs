using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RolanClone.Windows.Launching;

/// <summary>
/// 内置系统动作：音量、媒体控制、锁屏、电源、系统工具等。
/// </summary>
public static class BuiltinActions
{
    public static readonly (string Key, string Name)[] All =
    {
        ("volume-up", "音量 +"),
        ("volume-down", "音量 -"),
        ("volume-mute", "静音"),
        ("media-play", "播放 / 暂停"),
        ("media-prev", "上一首"),
        ("media-next", "下一首"),
        ("empty-bin", "清空回收站"),
        ("lock", "锁定电脑"),
        ("sleep", "睡眠"),
        ("screen-off", "关闭屏幕"),
        ("shutdown", "关机"),
        ("restart", "重启"),
        ("calc", "计算器"),
        ("task-manager", "任务管理器"),
        ("control-panel", "控制面板"),
        ("settings", "系统设置")
    };

    public static string GetName(string key) =>
        All.FirstOrDefault(action => action.Key == key).Name ?? key;

    public static bool Run(string key)
    {
        try
        {
            switch (key)
            {
                case "volume-up":
                    PressKey(0xAF);
                    return true;
                case "volume-down":
                    PressKey(0xAE);
                    return true;
                case "volume-mute":
                    PressKey(0xAD);
                    return true;
                case "media-play":
                    PressKey(0xB3);
                    return true;
                case "media-prev":
                    PressKey(0xB1);
                    return true;
                case "media-next":
                    PressKey(0xB0);
                    return true;
                case "empty-bin":
                    return SHEmptyRecycleBin(IntPtr.Zero, null, 0) >= 0;
                case "lock":
                    return LockWorkStation();
                case "sleep":
                    return SetSuspendState(false, false, false);
                case "screen-off":
                    SendMessage((IntPtr)0xFFFF, 0x0112, (IntPtr)0xF170, (IntPtr)2);
                    return true;
                case "shutdown":
                    EnableShutdownPrivilege();
                    return ExitWindowsEx(0x00000001 | 0x00000008 | 0x00000010, 0);
                case "restart":
                    EnableShutdownPrivilege();
                    return ExitWindowsEx(0x00000002 | 0x00000010, 0);
                case "calc":
                    return Start("calc.exe");
                case "task-manager":
                    return Start("taskmgr.exe");
                case "control-panel":
                    return Start("control.exe");
                case "settings":
                    return Start("ms-settings:");
                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"内置动作失败: {key} -> {ex.Message}");
            return false;
        }
    }

    private static bool Start(string fileName)
    {
        Process.Start(new ProcessStartInfo(fileName) { UseShellExecute = true });
        return true;
    }

    private static void PressKey(byte virtualKey)
    {
        keybd_event(virtualKey, 0, 0, UIntPtr.Zero);
        keybd_event(virtualKey, 0, 0x0002, UIntPtr.Zero);
    }

    private static bool EnableShutdownPrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008, out var token))
        {
            return false;
        }

        try
        {
            if (!LookupPrivilegeValue(null, "SeShutdownPrivilege", out var luid))
            {
                return false;
            }

            var privileges = new TokenPrivileges
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = 0x00000002
            };

            return AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public Luid Luid;
        public uint Attributes;
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, uint flags);

    [DllImport("user32.dll")]
    private static extern bool LockWorkStation();

    [DllImport("powrprof.dll")]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ExitWindowsEx(uint flags, uint reason);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? systemName, string name, out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr tokenHandle,
        bool disableAllPrivileges,
        ref TokenPrivileges newState,
        uint bufferLength,
        IntPtr previousState,
        IntPtr returnLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
