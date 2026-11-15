using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Microsoft.Win32;

namespace LaunchDock.App.Services;

/// <summary>
/// 自绘云母背景：取当前桌面壁纸，重模糊后叠加主题色调，得到与 Windows 11 云母相近的底色。
/// 为什么不直接用 WindowTransparencyLevel.Mica：云母要求窗口逐像素透明，Skia 在透明表面上
/// 只能做灰度抗锯齿，文字会明显发粗；改为不透明窗口 + 自绘底色后文字可用 ClearType。
/// </summary>
public static class MicaBackdrop
{
    // 采样越小越接近“整片色调”，与 Windows 云母那种几乎看不出壁纸细节的观感一致
    private const int SampleWidth = 6;
    private const int SampleHeight = 4;
    private const int OutputWidth = 320;
    private const int OutputHeight = 200;

    private static string? _cachedWallpaper;
    private static DateTime _cachedWallpaperTime;
    private static RenderTargetBitmap? _light;
    private static RenderTargetBitmap? _dark;

    /// <summary>生成云母底色；拿不到壁纸时返回 null，由调用方回退到纯色。</summary>
    public static IBrush? Create(bool dark)
    {
        var cached = dark ? _dark : _light;
        var path = ReadWallpaperPath();
        if (path is null)
        {
            return null;
        }

        var stamp = File.GetLastWriteTimeUtc(path);
        if (cached is not null && path == _cachedWallpaper && stamp == _cachedWallpaperTime)
        {
            return new ImageBrush(cached) { Stretch = Stretch.Fill };
        }

        var sampled = SampleWallpaper(path);
        if (sampled is null)
        {
            return null;
        }

        var result = Compose(sampled, dark);
        _cachedWallpaper = path;
        _cachedWallpaperTime = stamp;
        if (dark)
        {
            _dark = result;
        }
        else
        {
            _light = result;
        }

        return new ImageBrush(result) { Stretch = Stretch.Fill };
    }

    private static string? ReadWallpaperPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            var path = key?.GetValue("WallPaper") as string;
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>把壁纸缩到极小尺寸，相当于一次重度模糊，只保留整体色调。</summary>
    private static RenderTargetBitmap? SampleWallpaper(string path)
    {
        try
        {
            using var source = new Bitmap(path);
            var sample = new RenderTargetBitmap(new PixelSize(SampleWidth, SampleHeight), new Vector(96, 96));
            using (var ctx = sample.CreateDrawingContext())
            using (ctx.PushRenderOptions(new RenderOptions
                   {
                       BitmapInterpolationMode = BitmapInterpolationMode.HighQuality
                   }))
            {
                ctx.DrawImage(source, new Rect(0, 0, SampleWidth, SampleHeight));
            }

            return sample;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>放大回可用尺寸并叠加主题色调（浅色接近白、深色接近炭黑），模拟云母的乳浊感。</summary>
    private static RenderTargetBitmap Compose(RenderTargetBitmap sample, bool dark)
    {
        var output = new RenderTargetBitmap(new PixelSize(OutputWidth, OutputHeight), new Vector(96, 96));
        using var ctx = output.CreateDrawingContext();
        using (ctx.PushRenderOptions(new RenderOptions
               {
                   BitmapInterpolationMode = BitmapInterpolationMode.HighQuality
               }))
        {
            ctx.DrawImage(sample, new Rect(0, 0, OutputWidth, OutputHeight));
        }

        // 色调叠在模糊壁纸之上：浅色取与系统云母一致的暖白，深色取炭黑；
        // 透明度压得很高，只留一点壁纸色调的渐变，避免看起来像亚克力那样“透内容”
        ctx.FillRectangle(new SolidColorBrush(dark ? Color.FromArgb(0xE8, 32, 32, 32) : Color.FromArgb(0xE6, 255, 241, 233)),
            new Rect(0, 0, OutputWidth, OutputHeight));

        return output;
    }
}
