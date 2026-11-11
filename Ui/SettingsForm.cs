using System.Text;
using NikkeDualClient.Core;

namespace NikkeDualClient.Ui;

/// <summary>
/// 设置窗口：外观（背景裁切）/ 路径 / 界面 / 关于。
/// 内部改的是 AppConfig 副本，点确定才写回，点取消等于没动过。
/// </summary>
public sealed class SettingsForm : Form
{
    readonly AppConfig _target;
    readonly AppConfig _work;

    readonly List<NavItem> _nav = new();
    readonly List<Control> _pages = new();
    int _current = -1;

    // 外观页
    CropCanvas _canvas = null!;
    Label _lblInfo = null!, _lblDim = null!;
    TrackBar _trkDim = null!;
    RadioButton _rbCover = null!, _rbContain = null!, _rbStretch = null!;
    Image? _src;

    // 路径页
    TextBox _txtOfficial = null!, _txtPrivate = null!;
    Label _lblPathCheck = null!;

    // 维护页
    Label _lblRepairResult = null!;

    // 界面页
    RadioButton _rbDark = null!, _rbLight = null!, _rbSystem = null!;

    public SettingsForm(AppConfig cfg, int startPage = 0)
    {
        _target = cfg;
        _work = cfg.Clone();
        Icon = AppIcon.Load();
        BuildUi();
        LoadSource();
        SetMode(_work.BackgroundMode ?? BackgroundRenderer.ModeCover);
        Navigate(Math.Clamp(startPage, 0, 4));
    }

    // ---------------------------------------------------------------- 骨架

    void BuildUi()
    {
        Text = "设置";
        ClientSize = new Size(780, 646);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Ui(9f);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Theme.Bg
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        root.Controls.Add(BuildNav(), 0, 0);
        root.Controls.Add(BuildContent(), 1, 0);
        root.Controls.Add(BuildFooter(), 0, 1);
        root.SetColumnSpan(root.GetControlFromPosition(0, 1)!, 2);

        Controls.Add(root);
    }

    Control BuildNav()
    {
        var nav = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };

        var title = new Label
        {
            Text = "设置",
            Font = Theme.Ui(11f, FontStyle.Bold),
            ForeColor = Theme.Text,
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(16, 18)
        };
        nav.Controls.Add(title);

        var names = new[] { "外观", "路径", "界面", "维护", "关于" };
        for (var i = 0; i < names.Length; i++)
        {
            var b = new NavItem
            {
                Text = names[i],
                Size = new Size(118, 40),
                Location = new Point(10, 58 + i * 48)
            };
            var idx = i;
            b.Click += (_, _) => Navigate(idx);
            _nav.Add(b);
            nav.Controls.Add(b);
        }
        return nav;
    }

    Control BuildContent()
    {
        var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(18, 14, 18, 8) };

        _pages.Add(BuildAppearancePage());
        _pages.Add(BuildPathPage());
        _pages.Add(BuildThemePage());
        _pages.Add(BuildMaintenancePage());
        _pages.Add(BuildAboutPage());

        foreach (var p in _pages)
        {
            p.Dock = DockStyle.Fill;
            p.Visible = false;
            host.Controls.Add(p);
        }
        return host;
    }

    Control BuildFooter()
    {
        var bar = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Footer };

        var btnOk = new RoundButton
        {
            Text = "确定",
            Size = new Size(96, 36),
            Font = Theme.Ui(9.5f),
            Location = new Point(0, 11),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        btnOk.Click += (_, _) => Commit();

        var btnCancel = new RoundButton
        {
            Text = "取消",
            Size = new Size(96, 36),
            FillColor = Theme.SurfaceHi,
            HoverColor = Theme.Border,
            ForeColor = Theme.TextDim,
            Font = Theme.Ui(9.5f),
            Location = new Point(0, 11),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        bar.Controls.Add(btnOk);
        bar.Controls.Add(btnCancel);
        bar.Resize += (_, _) =>
        {
            btnCancel.Left = bar.Width - 18 - btnCancel.Width;
            btnOk.Left = btnCancel.Left - 12 - btnOk.Width;
        };
        return bar;
    }

    void Navigate(int idx)
    {
        if (_current == idx) return;
        _current = idx;

        for (var i = 0; i < _nav.Count; i++)
            _nav[i].SetSelected(i == idx);

        // 先全部收掉再显示目标页，避免两页同时可见的那一帧闪一下
        SuspendLayout();
        foreach (var page in _pages)
            if (page.Visible) page.Visible = false;

        var target = _pages[idx];
        target.Visible = true;
        target.BringToFront();
        ResumeLayout(true);
    }

    /// <summary>WS_EX_COMPOSITED：整窗双缓冲。透明 Panel 切页时靠这个压住闪烁。</summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x02000000;
            return cp;
        }
    }

    // ---------------------------------------------------------------- 外观页

    Control BuildAppearancePage()
    {
        var page = new Panel { BackColor = Color.Transparent };

        _canvas = new CropCanvas
        {
            Location = new Point(0, 0),
            Size = new Size(480, 384)
        };

        _lblInfo = MakeLabel("", 0, 392, Theme.TextFaint, Theme.Ui(8.5f));

        var btnPick = new RoundButton
        {
            Text = "选择图片",
            Size = new Size(104, 32),
            Font = Theme.Ui(9f),
            Location = new Point(0, 416)
        };
        btnPick.Click += (_, _) => PickImage();

        var btnClear = new RoundButton
        {
            Text = "清除背景",
            Size = new Size(104, 32),
            FillColor = Theme.SurfaceHi,
            HoverColor = Theme.Border,
            ForeColor = Theme.TextDim,
            Font = Theme.Ui(9f),
            Location = new Point(114, 416)
        };
        btnClear.Click += (_, _) =>
        {
            _work.BackgroundImage = null;
            LoadSource();
        };

        var btnReset = new RoundButton
        {
            Text = "复位",
            Size = new Size(104, 32),
            FillColor = Theme.SurfaceHi,
            HoverColor = Theme.Border,
            ForeColor = Theme.TextDim,
            Font = Theme.Ui(9f),
            Location = new Point(228, 416)
        };
        btnReset.Click += (_, _) =>
        {
            _work.BackgroundPosX = 50;
            _work.BackgroundPosY = 50;
            _work.BackgroundZoom = 100;
            _canvas.Refresh_();
        };

        // 显示方式用单选而不是下拉：系统 ComboBox 的下拉箭头在深色下是白块，很难看
        page.Controls.Add(MakeLabel("显示方式", 0, 464, Theme.TextDim, Theme.Ui(8.5f)));
        _rbCover = MakeRadio("铺满裁切", 68, 462);
        _rbContain = MakeRadio("等比留边", 176, 462);
        _rbStretch = MakeRadio("拉伸", 284, 462);

        var mode = (_work.BackgroundMode ?? BackgroundRenderer.ModeCover).ToLowerInvariant();
        _rbCover.Checked = mode == BackgroundRenderer.ModeCover;
        _rbContain.Checked = mode == BackgroundRenderer.ModeContain;
        _rbStretch.Checked = mode == BackgroundRenderer.ModeStretch;

        _rbCover.CheckedChanged += (_, _) => { if (_rbCover.Checked) SetMode(BackgroundRenderer.ModeCover); };
        _rbContain.CheckedChanged += (_, _) => { if (_rbContain.Checked) SetMode(BackgroundRenderer.ModeContain); };
        _rbStretch.CheckedChanged += (_, _) => { if (_rbStretch.Checked) SetMode(BackgroundRenderer.ModeStretch); };

        _lblDim = MakeLabel("压暗 65%", 0, 504, Theme.TextDim, Theme.Ui(8.5f));
        _trkDim = MakeSlider(0, 522, 0, 95, _work.BackgroundDim, v =>
        {
            _work.BackgroundDim = v;
            _lblDim.Text = $"压暗 {v}%";
            _canvas.Refresh_();
        });

        page.Controls.AddRange(new Control[]
        {
            _canvas, _lblInfo, btnPick, btnClear, btnReset,
            _rbCover, _rbContain, _rbStretch,
            _lblDim, _trkDim,
            MakeLabel("拖动平移", 500, 12, Theme.TextFaint, Theme.Ui(8.5f)),
            MakeLabel("滚轮缩放", 500, 38, Theme.TextFaint, Theme.Ui(8.5f)),
            MakeLabel("双击复位", 500, 64, Theme.TextFaint, Theme.Ui(8.5f))
        });
        return page;
    }

    void SetMode(string mode)
    {
        _work.BackgroundMode = mode;
        _canvas.Refresh_();
    }

    Label MakeLabel(string text, int x, int y, Color color, Font font) => new()
    {
        Text = text,
        Location = new Point(x, y),
        AutoSize = true,
        ForeColor = color,
        BackColor = Color.Transparent,
        Font = font
    };

    TrackBar MakeSlider(int x, int y, int min, int max, int value, Action<int> onChange)
    {
        var trk = new TrackBar
        {
            Location = new Point(x, y),
            Size = new Size(240, 34),
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            TickStyle = TickStyle.None,
            BackColor = Theme.Bg
        };
        trk.ValueChanged += (_, _) => onChange(trk.Value);
        return trk;
    }

    void PickImage()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "选择背景图",
            Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.webp|所有文件|*.*"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _work.BackgroundImage = dlg.FileName;
        LoadSource();
    }

    void LoadSource()
    {
        _src?.Dispose();
        _src = BackgroundRenderer.LoadSource(_work.BackgroundImage);

        _canvas.Bind(_work, _src, UpdateInfo);
        UpdateInfo();
    }

    void UpdateInfo()
    {
        _lblInfo.Text = _src is null
            ? "未设置背景，使用纯色"
            : $"{Path.GetFileName(_work.BackgroundImage)}   {_src.Width} × {_src.Height}   ·   缩放 {_work.BackgroundZoom}%";
    }

    // ---------------------------------------------------------------- 路径页

    Control BuildPathPage()
    {
        var page = new Panel { BackColor = Color.Transparent };

        page.Controls.Add(MakeLabel("官服根目录", 0, 4, Theme.Text, Theme.Ui(9.5f)));
        page.Controls.Add(MakeLabel("含 NIKKE\\Launcher 的那一层", 0, 26, Theme.TextFaint, Theme.Ui(8f)));

        _txtOfficial = MakePathBox(0, 46);
        page.Controls.Add(_txtOfficial);
        page.Controls.Add(MakeBrowseButton(468, 46, () =>
        {
            var p = PickFolder("选择官服根目录");
            if (p is not null) { _txtOfficial.Text = p; RefreshPathCheck(); }
        }));

        page.Controls.Add(MakeLabel("私服根目录", 0, 108, Theme.Text, Theme.Ui(9.5f)));
        page.Controls.Add(MakeLabel("含 EpinelPS 与 NIKKEG 的那一层", 0, 130, Theme.TextFaint, Theme.Ui(8f)));

        _txtPrivate = MakePathBox(0, 150);
        page.Controls.Add(_txtPrivate);
        page.Controls.Add(MakeBrowseButton(468, 150, () =>
        {
            var p = PickFolder("选择私服根目录");
            if (p is not null) { _txtPrivate.Text = p; RefreshPathCheck(); }
        }));

        _lblPathCheck = new Label
        {
            Location = new Point(0, 196),
            Size = new Size(560, 84),
            ForeColor = Theme.TextDim,
            BackColor = Color.Transparent,
            Font = Theme.Mono(8.5f)
        };
        page.Controls.Add(_lblPathCheck);

        var btnScan = new RoundButton
        {
            Text = "自动扫描",
            Size = new Size(104, 32),
            FillColor = Theme.SurfaceHi,
            HoverColor = Theme.Border,
            ForeColor = Theme.TextDim,
            Font = Theme.Ui(9f),
            Location = new Point(0, 296)
        };
        btnScan.Click += (_, _) =>
        {
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                var found = Locator.Scan();
                if (found.PrivateRoot is not null) _txtPrivate.Text = found.PrivateRoot;
                if (found.OfficialRoot is not null) _txtOfficial.Text = found.OfficialRoot;
                RefreshPathCheck();
            }
            finally { Cursor.Current = Cursors.Default; }
        };
        page.Controls.Add(btnScan);

        _txtOfficial.Text = _work.OfficialRoot ?? "";
        _txtPrivate.Text = _work.PrivateRoot ?? "";
        _txtOfficial.TextChanged += (_, _) => _work.OfficialRoot = Null(_txtOfficial.Text);
        _txtPrivate.TextChanged += (_, _) => _work.PrivateRoot = Null(_txtPrivate.Text);

        RefreshPathCheck();
        return page;
    }

    static string? Null(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    TextBox MakePathBox(int x, int y) => new()
    {
        Location = new Point(x, y),
        Size = new Size(456, 26),
        BackColor = Theme.Sunken,
        ForeColor = Theme.Text,
        BorderStyle = BorderStyle.FixedSingle,
        Font = Theme.Mono(9f)
    };

    static RoundButton MakeBrowseButton(int x, int y, Action onClick)
    {
        var b = new RoundButton
        {
            Text = "浏览",
            Size = new Size(88, 26),
            FillColor = Theme.SurfaceHi,
            HoverColor = Theme.Border,
            ForeColor = Theme.TextDim,
            Font = Theme.Ui(8.5f),
            Location = new Point(x, y)
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    string? PickFolder(string title)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = title,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            RootFolder = Environment.SpecialFolder.MyComputer
        };
        return dlg.ShowDialog(this) == DialogResult.OK ? dlg.SelectedPath : null;
    }

    /// <summary>把配置里那几个相对路径逐个落到磁盘上验证，缺哪个一眼看到。</summary>
    void RefreshPathCheck()
    {
        var lines = new List<string>();

        void Check(string label, string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) { lines.Add($"  × {label}  未设置"); return; }
            lines.Add(File.Exists(path) ? $"  √ {label}" : $"  × {label}  找不到：{path}");
        }

        Check(@"官服 NIKKE\Launcher\nikke_launcher.exe", _work.OfficialLauncherPath);
        Check(@"私服 EpinelPS\EpinelPS.exe", _work.ServerExePath);
        Check(@"私服 NIKKEG\Launcher\nikke_launcher.exe", _work.PrivateLauncherPath);

        _lblPathCheck.Text = string.Join(Environment.NewLine, lines);
        _lblPathCheck.ForeColor = lines.Any(l => l.Contains('×')) ? Theme.Warn : Theme.Good;
    }

    // ---------------------------------------------------------------- 界面页

    Control BuildThemePage()
    {
        var page = new Panel { BackColor = Color.Transparent };

        page.Controls.Add(MakeLabel("主题", 0, 4, Theme.Text, Theme.Ui(9.5f)));

        _rbDark = MakeRadio("深色", 0, 36);
        _rbLight = MakeRadio("浅色", 0, 66);
        _rbSystem = MakeRadio("跟随系统", 0, 96);

        switch ((_work.ThemeMode ?? "light").ToLowerInvariant())
        {
            case "light": _rbLight.Checked = true; break;
            case "system": _rbSystem.Checked = true; break;
            default: _rbDark.Checked = true; break;
        }

        _rbDark.CheckedChanged += (_, _) => { if (_rbDark.Checked) _work.ThemeMode = "dark"; };
        _rbLight.CheckedChanged += (_, _) => { if (_rbLight.Checked) _work.ThemeMode = "light"; };
        _rbSystem.CheckedChanged += (_, _) => { if (_rbSystem.Checked) _work.ThemeMode = "system"; };

        page.Controls.AddRange(new Control[] { _rbDark, _rbLight, _rbSystem });
        return page;
    }

    RadioButton MakeRadio(string text, int x, int y) => new()
    {
        Text = text,
        Location = new Point(x, y),
        AutoSize = true,
        ForeColor = Theme.Text,
        BackColor = Color.Transparent,
        Font = Theme.Ui(9f),
        FlatStyle = FlatStyle.Flat
    };

    // ---------------------------------------------------------------- 关于页

    // ---------------------------------------------------------------- 维护页

    Control BuildMaintenancePage()
    {
        var page = new Panel { BackColor = Color.Transparent };

        page.Controls.Add(MakeLabel("修复新闻页", 0, 0, Theme.Text, Theme.Ui(10f, FontStyle.Bold)));
        page.Controls.Add(WrapLabel(
            "把启动器新闻页里的卖家广告换回官方内容，并清掉启动器缓存。\r\n" +
            "启动单机时会自动做一次；这里是手动触发，不用非得重启整套。", 0, 26));

        var btnRepair = new RoundButton
        {
            Text = "立即修复",
            Size = new Size(112, 36),
            Font = Theme.Ui(9.5f),
            Location = new Point(0, 96)
        };
        btnRepair.Click += (_, _) => RunRepair();
        page.Controls.Add(btnRepair);

        _lblRepairResult = new Label
        {
            Location = new Point(126, 106),
            AutoSize = true,
            ForeColor = Theme.TextFaint,
            BackColor = Color.Transparent,
            Font = Theme.Ui(8.5f),
            Text = ""
        };
        page.Controls.Add(_lblRepairResult);

        page.Controls.Add(MakeLabel("打开目录", 0, 148, Theme.Text, Theme.Ui(10f, FontStyle.Bold)));

        var btnBackup = new RoundButton
        {
            Text = "备份目录",
            Size = new Size(112, 36),
            FillColor = Theme.SurfaceHi,
            HoverColor = Theme.Border,
            Outlined = true,
            ForeColor = Theme.Text,
            Font = Theme.Ui(9.5f),
            Location = new Point(0, 176)
        };
        btnBackup.Click += (_, _) => OpenFolder(Path.Combine(ConfigStore.DataDir, "backup"));
        page.Controls.Add(btnBackup);

        var btnLog = new RoundButton
        {
            Text = "服务日志",
            Size = new Size(112, 36),
            FillColor = Theme.SurfaceHi,
            HoverColor = Theme.Border,
            Outlined = true,
            ForeColor = Theme.Text,
            Font = Theme.Ui(9.5f),
            Location = new Point(126, 176)
        };
        btnLog.Click += (_, _) =>
        {
            var root = _work.PrivateRoot;
            if (root is not null) OpenFolder(Path.Combine(root, "EpinelPS", "logs"));
        };
        page.Controls.Add(btnLog);

        page.Controls.Add(MakeLabel("排查问题", 0, 228, Theme.Text, Theme.Ui(10f, FontStyle.Bold)));

        var btnExport = new RoundButton
        {
            Text = "导出日志",
            Size = new Size(112, 36),
            Font = Theme.Ui(9.5f),
            Location = new Point(0, 256)
        };
        btnExport.Click += (_, _) => ExportLog();
        page.Controls.Add(btnExport);

        page.Controls.Add(WrapLabel(
            "把版本、系统、路径、环境状态和本次运行日志打成一个 txt。\r\n" +
            "反馈问题时直接发这个文件就行。", 0, 302));

        page.Controls.Add(WrapLabel(
            "修复不修改游戏文件，只动启动器的网页和缓存，原文会备份到 backup 目录。", 0, 356));

        return page;
    }

    /// <summary>限宽自动换行的说明文字，避免长句把下面的控件盖住。</summary>
    Label WrapLabel(string text, int x, int y) => new()
    {
        Text = text,
        Location = new Point(x, y),
        AutoSize = true,
        MaximumSize = new Size(540, 0),
        ForeColor = Theme.TextFaint,
        BackColor = Color.Transparent,
        Font = Theme.Ui(8.5f)
    };

    void RunRepair()
    {
        // 启动器开着的时候缓存文件被占用，移不走也删不掉。
        // 只认游戏客户端，私服目录里那些第三方启动器不算。
        var busy = ProcessScanner.FindClients(_work);

        if (busy.Count > 0)
        {
            _lblRepairResult.ForeColor = Theme.Warn;
            _lblRepairResult.Text = "游戏或启动器还开着，先退出再修复";
            return;
        }

        Cursor.Current = Cursors.WaitCursor;
        try
        {
            var r = NewsRepair.Repair(_work);
            _lblRepairResult.ForeColor = Theme.Good;
            _lblRepairResult.Text = $"已修复，{r.Actions.Count} 项";
            AppLog.Add($"维护页手动修复新闻页：{r.Actions.Count} 项");
        }
        catch (Exception ex)
        {
            _lblRepairResult.ForeColor = Theme.Bad;
            _lblRepairResult.Text = "失败：" + ex.Message;
            AppLog.Add("维护页修复失败：" + ex.Message);
        }
        finally { Cursor.Current = Cursors.Default; }
    }

    void ExportLog()
    {
        using var dlg = new SaveFileDialog
        {
            Title = "导出日志",
            FileName = $"NikkeDualClient_日志_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            Filter = "文本文件|*.txt",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            File.WriteAllText(dlg.FileName, Diagnostics.BuildReport(_work), new UTF8Encoding(true));
            MessageBox.Show(this, "日志已导出：\r\n" + dlg.FileName, "导出成功",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导出失败：" + ex.Message, "导出日志",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    static void OpenFolder(string path)
    {
        try
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch { }
    }

    Control BuildAboutPage()
    {
        var page = new Panel { BackColor = Color.Transparent };

        var title = new Label
        {
            Text = "NIKKE 双服切换",
            Location = new Point(0, 0),
            AutoSize = true,
            ForeColor = Theme.Text,
            BackColor = Color.Transparent,
            Font = Theme.Ui(15f, FontStyle.Bold)
        };

        var body = new Label
        {
            Location = new Point(0, 36),
            Size = new Size(545, 400),
            ForeColor = Theme.TextDim,
            BackColor = Color.Transparent,
            Font = Theme.Ui(9f),
            Text =
$@"版本    {Application.ProductVersion.Split('+')[0]}
作者    {_work.Author}

这个工具只做环境切换：改 hosts、装/卸根证书、起停本地服务、清理启动器新闻页。
不修改游戏文件，不读写存档，不注入进程。

切换前请把游戏与启动器完全退出。工具不会强杀客户端进程 ——
正在下载更新或存进度时被杀会出问题。

配置文件    NikkeDualClient.json
备份目录    backup\"
        };

        page.Controls.Add(title);
        page.Controls.Add(body);
        return page;
    }

    // ---------------------------------------------------------------- 提交

    void Commit()
    {
        _target.BackgroundImage = _work.BackgroundImage;
        _target.BackgroundDim = _work.BackgroundDim;
        _target.BackgroundMode = _work.BackgroundMode;
        _target.BackgroundPosX = _work.BackgroundPosX;
        _target.BackgroundPosY = _work.BackgroundPosY;
        _target.BackgroundZoom = _work.BackgroundZoom;
        _target.ThemeMode = _work.ThemeMode;
        _target.OfficialRoot = _work.OfficialRoot;
        _target.PrivateRoot = _work.PrivateRoot;

        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.SetDarkTitleBar(Handle, !Theme.Light);
    }
}
