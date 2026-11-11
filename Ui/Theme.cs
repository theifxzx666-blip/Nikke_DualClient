using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace NikkeDualClient.Ui;

/// <summary>
/// 默认深色配色取自 NIKKE_GM 登录窗口的实际取色：中性灰黑分三层，强调色稍偏冷的蓝，
/// 不用大渐变，靠不同深浅拉开层次。浅色是同一套层次关系反过来。
/// </summary>
internal static class Theme
{
    public static bool Light { get; private set; }

    public static Color Bg => Light ? Color.FromArgb(244, 245, 247) : Color.FromArgb(20, 21, 24);
    public static Color Surface => Light ? Color.FromArgb(255, 255, 255) : Color.FromArgb(31, 33, 38);
    public static Color SurfaceHi => Light ? Color.FromArgb(235, 237, 240) : Color.FromArgb(40, 43, 50);
    public static Color Sunken => Light ? Color.FromArgb(248, 249, 251) : Color.FromArgb(27, 29, 34);
    public static Color Footer => Light ? Color.FromArgb(238, 240, 243) : Color.FromArgb(17, 18, 21);
    public static Color Border => Light ? Color.FromArgb(222, 225, 230) : Color.FromArgb(48, 51, 58);
    public static Color BorderSoft => Light ? Color.FromArgb(234, 236, 240) : Color.FromArgb(38, 41, 47);

    public static Color Text => Light ? Color.FromArgb(24, 28, 34) : Color.FromArgb(240, 242, 245);
    public static Color TextDim => Light ? Color.FromArgb(84, 92, 102) : Color.FromArgb(160, 165, 175);
    public static Color TextFaint => Light ? Color.FromArgb(140, 147, 157) : Color.FromArgb(112, 118, 128);

    public static Color Accent => Color.FromArgb(47, 111, 208);
    public static Color AccentHover => Color.FromArgb(59, 125, 224);

    public static Color Good => Light ? Color.FromArgb(46, 158, 107) : Color.FromArgb(76, 175, 125);
    public static Color Warn => Light ? Color.FromArgb(201, 138, 24) : Color.FromArgb(217, 164, 65);
    public static Color Bad => Light ? Color.FromArgb(208, 69, 63) : Color.FromArgb(214, 90, 90);
    public static Color Info => Light ? Color.FromArgb(47, 111, 208) : Color.FromArgb(120, 170, 240);

    /// <summary>半透明卡片底色：深色盖黑，浅色盖白。</summary>
    public static Color Tint(int alpha) => Light
        ? Color.FromArgb(alpha, 255, 255, 255)
        : Color.FromArgb(alpha, 32, 32, 32);

    /// <summary>
    /// 卡片实底（不透明）。
    ///
    /// 半透明卡片配自定义壁纸是踩过的坑：alpha 再高，照片的花纹也会从卡面透出来，
    /// 浅色主题下 65% 白压暗只能把照片压成"浅粉"，深色正文照样糊在花纹上。
    /// 所以卡片一律给不透明色，背景只当窗口边缘的氛围层，不参与可读性。
    /// </summary>
    public static Color Card => Light ? Color.FromArgb(252, 253, 254) : Color.FromArgb(28, 30, 35);

    /// <summary>比卡片亮一档，用于表头、输入框底等需要区分层次的地方。</summary>
    public static Color CardHi => Light ? Color.FromArgb(244, 246, 249) : Color.FromArgb(35, 38, 44);

    public static Font Ui(float size, FontStyle style = FontStyle.Regular) =>
        new("Microsoft YaHei UI", size, style);

    public static Font Mono(float size = 8.5f) => new("Consolas", size);

    /// <summary>圆角矩形路径，各处自绘共用。</summary>
    public static GraphicsPath RoundPath(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0) { path.AddRectangle(r); return path; }

        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { path.AddRectangle(r); return path; }

        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>整体提亮/压暗，用来做按钮的上下微渐变。</summary>
    public static Color Shade(Color c, int delta) => Color.FromArgb(
        c.A,
        Math.Clamp(c.R + delta, 0, 255),
        Math.Clamp(c.G + delta, 0, 255),
        Math.Clamp(c.B + delta, 0, 255));

    public static void Apply(string mode)
    {
        Light = mode switch
        {
            "light" => true,
            "system" => !SystemPrefersDark(),
            _ => false
        };
    }

    /// <summary>AppsUseLightTheme = 0 表示系统用的是深色。</summary>
    static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int v) return v == 0;
        }
        catch { }
        return true;
    }
}

/// <summary>
/// 圆角按钮。用上下 8 级的微渐变代替纯色，视觉上有厚度但仍属扁平；
/// 再叠一圈 1px 内描边，避免大色块发闷。
/// </summary>
internal sealed class RoundButton : Button
{
    public int Radius { get; set; } = 8;
    public Color? FillColor { get; set; }
    public Color? HoverColor { get; set; }
    public Color? DisabledColor { get; set; }
    /// <summary>次要按钮用：边缘用主题边框色，和主按钮拉开层级。</summary>
    public bool Outlined { get; set; }

    bool _hover;

    public RoundButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        ForeColor = Color.White;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        // 让父控件把背景画进来。圆角边缘的半透明像素要和真实底色混合，
        // 否则 GDI+ 会拿未初始化像素去混，四角就出现暗角。
        base.OnPaintBackground(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // 父容器是纯色时补铺一层（Panel 的情况）
        var bg = Parent?.BackColor ?? Color.Transparent;
        if (bg.A == 255)
        {
            using var bgBrush = new SolidBrush(bg);
            g.FillRectangle(bgBrush, ClientRectangle);
        }

        var baseColor = !Enabled
            ? (DisabledColor ?? Theme.SurfaceHi)
            : _hover
                ? (HoverColor ?? Theme.Shade(FillColor ?? Theme.Accent, 12))
                : (FillColor ?? Theme.Accent);

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundPath(rect, Radius);

        // 只有主按钮（没显式指定填充色）用渐变。次要按钮和背景同色时，
        // 渐变底边会比背景暗一档，看起来像一条接缝，所以走纯色。
        if (FillColor is null && Enabled)
        {
            using var brush = new LinearGradientBrush(
                new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height)),
                Theme.Shade(baseColor, 8), Theme.Shade(baseColor, -8), 90f);
            g.FillPath(brush, path);
        }
        else
        {
            using var brush = new SolidBrush(baseColor);
            g.FillPath(brush, path);
        }

        var edge = Outlined
            ? Theme.Border
            : Color.FromArgb(34, 255, 255, 255);
        using (var pen = new Pen(edge))
            g.DrawPath(pen, path);

        var fore = Enabled ? ForeColor : Theme.TextFaint;
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

/// <summary>卡片：圆角 + 半透明 + 可选细边。圆角靠 Region 裁，子控件也被收进圆角内。</summary>
internal sealed class GlassPanel : Panel
{
    Color _tint = Color.FromArgb(215, 26, 26, 26);
    int _radius = 12;

    public Color Tint
    {
        get => _tint;
        set { _tint = value; Invalidate(); }
    }

    public int Radius
    {
        get => _radius;
        set { _radius = value; ApplyRegion(); Invalidate(); }
    }

    public bool DrawBorder { get; set; }
    public Color BorderColor { get; set; } = Theme.BorderSoft;

    public GlassPanel()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor
                 | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint, true);
        BackColor = Color.Transparent;
    }

    void ApplyRegion()
    {
        if (_radius <= 0 || Width <= 0 || Height <= 0) { Region = null; return; }
        using var path = Theme.RoundPath(new Rectangle(0, 0, Width, Height), _radius);
        Region = new Region(path);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ApplyRegion();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundPath(rect, _radius);

        if (_tint.A > 0)
        {
            using var brush = new SolidBrush(_tint);
            g.FillPath(brush, path);
        }
        if (DrawBorder)
        {
            using var pen = new Pen(BorderColor);
            g.DrawPath(pen, path);
        }
    }
}

/// <summary>
/// 设置页左侧导航项。选中靠“左侧竖条 + 淡色底”，不用整块纯色 ——
/// 纯色块和同色背景之间会有 1px 抗锯齿接缝。
/// </summary>
internal sealed class NavItem : Control
{
    public bool Selected { get; set; }

    bool _hover;

    public NavItem()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Height = 40;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    public void SetSelected(bool value) { Selected = value; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (Selected || _hover)
        {
            var fill = Selected
                ? Color.FromArgb(Theme.Light ? 26 : 46, Theme.Accent)
                : Theme.SurfaceHi;
            using var path = Theme.RoundPath(new Rectangle(0, 0, Width - 1, Height - 1), 8);
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);
        }

        if (Selected)
        {
            using var bar = new SolidBrush(Theme.Accent);
            g.FillRectangle(bar, 0, 9, 3, Height - 18);
        }

        using var font = Theme.Ui(9.5f);
        TextRenderer.DrawText(g, Text, font,
            new Rectangle(18, 0, Math.Max(1, Width - 24), Height),
            Selected ? Theme.Accent : Theme.TextDim,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

/// <summary>标题栏右侧的图标按钮（齿轮），用 Segoe MDL2 字形，不额外带图片资源。</summary>
internal sealed class IconButton : Control
{
    public string Glyph { get; set; } = "\uE713";
    public float GlyphSize { get; set; } = 12f;
    bool _hover;

    public IconButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Size = new Size(34, 34);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaintBackground(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var bg = Parent?.BackColor ?? Color.Transparent;
        if (bg.A == 255)
        {
            using var bgBrush = new SolidBrush(bg);
            g.FillRectangle(bgBrush, ClientRectangle);
        }

        if (_hover)
        {
            using var path = Theme.RoundPath(new Rectangle(0, 0, Width - 1, Height - 1), 8);
            using var brush = new SolidBrush(Theme.SurfaceHi);
            g.FillPath(brush, path);
        }

        using var font = new Font("Segoe MDL2 Assets", GlyphSize);
        TextRenderer.DrawText(g, Glyph, font, ClientRectangle,
            _hover ? Theme.Text : Theme.TextDim,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}
