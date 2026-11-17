using System.Runtime.InteropServices;

namespace LaunchDock.Windows.Interop;

internal static class ShellInterop
{
    public const uint ShgfiIcon = 0x000000100;
    public const uint ShgfiLargeIcon = 0x000000000;
    public const uint ShgfiIconLocation = 0x000000200;

    [StructLayout(LayoutKind.Sequential)]
    public struct Size
    {
        public int Cx;
        public int Cy;

        public Size(int cx, int cy)
        {
            Cx = cx;
            Cy = cy;
        }
    }

    [Flags]
    public enum Siigbf
    {
        ResizeToFit = 0x00000000,
        BiggerSizeOk = 0x00000001,
        MemoryOnly = 0x00000002,
        IconOnly = 0x00000004,
        ThumbnailOnly = 0x00000008,
        InCacheOnly = 0x00000010
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItemImageFactory
    {
        void GetImage(Size size, Siigbf flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct ShFileInfo
    {
        public IntPtr HIcon;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    public static extern IShellItemImageFactory SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string path,
        IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHParseDisplayName(
        [MarshalAs(UnmanagedType.LPWStr)] string name,
        IntPtr pbc,
        out IntPtr pidl,
        uint sfgaoIn,
        out uint sfgaoOut);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    public static extern IShellItemImageFactory SHCreateItemFromIDList(
        IntPtr pidl,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid);

    [DllImport("ole32.dll")]
    public static extern void CoTaskMemFree(IntPtr pv);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SHGetFileInfo(
        string path,
        uint fileAttributes,
        ref ShFileInfo fileInfo,
        uint fileInfoSize,
        uint flags);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    /// 按指定尺寸提取文件图标：会把最接近的图标帧缩放到请求尺寸，
    /// 而 IShellItemImageFactory::GetImage 只会原尺寸居中、不放大。
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint PrivateExtractIcons(
        string fileName,
        int iconIndex,
        int cxIcon,
        int cyIcon,
        IntPtr[]? phicon,
        int[]? piconid,
        uint count,
        uint flags);

    [StructLayout(LayoutKind.Sequential)]
    public struct IconInfo
    {
        public int IsIcon;
        public int XHotspot;
        public int YHotspot;
        public IntPtr MaskBitmap;
        public IntPtr ColorBitmap;
    }

    [DllImport("user32.dll")]
    public static extern bool GetIconInfo(IntPtr hIcon, ref IconInfo info);
}
