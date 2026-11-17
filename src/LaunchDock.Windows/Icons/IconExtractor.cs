using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using LaunchDock.Windows.Interop;

namespace LaunchDock.Windows.Icons;

/// <summary>
/// 通过 Shell 取图标：优先 256px 大图标，失败时退回 32px 系统图标。
/// </summary>
public static class IconExtractor
{
    public static byte[]? ExtractPng(string path, int size)
    {
        var bitmap = ExtractBitmap(path, size);
        if (bitmap is null)
        {
            return null;
        }

        using (bitmap)
        using (var stream = new MemoryStream())
        {
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
    }

    private static ShellInterop.IShellItemImageFactory? CreateFactory(string path)
    {
        if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("::", StringComparison.Ordinal))
        {
            if (ShellInterop.SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _) == 0
                && pidl != IntPtr.Zero)
            {
                try
                {
                    return ShellInterop.SHCreateItemFromIDList(
                        pidl, typeof(ShellInterop.IShellItemImageFactory).GUID);
                }
                finally
                {
                    ShellInterop.CoTaskMemFree(pidl);
                }
            }

            return null;
        }

        return ShellInterop.SHCreateItemFromParsingName(
            path, IntPtr.Zero, typeof(ShellInterop.IShellItemImageFactory).GUID);
    }

    /// <summary>
    /// Image.FromHbitmap 会丢掉 alpha 通道，导致图标带黑色方底；
    /// 这里用 GetDIBits 按 32 位 BGRA 读回像素，保留透明度。
    /// </summary>
    private static Bitmap FromHBitmap(IntPtr hBitmap)
    {
        var native = new NativeBitmap();
        if (GetObject(hBitmap, Marshal.SizeOf<NativeBitmap>(), ref native) == 0
            || native.Width <= 0 || native.Height <= 0)
        {
            return Image.FromHbitmap(hBitmap);
        }

        var width = native.Width;
        var height = native.Height;
        var buffer = new byte[width * height * 4];
        var header = new BitmapInfo
        {
            Size = 40,
            Width = width,
            Height = -height, // 负数表示自上而下
            Planes = 1,
            BitCount = 32,
            Compression = 0
        };

        var hdc = CreateCompatibleDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(hdc, hBitmap, 0, (uint)height, buffer, ref header, 0) == 0)
            {
                return Image.FromHbitmap(hBitmap);
            }
        }
        finally
        {
            DeleteDC(hdc);
        }

        var hasAlpha = false;
        for (var i = 3; i < buffer.Length; i += 4)
        {
            if (buffer[i] != 0)
            {
                hasAlpha = true;
                break;
            }
        }

        if (!hasAlpha)
        {
            for (var i = 3; i < buffer.Length; i += 4)
            {
                buffer[i] = 255;
            }
        }

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmap
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public IntPtr Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public int Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr hObject, int size, ref NativeBitmap bitmap);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(
        IntPtr hdc,
        IntPtr hBitmap,
        uint start,
        uint lines,
        byte[] bits,
        ref BitmapInfo bitmapInfo,
        uint usage);

    private static Bitmap? ExtractBitmap(string path, int size)
    {
        IntPtr hBitmap = IntPtr.Zero;
        try
        {
            var factory = CreateFactory(path);
            if (factory is not null)
            {
                factory.GetImage(new ShellInterop.Size(size, size),
                    ShellInterop.Siigbf.IconOnly | ShellInterop.Siigbf.BiggerSizeOk, out hBitmap);

                if (hBitmap != IntPtr.Zero)
                {
                    var bitmap = FromHBitmap(hBitmap);

                    // Shell 行为：exe 缺少大尺寸帧时，GetImage 会把小图标按原始
                    // 尺寸画在请求尺寸的画布中央（不放大），显示出来就是
                    // “小图标 + 大空格子”。检测到字形明显偏小时改用
                    // PrivateExtractIcons 兜底，它会把最接近的帧缩放到请求尺寸。
                    if (GlyphFillsCanvas(bitmap) || !File.Exists(path))
                    {
                        return bitmap;
                    }

                    var scaled = ExtractScaledIcon(path, size);
                    if (scaled is null)
                    {
                        return bitmap;
                    }

                    bitmap.Dispose();
                    return scaled;
                }
            }
        }
        catch
        {
            // 落回下方 32px 方案
        }
        finally
        {
            if (hBitmap != IntPtr.Zero)
            {
                ShellInterop.DeleteObject(hBitmap);
            }
        }

        var info = new ShellInterop.ShFileInfo();
        var result = ShellInterop.SHGetFileInfo(path, 0, ref info,
            (uint)Marshal.SizeOf<ShellInterop.ShFileInfo>(),
            ShellInterop.ShgfiIcon | ShellInterop.ShgfiLargeIcon);

        if (result == IntPtr.Zero || info.HIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            using var icon = Icon.FromHandle(info.HIcon);
            return icon.ToBitmap();
        }
        finally
        {
            ShellInterop.DestroyIcon(info.HIcon);
        }
    }

    /// <summary>
    /// 判断不透明字形是否基本铺满画布。阈值取 alpha &gt; 128，
    /// 以避开 GetImage 画布边缘那圈低不透明度的白色装饰框（A 约 40-80）。
    /// </summary>
    private static bool GlyphFillsCanvas(Bitmap bitmap)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
        {
            return true;
        }

        var bounds = MeasureOpaqueBounds(bitmap, alphaThreshold: 128);
        if (bounds is null)
        {
            return true; // 全透明字形无从修正，交回原样
        }

        return bounds.Value.Width >= bitmap.Width * 0.6
            && bounds.Value.Height >= bitmap.Height * 0.6;
    }

    /// <summary>统计 alpha 超过阈值的不透明像素包围盒。</summary>
    private static Rectangle? MeasureOpaqueBounds(Bitmap bitmap, byte alphaThreshold)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var minX = bitmap.Width;
            var minY = bitmap.Height;
            var maxX = -1;
            var maxY = -1;
            var buffer = new byte[data.Stride * bitmap.Height];
            Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);

            for (var y = 0; y < bitmap.Height; y++)
            {
                var row = y * data.Stride;
                for (var x = 0; x < bitmap.Width; x++)
                {
                    if (buffer[row + x * 4 + 3] > alphaThreshold)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            return maxX < 0 ? null : new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    /// <summary>
    /// 把图标缩放到请求尺寸提取。优先取 SHGFI_ICONLOCATION 给出的图标文件
    /// 与索引（.lnk 会指向目标 exe 的图标），取不到时按原文件第 0 个图标处理。
    /// </summary>
    private static Bitmap? ExtractScaledIcon(string path, int size)
    {
        try
        {
            var info = new ShellInterop.ShFileInfo();
            var iconFile = path;
            var index = 0;
            if (ShellInterop.SHGetFileInfo(path, 0, ref info,
                    (uint)Marshal.SizeOf<ShellInterop.ShFileInfo>(),
                    ShellInterop.ShgfiIconLocation) != IntPtr.Zero)
            {
                if (!string.IsNullOrWhiteSpace(info.DisplayName) && File.Exists(info.DisplayName))
                {
                    iconFile = info.DisplayName;
                }
                index = info.IconIndex;
            }

            var hIcons = new IntPtr[1];
            if (ShellInterop.PrivateExtractIcons(iconFile, index, size, size,
                    hIcons, null, 1, 0) == 0 || hIcons[0] == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                using var icon = Icon.FromHandle(hIcons[0]);
                return icon.ToBitmap();
            }
            finally
            {
                ShellInterop.DestroyIcon(hIcons[0]);
            }
        }
        catch
        {
            return null;
        }
    }
}
