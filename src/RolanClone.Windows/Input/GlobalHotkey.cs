using System.Runtime.InteropServices;

namespace RolanClone.Windows.Input;

/// <summary>
/// 在独立线程上注册全局热键：RegisterHotKey 不指定窗口时，WM_HOTKEY 会投递到该线程的消息队列。
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int WmQuit = 0x0012;
    private const uint ModNoRepeat = 0x4000;

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private uint _threadId;
    private volatile bool _running = true;
    private bool _registered;

    public GlobalHotkey(string gesture)
    {
        var (modifiers, virtualKey) = ParseGesture(gesture);
        _thread = new Thread(() => Run(modifiers, virtualKey))
        {
            IsBackground = true,
            Name = "LaunchDock.GlobalHotkey"
        };
        _thread.Start();
        _ready.Wait(1500);
    }

    /// <summary>注册是否成功；失败通常表示组合键已被其他程序占用。</summary>
    public bool IsRegistered => _registered;

    public event Action? Pressed;

    private void Run(uint modifiers, uint virtualKey)
    {
        _threadId = GetCurrentThreadId();
        _registered = RegisterHotKey(IntPtr.Zero, 1, modifiers | ModNoRepeat, virtualKey);
        _ready.Set();

        while (_running)
        {
            var result = GetMessage(out var message, IntPtr.Zero, 0, 0);
            if (result == 0 || result == -1)
            {
                break;
            }

            if (message.Message == WmHotkey)
            {
                Pressed?.Invoke();
            }
        }

        if (_registered)
        {
            UnregisterHotKey(IntPtr.Zero, 1);
        }
    }

    public void Dispose()
    {
        _running = false;
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        }
    }

    /// <summary>校验热键格式：必须包含至少一个修饰键和一个可识别的按键。</summary>
    public static bool IsValid(string gesture)
    {
        uint modifiers = 0;
        var keyFound = false;

        foreach (var part in gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "alt":
                case "ctrl":
                case "control":
                case "shift":
                case "win":
                    modifiers |= 1;
                    break;
                case "space":
                case "enter":
                case "tab":
                case "esc":
                case "escape":
                    keyFound = true;
                    break;
                default:
                    if (part.Length == 1 && char.IsAsciiLetterOrDigit(part[0]))
                    {
                        keyFound = true;
                    }
                    else if (part.Length is >= 2 and <= 3
                             && (part[0] is 'F' or 'f')
                             && int.TryParse(part[1..], out var functionKey)
                             && functionKey is >= 1 and <= 24)
                    {
                        keyFound = true;
                    }
                    else
                    {
                        return false;
                    }

                    break;
            }
        }

        return modifiers != 0 && keyFound;
    }

    private static (uint Modifiers, uint VirtualKey) ParseGesture(string gesture)
    {
        uint modifiers = 0;
        uint virtualKey = 0x20; // Space

        foreach (var part in gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "alt":
                    modifiers |= 0x0001;
                    break;
                case "ctrl":
                case "control":
                    modifiers |= 0x0002;
                    break;
                case "shift":
                    modifiers |= 0x0004;
                    break;
                case "win":
                    modifiers |= 0x0008;
                    break;
                case "space":
                    virtualKey = 0x20;
                    break;
                case "enter":
                    virtualKey = 0x0D;
                    break;
                case "tab":
                    virtualKey = 0x09;
                    break;
                case "esc":
                case "escape":
                    virtualKey = 0x1B;
                    break;
                default:
                    if (part.Length == 1 && char.IsAsciiLetterOrDigit(part[0]))
                    {
                        virtualKey = char.ToUpperInvariant(part[0]);
                    }
                    else if (part.Length is >= 2 and <= 3
                             && (part[0] is 'F' or 'f')
                             && int.TryParse(part[1..], out var functionKey)
                             && functionKey is >= 1 and <= 24)
                    {
                        virtualKey = (uint)(0x6F + functionKey);
                    }

                    break;
            }
        }

        return (modifiers, virtualKey);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Point;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out NativeMessage message, IntPtr hWnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
