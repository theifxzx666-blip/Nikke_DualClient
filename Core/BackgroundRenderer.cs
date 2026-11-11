using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace NikkeDualClient.Core;

/// <summary>
/// 背景图渲染。主窗口、设置页预览、裁切画布共用同一套布局算法，
/// 保证「预览里框到哪，窗口里就是哪」。
/// </summary>
public static class BackgroundRenderer
{
    public const string ModeCover = "cover";
    public const string ModeContain = "contain";
    public const string ModeStretch = "stretch";

    /// <summary>图片在画布上的实际落点。</summary>
    public readonly record struct Layout(float X, float Y, float W, float H);

    public static Layout Compute(Image src, int width, int height, AppConfig cfg)
    {
        var w = Math.Max(1, width);
        var h = Math.Max(1, height);
        var mode = (cfg.BackgroundMode ?? ModeCover).ToLowerInvariant();

        if (mode == ModeStretch) return new Layout(0, 0, w, h);

        float scale;
        if (mode == ModeContain)
        {
            scale = Math.Min((float)w / src.Width, (float)h / src.Height);
        }
        else
        {
            var zoom = Math.Clamp(cfg.BackgroundZoom, 100, 300) / 100f;
            scale = Math.Max((float)w / src.Width, (float)h / src.Height) * zoom;
        }

        var dw = src.Width * scale;
        var dh = src.Height * scale;

        var px = Math.Clamp(cfg.BackgroundPosX, 0, 100) / 100f;
        var py = Math.Clamp(cfg.BackgroundPosY, 0, 100) / 100f;

        return new Layout((w - dw) * px, (h - dh) * py, dw, dh);
    }

    public static Bitmap Render(Image src, int width, int height, AppConfig cfg, bool blur = false)
    {
        var w = Math.Max(1, width);
        var h = Math.Max(1, height);
        var bmp = new Bitmap(w, h);

        var layout = Compute(src, w, h, cfg);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(src, layout.X, layout.Y, layout.W, layout.H);
        }

        return blur ? Blur(bmp, 26) : bmp;
    }

    /// <summary>
    /// 三次 box blur 近似高斯。滑动窗口累加，复杂度 O(w*h)，
    /// 880×680 的图约 100ms，只在背景变动时算一次。
    /// </summary>
    public static Bitmap Blur(Bitmap source, int radius)
    {
        var w = source.Width;
        var h = source.Height;
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);

        using (var g = Graphics.FromImage(bmp))
            g.DrawImage(source, 0, 0, w, h);

        var rect = new Rectangle(0, 0, w, h);
        var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var stride = data.Stride;
            var bytes = new byte[stride * h];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);

            for (var i = 0; i < 3; i++)
            {
                BoxPassHorizontal(bytes, w, h, stride, radius);
                BoxPassVertical(bytes, w, h, stride, radius);
            }

            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return bmp;
    }

    static void BoxPassHorizontal(byte[] buf, int w, int h, int stride, int r)
    {
        var win = r * 2 + 1;
        var line = new byte[w * 4];

        for (var y = 0; y < h; y++)
        {
            var row = y * stride;

            for (var c = 0; c < 4; c++)
            {
                var sum = 0;
                for (var i = -r; i <= r; i++)
                    sum += buf[row + Math.Clamp(i, 0, w - 1) * 4 + c];

                for (var x = 0; x < w; x++)
                {
                    line[x * 4 + c] = (byte)(sum / win);

                    var add = Math.Clamp(x + r + 1, 0, w - 1);
                    var sub = Math.Clamp(x - r, 0, w - 1);
                    sum += buf[row + add * 4 + c] - buf[row + sub * 4 + c];
                }
            }

            Buffer.BlockCopy(line, 0, buf, row, w * 4);
        }
    }

    static void BoxPassVertical(byte[] buf, int w, int h, int stride, int r)
    {
        var win = r * 2 + 1;
        var col = new byte[h * 4];

        for (var x = 0; x < w; x++)
        {
            var offset = x * 4;

            for (var c = 0; c < 4; c++)
            {
                var sum = 0;
                for (var i = -r; i <= r; i++)
                    sum += buf[Math.Clamp(i, 0, h - 1) * stride + offset + c];

                for (var y = 0; y < h; y++)
                {
                    col[y * 4 + c] = (byte)(sum / win);

                    var add = Math.Clamp(y + r + 1, 0, h - 1);
                    var sub = Math.Clamp(y - r, 0, h - 1);
                    sum += buf[add * stride + offset + c] - buf[sub * stride + offset + c];
                }
            }

            for (var y = 0; y < h; y++)
                Buffer.BlockCopy(col, y * 4, buf, y * stride + offset, 4);
        }
    }

    /// <summary>读图并返回独立副本，放掉文件句柄（否则用户改图会失败）。</summary>
    public static Image? LoadSource(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            using var fs = File.OpenRead(path);
            using var tmp = Image.FromStream(fs);
            return new Bitmap(tmp);
        }
        catch { return null; }
    }
}
