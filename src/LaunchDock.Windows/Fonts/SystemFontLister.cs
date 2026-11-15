using System.Runtime.InteropServices;

namespace LaunchDock.Windows.Fonts;

/// <summary>
/// 枚举系统已安装的字体族。
/// 使用 GDI EnumFontFamiliesEx（与 Win32 字体对话框一致），
/// 比InstalledFontCollection 更完整，能看到可变字体等新字体。
/// </summary>
public static class SystemFontLister
{
    private const byte DefaultCharset = 1; // DEFAULT_CHARSET：枚举全部字符集

    public static List<string> GetInstalled()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hdc = GetDC(IntPtr.Zero);
        if (hdc != IntPtr.Zero)
        {
            try
            {
                var logFont = new LOGFONT { CharSet = DefaultCharset };
                var buffer = Marshal.AllocHGlobal(Marshal.SizeOf<LOGFONT>());
                try
                {
                    Marshal.StructureToPtr(logFont, buffer, false);
                    var proc = new EnumFontFamiliesProc((lpelfe, _, _) =>
                    {
                        var elf = Marshal.PtrToStructure<ENUMLOGFONTEX>(lpelfe);
                        var name = elf.LogFont.FaceName;
                        // “@”开头是竖排变体，不在选择列表中展示
                        if (!string.IsNullOrEmpty(name) && name[0] != '@')
                        {
                            names.Add(name);
                        }

                        return true;
                    });
                    EnumFontFamiliesEx(hdc, buffer, proc, IntPtr.Zero, 0);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch
            {
                // 枚举失败时返回空列表，设置界面仅显示默认字体项
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdc);
            }
        }

        return names.OrderBy(n => n, StringComparer.CurrentCulture).ToList();
    }

    private delegate bool EnumFontFamiliesProc(IntPtr lpelfe, IntPtr lpntme, int fontType);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll", EntryPoint = "EnumFontFamiliesExW")]
    private static extern int EnumFontFamiliesEx(
        IntPtr hdc, IntPtr logFont, EnumFontFamiliesProc proc, IntPtr lParam, int flags);

    [StructLayout(LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct LOGFONT
    {
        public int Height;
        public int Width;
        public int Escapement;
        public int Orientation;
        public int Weight;
        public byte Italic;
        public byte Underline;
        public byte StrikeOut;
        public byte CharSet;
        public byte OutPrecision;
        public byte ClipPrecision;
        public byte Quality;
        public byte PitchAndFamily;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FaceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct ENUMLOGFONTEX
    {
        public LOGFONT LogFont;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string FullName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Style;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Script;
    }
}
