using System.Drawing.Drawing2D;
using NikkeDualClient.Core;

namespace NikkeDualClient.Ui;

/// <summary>
/// 背景裁切画布。画布比例和主窗口一致，看到的就是最终效果：
///   拖动 = 平移图片
///   滚轮 = 缩放
///   双击 = 复位
/// 比原来的三个滑块直观，也不用算百分比。
/// </summary>
internal sealed class CropCanvas : Control
{
    AppConfig? _cfg;
    Image? _src;
    Action? _changed;

    Point _dragFrom;
    int _posX0, _posY0;
    bool _dragging;
    bool _hover;

    public CropCanvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Sunken;
        Cursor = Cursors.Default;
    }

    public void Bind(AppConfig cfg, Image? src, Action changed)
    {
        _cfg = cfg;
        _src = src;
        _changed = changed;
        Invalidate();
    }

    public void Refresh_() => Invalidate();

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Sunken);

        if (_src is null || _cfg is null)
        {
            using var f = Theme.Ui(9f);
            TextRenderer.DrawText(g, "还没有选择背景图", f, ClientRectangle, Theme.TextFaint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        var layout = BackgroundRenderer.Compute(_src, Width, Height, _cfg);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(_src, layout.X, layout.Y, layout.W, layout.H);

        // 叠一层压暗，所见即最终观感
        var dim = Math.Clamp(_cfg.BackgroundDim, 0, 95);
        var baseColor = Theme.Light ? 255 : 26;
        using (var brush = new SolidBrush(Color.FromArgb(dim * 255 / 100, baseColor, baseColor, baseColor)))
            g.FillRectangle(brush, ClientRectangle);

        // 边框提示可操作区域
        using var pen = new Pen(Color.FromArgb(_hover ? 130 : 70, 120, 160, 255), 1);
        g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

        // 右下角用法提示，只在悬停时出现，不干扰看图
        if (_hover)
        {
            var hint = "拖动平移 · 滚轮缩放 · 双击复位";
            using var f = Theme.Ui(8f);
            var size = TextRenderer.MeasureText(hint, f);
            var box = new Rectangle(Width - size.Width - 18, Height - size.Height - 14, size.Width + 12, size.Height + 6);
            using var bg = new SolidBrush(Color.FromArgb(170, 0, 0, 0));
            g.FillRectangle(bg, box);
            TextRenderer.DrawText(g, hint, f, box, Color.FromArgb(220, 230, 240),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Cursor = Cursors.SizeAll; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Cursor = Cursors.Default; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _src is null || _cfg is null) return;
        _dragging = true;
        _dragFrom = e.Location;
        _posX0 = _cfg.BackgroundPosX;
        _posY0 = _cfg.BackgroundPosY;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging || _src is null || _cfg is null) return;

        var layout = BackgroundRenderer.Compute(_src, Width, Height, _cfg);
        var spanX = layout.W - Width;
        var spanY = layout.H - Height;

        // 拖动方向和图片移动方向相反：往右拖，画面跟着往右走
        if (spanX > 1)
        {
            var delta = (e.X - _dragFrom.X) / spanX * 100f;
            _cfg.BackgroundPosX = Math.Clamp((int)Math.Round(_posX0 - delta), 0, 100);
        }
        if (spanY > 1)
        {
            var delta = (e.Y - _dragFrom.Y) / spanY * 100f;
            _cfg.BackgroundPosY = Math.Clamp((int)Math.Round(_posY0 - delta), 0, 100);
        }

        _changed?.Invoke();
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        Capture = false;
        base.OnMouseUp(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (_src is null || _cfg is null) return;

        var next = Math.Clamp(_cfg.BackgroundZoom + (e.Delta > 0 ? 8 : -8), 100, 300);
        if (next == _cfg.BackgroundZoom) return;

        _cfg.BackgroundZoom = next;
        _changed?.Invoke();
        Invalidate();
    }

    protected override void OnDoubleClick(EventArgs e)
    {
        if (_cfg is null) return;
        _cfg.BackgroundPosX = 50;
        _cfg.BackgroundPosY = 50;
        _cfg.BackgroundZoom = 100;
        _changed?.Invoke();
        Invalidate();
        base.OnDoubleClick(e);
    }
}
