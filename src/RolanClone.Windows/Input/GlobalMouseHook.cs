using System.Runtime.InteropServices;

namespace RolanClone.Windows.Input;

/// <summary>
/// 低级鼠标钩子：监听扩展键（XButton1 / XButton2）与中键按下。
/// 与 GlobalHotkey 一样在独立线程上安装钩子并跑消息循环。
/// </summary>
public sealed class GlobalMouseHook : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmMButtonDown = 0x0207;
    private const int WmXButtonDown = 0x020B;
    private const int WmQuit = 0x0012;

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly HookProc _proc;
    private uint _threadId;
    private IntPtr _hook;
    private volatile bool _running = true;

    public GlobalMouseHook()
    {
        _proc = HookCallback;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "LaunchDock.GlobalMouseHook"
        };
        _thread.Start();
        _ready.Wait(1500);
    }

    public bool IsHooked => _hook != IntPtr.Zero;

    public event Action? XButton1Pressed;
    public event Action? XButton2Pressed;
    public event Action? MiddlePressed;

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        _hook = SetWindowsHookEx(WhMouseLl, _proc, GetModuleHandle(null), 0);
        _ready.Set();

        while (_running)
        {
            var result = GetMessage(out _, IntPtr.Zero, 0, 0);
            if (result == 0 || result == -1)
            {
                break;
            }
        }

        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = wParam.ToInt32();
            if (message == WmXButtonDown)
            {
                var data = Marshal.PtrToStructure<MouseHookInfo>(lParam);
                var button = (data.MouseData >> 16) & 0xFFFF;
                if (button == 1)
                {
                    XButton1Pressed?.Invoke();
                }
                else if (button == 2)
                {
                    XButton2Pressed?.Invoke();
                }
            }
            else if (message == WmMButtonDown)
            {
                MiddlePressed?.Invoke();
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        _running = false;
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookInfo
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out NativeMessage message, IntPtr hWnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }
}
