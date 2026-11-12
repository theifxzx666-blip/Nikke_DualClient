using NikkeDualClient.Core;

namespace NikkeDualClient.Ui;

public sealed class MainForm : Form
{
    AppConfig _cfg;
    bool _busy;

    GlassPanel _root = null!;
    Panel _statusBar = null!;
    Label _lblMode = null!;
    Label _lblOfficial = null!;
    Label _lblPrivate = null!;
    ListView _list = null!;
    TextBox _log = null!;
    RoundButton _btnPrivate = null!;
    RoundButton _btnOfficial = null!;
    RoundButton _btnReset = null!;

    public MainForm()
    {
        var cfg = ConfigStore.Load();
        if (cfg is null || !Directory.Exists(cfg.PrivateRoot ?? "") || !Directory.Exists(cfg.OfficialRoot ?? ""))
            cfg = ResolveInteractive(this) ?? PlaceholderConfig();
        _cfg = cfg;

        Theme.Apply(cfg.ThemeMode); // 必须在建控件之前，颜色是构建时读的
        Icon = AppIcon.Load();
        BuildUi();
        ApplyBackground();
        RefreshStatus();
        Log("就绪。切换前请确认游戏与启动器已全部退出。");
    }

    // ---------------------------------------------------------------- 首次定位

    static AppConfig? ResolveInteractive(IWin32Window owner)
    {
        Cursor.Current = Cursors.WaitCursor;
        DiscoveryResult found;
        try { found = Locator.Scan(); }
        finally { Cursor.Current = Cursors.Default; }

        var cfg = new AppConfig { OfficialRoot = found.OfficialRoot, PrivateRoot = found.PrivateRoot };

        if (cfg.PrivateRoot is null)
        {
            var picked = PickFolder(owner, "没有自动找到私服目录。请选择私服根目录（含 EpinelPS 与 NIKKEG 的那一层）");
            if (picked is null) return null;
            cfg.PrivateRoot = picked;
        }
        if (cfg.OfficialRoot is null)
        {
            var picked = PickFolder(owner, "请选择官服根目录（含 NIKKE\\Launcher 的那一层）");
            if (picked is null) return null;
            cfg.OfficialRoot = picked;
        }

        ConfigStore.Save(cfg);
        return cfg;
    }

    static string? PickFolder(IWin32Window owner, string description)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            RootFolder = Environment.SpecialFolder.MyComputer
        };
        return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.SelectedPath : null;
    }

    static AppConfig PlaceholderConfig() => new()
    {
        PrivateRoot = @"C:\请重新定位\私服",
        OfficialRoot = @"C:\请重新定位\官服"
    };

    // ---------------------------------------------------------------- 界面

    void BuildUi()
    {
        Text = "NIKKE 双服切换";
        // 高度按内容反推：根内边距 24 + 标题 112 + 路径 114 + 列表 11 行 × 40 + 卡片内边距 44
        // + 日志 104 + 底栏 84 ≈ 922。行高由字高实测得出（见 BuildChecks），
        // 这里留出 11 行全部可见的空间，否则最后一行会被卡片的圆角 Region 裁掉半截。
        ClientSize = new Size(980, 906);
        MinimumSize = new Size(930, 960);

        // 小屏兜底：1080p 带任务栏只剩约 1040，窗口放不下时往下压，
        // 让列表改成滚动，而不是把底栏和日志挤出屏幕（那样按钮点不到）。
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        if (Height > wa.Height - 16)
        {
            ClientSize = new Size(ClientSize.Width, Math.Max(680, wa.Height - 16 - (Height - ClientSize.Height)));
            MinimumSize = new Size(930, Math.Min(960, Height));
        }
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Ui(10f);

        // 整层压暗压在背景图上方；没设背景时就是纯色底
        _root = new GlassPanel { Dock = DockStyle.Fill, Padding = new Padding(14, 12, 14, 12) };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Color.Transparent
        };
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 114));
        // 列表用 Percent 吃满剩余：定死高度（算过 352）实测仍会差几像素出滚动条，
        // 而 Percent 在窗口拉大时还会跟着长。日志给 130 压掉多余空白。
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));

        table.Controls.Add(Card(BuildHeader(), 158), 0, 0);
        table.Controls.Add(Card(BuildPaths(), 182), 0, 1);
        table.Controls.Add(Card(BuildChecks(), 192), 0, 2);
        table.Controls.Add(Card(BuildLog(), 192), 0, 3);
        table.Controls.Add(BuildFooter(), 0, 4);

        _root.Controls.Add(table);
        Controls.Add(_root);
    }

    /// <summary>
    /// 统一卡片：不透明实底 + 14px 圆角 + 1px 描边。
    ///
    /// 之前用半透明（alpha 158~232）配自定义壁纸，正文会被照片花纹吃掉 ——
    /// 浅色主题下深色字压在浅粉纹理上，最难看清。现在改实底，
    /// alpha 参数留着只为不改调用处，实际不再参与绘制。
    /// </summary>
    static Control Card(Control inner, int alpha = 0)
    {
        var card = new GlassPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 10),
            // 底部给 20：卡片靠 Region 裁圆角，padding 只有 14 时
            // 列表最后一行会被圆角内切切掉半截（用户截图实测）
            Padding = new Padding(18, 14, 18, 20),
            Tint = Theme.Card,
            DrawBorder = true,
            BorderColor = Theme.BorderSoft,
            Radius = 14
        };
        inner.Dock = DockStyle.Fill;
        card.Controls.Add(inner);
        return card;
    }

    Control BuildHeader()
    {
        var p = new Panel { BackColor = Color.Transparent };

        // 高度必须由字体实测得出。19pt 粗体的「环境不完整」实测字高 46px，
        // 而之前写死 38 —— Label 会用 TextAlign 居中后把上下各裁掉 4px，
        // 看起来就是「字没显示全」（用户截图里标题上下沿是平的）。
        var titleFont = Theme.Ui(19f, FontStyle.Bold);
        var titleH = TextRenderer.MeasureText("环境不完整", titleFont).Height;

        _lblMode = new Label
        {
            Text = "读取中…",
            Font = titleFont,
            ForeColor = Theme.Text,
            BackColor = Color.Transparent,
            AutoSize = false,
            Height = titleH,
            TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(16, 3)
        };

        // 设置入口放右上角，用齿轮图标；比底部文字链接更符合通用习惯
        var btnSettings = new IconButton
        {
            Size = new Size(36, 36),
            GlyphSize = 13f,
            Location = new Point(0, 0)
        };
        btnSettings.Click += (_, _) => OpenSettings();
        p.Resize += (_, _) => btnSettings.Left = p.Width - btnSettings.Width;

        _statusBar = new Panel
        {
            Location = new Point(0, 2),
            Size = new Size(4, 46),
            BackColor = Theme.Info
        };

        // 标题宽度跟着面板走，避免长文本（含 IP）被截断
        p.Resize += (_, _) =>
        {
            _lblMode.Width = Math.Max(120, p.Width - 80);
        };

        p.Controls.Add(_statusBar);
        p.Controls.Add(_lblMode);
        p.Controls.Add(btnSettings);
        return p;
    }

    Control BuildPaths()
    {
        var p = new Panel { BackColor = Color.Transparent };

        _lblOfficial = new Label
        {
            AutoSize = true,
            ForeColor = Theme.TextDim,
            BackColor = Color.Transparent,
            Font = Theme.Mono(10f),
            Location = new Point(0, 8)
        };
        _lblPrivate = new Label
        {
            AutoSize = true,
            ForeColor = Theme.TextDim,
            BackColor = Color.Transparent,
            Font = Theme.Mono(10f),
            Location = new Point(0, 46)
        };

        var btnPick = new RoundButton
        {
            Text = "重新定位",
            Size = new Size(96, 28),
            Radius = 4,
            FillColor = Theme.SurfaceHi,
            HoverColor = Color.FromArgb(54, 54, 54),
            ForeColor = Theme.TextDim,
            Font = Theme.Ui(9f),
            Location = new Point(690, 16),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        btnPick.Click += (_, _) => Relocate();

        p.Controls.Add(_lblOfficial);
        p.Controls.Add(_lblPrivate);
        p.Controls.Add(btnPick);
        return p;
    }

    Control BuildChecks()
    {
        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HeaderStyle = ColumnHeaderStyle.None,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Sunken,
            ForeColor = Theme.Text,
            Font = Theme.Ui(10.5f),
            MultiSelect = false,
            HideSelection = true,
            OwnerDraw = true
        };
        // ListView 的行高不由 Font 决定，只能靠一个空 SmallImageList 撑。
        // 但字高在不同 DPI / 字体替换下差很多，写死像素值必然在别人的机器上贴成一片
        // （28 在 10pt 下留白只有 12px，用户连报两次「挤」）。改成按实测字高 + 16。
        var glyphH = TextRenderer.MeasureText("国Ag", _list.Font).Height;
        _list.SmallImageList = new ImageList { ImageSize = new Size(1, glyphH + 11) };
        _list.Columns.Add("检查项", 208);
        _list.Columns.Add("状态", 124);
        _list.Columns.Add("说明", 100);

        // 最后一列跟着窗口宽度走。Add 时传 -2 在 OwnerDraw 下不生效，
        // 会留出横向滚动条，反而吃掉可视高度。
        void FitLastColumn()
        {
            var used = _list.Columns[0].Width + _list.Columns[1].Width;
            var rest = _list.ClientSize.Width - used - 4;
            if (rest > 80) _list.Columns[2].Width = rest;
        }
        _list.Resize += (_, _) => FitLastColumn();

        // ListView 不支持透明，只能自绘，否则行底色和卡片对不上
        _list.DrawColumnHeader += (_, e) =>
        {
            using var b = new SolidBrush(Theme.Sunken);
            e.Graphics.FillRectangle(b, e.Bounds);
        };
        _list.DrawItem += (_, _) => { };
        _list.DrawSubItem += (_, e) =>
        {
            var item = e.Item!;
            using (var b = new SolidBrush(Theme.Sunken))
                e.Graphics.FillRectangle(b, e.Bounds);

            var stateColor = item.Tag is Color c ? c : Theme.Text;
            var fore = e.ColumnIndex switch
            {
                0 => Theme.Text,
                1 => stateColor,
                _ => Theme.TextFaint
            };

            var rect = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? string.Empty, Font, rect, fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        };

        return _list;
    }

    Control BuildLog()
    {
        _log = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Theme.Sunken,
            ForeColor = Theme.TextFaint,
            BorderStyle = BorderStyle.None,
            Font = Theme.Mono(9f),
            WordWrap = false
        };
        return _log;
    }

    Control BuildFooter()
    {
        var p = new GlassPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 0, 0),
            Padding = new Padding(18, 14, 18, 14),
            Tint = Theme.Card,
            DrawBorder = true,
            BorderColor = Theme.BorderSoft,
            Radius = 14
        };

        _btnPrivate = new RoundButton
        {
            Text = "启动单机版",
            Size = new Size(204, 46),
            Radius = 10,
            Font = Theme.Ui(11f, FontStyle.Bold),
            Location = new Point(20, 16)
        };
        _btnOfficial = new RoundButton
        {
            Text = "启动官服",
            Size = new Size(156, 46),
            Radius = 10,
            FillColor = Theme.SurfaceHi,
            HoverColor = Theme.Border,
            Outlined = true,
            ForeColor = Theme.Text,
            Font = Theme.Ui(11f),
            Location = new Point(236, 16)
        };
        _btnReset = new RoundButton
        {
            Text = "重置环境",
            Size = new Size(116, 42),
            Radius = 10,
            FillColor = Theme.SurfaceHi,
            HoverColor = Theme.Border,
            Outlined = true,
            ForeColor = Theme.TextDim,
            Font = Theme.Ui(9.5f),
            Location = new Point(20, 18)
        };
        _btnReset.Click += async (_, _) =>
        {
            var ok = MessageBox.Show(this,
                "会把 hosts、私服证书、本地服务全部恢复到官服状态。\r\n正在运行的游戏会断线。\r\n\r\n继续？",
                "重置环境", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (ok != DialogResult.OK) return;
            await Run("重置环境", ResetFlow);
        };

        _btnPrivate.Click += async (_, _) => await Run("启动单机版", log => EnvController.GoPrivate(_cfg, log));
        _btnOfficial.Click += async (_, _) => await Run("启动官服", log => EnvController.GoOfficial(_cfg, log));

        p.Controls.AddRange(new Control[] { _btnPrivate, _btnOfficial, _btnReset });
        p.Resize += (_, _) => _btnReset.Left = p.Width - 20 - _btnReset.Width;
        return p;
    }

    // ---------------------------------------------------------------- 背景

    Image? _bgSource;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.SetDarkTitleBar(Handle, !Theme.Light);
    }

    /// <summary>整窗双缓冲，主题热切换重建控件时不闪。</summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x02000000;
            return cp;
        }
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        RenderBackground();
    }

    void ApplyBackground()
    {
        _bgSource?.Dispose();
        _bgSource = BackgroundRenderer.LoadSource(_cfg.BackgroundImage);

        // 背景只当氛围层，可读性不能靠它。
        // 压暗下限是硬约束：浅色主题 88%、深色 72%。
        // 低于这个值，照片纹理就会和正文抢注意力 —— 用户配的图往往很花。
        var floor = Theme.Light ? 88 : 72;
        var dim = Math.Clamp(Math.Max(_cfg.BackgroundDim, floor), 0, 97);
        var baseColor = Theme.Light ? 250 : 22;
        _root.Tint = Color.FromArgb(dim * 255 / 100, baseColor, baseColor, baseColor);

        RenderBackground();
    }

    /// <summary>
    /// 等比裁剪铺满（cover），不是拉伸 —— 竖版壁纸硬拉到横版窗口会变形。
    /// 只在尺寸确定后生成一次，拖动窗口过程中不重算。
    /// </summary>
    void RenderBackground()
    {
        var old = BackgroundImage;
        BackgroundImage = null;
        old?.Dispose();

        if (_bgSource is null) return;

        // 必须模糊：清晰的照片即使压暗 88%，细节边缘仍会干扰文字。
        // 模糊 + 压暗后它退化成一层色块，这才叫"背景"。
        BackgroundImage = BackgroundRenderer.Render(_bgSource, ClientSize.Width, ClientSize.Height, _cfg, blur: true);
        BackgroundImageLayout = ImageLayout.None; // 尺寸已匹配，不需要再缩放
    }

    void OpenSettings()
    {
        if (_busy) return;

        var themeBefore = _cfg.ThemeMode ?? "light";
        using var dlg = new SettingsForm(_cfg);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        ConfigStore.Save(_cfg);

        // 主题变了就地重建界面，不用重启进程
        if (!string.Equals(themeBefore, _cfg.ThemeMode ?? "light", StringComparison.OrdinalIgnoreCase))
            ReloadTheme();
        else
            ApplyBackground();

        RefreshStatus();
        Log("设置已保存");
    }

    /// <summary>主题热切换：整棵控件树按新配色重建，状态和日志都保留。</summary>
    void ReloadTheme()
    {
        Theme.Apply(_cfg.ThemeMode);
        var logText = _log?.Text ?? string.Empty;

        SuspendLayout();
        foreach (Control c in Controls.Cast<Control>().ToArray()) c.Dispose();
        Controls.Clear();

        BuildUi();
        ApplyBackground();
        ResumeLayout(true);

        RefreshStatus();
        if (logText.Length > 0 && _log is not null)
        {
            _log.Text = logText;
            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
        }
        Native.SetDarkTitleBar(Handle, !Theme.Light);
    }

    // ---------------------------------------------------------------- 状态刷新

    void RefreshStatus()
    {
        EnvStatus s;
        try { s = EnvController.Check(_cfg); }
        catch (Exception ex)
        {
            Log("体检失败：" + ex.Message);
            return;
        }

        _statusBar.BackColor = s.Mode switch { 0 => Theme.Good, 1 => Theme.Info, _ => Theme.Warn };
        _lblMode.Text = s.ModeText;
        _lblMode.ForeColor = s.Mode switch { 0 => Theme.Good, 1 => Theme.Info, _ => Theme.Warn };

        _lblOfficial.Text = "官服    " + (_cfg.OfficialRoot ?? "(未设置)");
        _lblPrivate.Text = "私服    " + (_cfg.PrivateRoot ?? "(未设置)");

        _list.BeginUpdate();
        _list.Items.Clear();
        AddRow("官服目录", s.OfficialRootOk ? "正常" : "缺失", _cfg.OfficialRoot, s.OfficialRootOk ? Theme.Good : Theme.Bad);
        AddRow("私服目录", s.PrivateRootOk ? "正常" : "缺失", _cfg.PrivateRoot, s.PrivateRootOk ? Theme.Good : Theme.Bad);
        AddRow("私服服务端", s.ServerOk ? "正常" : "缺失", _cfg.ServerExePath, s.ServerOk ? Theme.Good : Theme.Bad);
        AddRow("启动器", s.OfficialLauncherOk && s.PrivateLauncherOk ? "正常" : "缺失",
               $"官服 {(s.OfficialLauncherOk ? "√" : "×")}   私服 {(s.PrivateLauncherOk ? "√" : "×")}",
               s.OfficialLauncherOk && s.PrivateLauncherOk ? Theme.Good : Theme.Bad);
        AddRow("hosts 私服区块", s.HostsBlock ? "存在" : "无",
               s.HostsBlock ? $"{_cfg.PrivateHosts.Length} 个域名指向 127.0.0.1" : "官服基线",
               s.HostsBlock ? Theme.Info : Theme.TextFaint);
        AddRow("官方 lobby 入口", s.OfficialLobby == 0 ? "无" : $"{s.OfficialLobby} 条",
               s.OfficialLobby == 0
                   ? "点「启动官服」时探测候选 IP 并写入"
                   : $"当前走 {s.LobbyIp}",
               s.OfficialLobby == 0 ? Theme.Warn : Theme.Good);
        AddRow("新闻域名", s.NewsHijacked ? "被劫持" : "正常",
               s.NewsHijacked ? "na-community 仍指向本机，会显示卖家页" : "na-community 未指向本机",
               s.NewsHijacked ? Theme.Bad : Theme.Good);
        AddRow("私服根证书", s.CaInstalled ? "已安装" : "未安装", _cfg.CaThumbprint, s.CaInstalled ? Theme.Info : Theme.TextFaint);
        AddRow("私服端口", s.PortOpen ? "监听中" : "未监听", $"127.0.0.1:{_cfg.ServerPort}", s.PortOpen ? Theme.Good : Theme.TextFaint);
        AddRow("新闻页", s.NewsPolluted ? "含推广" : "干净",
               s.NewsPolluted ? "命中淘宝 / 毛哥 / 9.png / QQ群" : "无卖家信息",
               s.NewsPolluted ? Theme.Bad : Theme.Good);

        // 服务是工具自己管的，第三方启动器不拦，这一行只列真正要用户关的客户端
        var clients = s.Processes
            .Where(p => !string.Equals(p.Path, _cfg.ServerExePath, StringComparison.OrdinalIgnoreCase)
                        && ProcessScanner.IsClient(p.Name))
            .ToList();
        // 与拦截弹窗同一套收敛：启动器在跑时只看主进程，不然这里会写「9 个」而弹窗只列 1 个
        var mustClose = ProcessScanner.MustClose(clients);
        // 说明列宽度有限，进程名拼多了必然被省略号截断。
        // 只列前 2 个够判断"这是不是我要关的客户端"，全量放 ToolTip。
        var clientText = mustClose.Count == 0
            ? "可以切换"
            : string.Join("、", mustClose.Take(2).Select(p => $"{p.Name}({p.Id})"))
              + (mustClose.Count > 2 ? $" 等 {mustClose.Count} 个" : "");
        AddRow("待关闭的进程", mustClose.Count == 0 ? "无" : $"{mustClose.Count} 个",
               clientText, mustClose.Count == 0 ? Theme.Good : Theme.Warn);
        _list.EndUpdate();

        // 重建列表后滚动位置会保留，行高一变就容易停在半中间（顶部几行看不见）。
        // 每次刷新都回到第一行，用户看到的永远是完整列表。
        if (_list.Items.Count > 0) _list.EnsureVisible(0);
    }

    void AddRow(string name, string state, string? detail, Color color)
    {
        var item = new ListViewItem(name) { Tag = color };
        item.SubItems.Add(state);
        item.SubItems.Add(detail ?? "");
        _list.Items.Add(item);
    }

    // ---------------------------------------------------------------- 动作

    async Task Run(string title, Action<Action<string>> action)
    {
        if (_busy) return;
        _busy = true;
        SetButtons(false);
        _log.Clear();
        Log("== " + title + " ==");

        try
        {
            await Task.Run(() => action(msg =>
            {
                try { BeginInvoke(new Action(() => Log(msg))); } catch { }
            }));
            Log("完成");
        }
        catch (EnvException ex)
        {
            Log("失败：" + ex.Message.Replace("\r\n", "\n  "));
            MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            Log("失败：" + ex.Message);
            MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
            SetButtons(true);
            RefreshStatus();
        }
    }

    void ResetFlow(Action<string> log)
    {
        EnvController.Restore(_cfg, log);
        log("下次点任一个启动按钮，会按需把环境重新建起来。");
    }

    void Relocate()
    {
        if (_busy) return;
        Cursor.Current = Cursors.WaitCursor;
        try
        {
            var found = Locator.Scan();
            Cursor.Current = Cursors.Default;

            // 用副本改，背景、主题、作者这些设置不能因为重新定位就丢
            var next = _cfg.Clone();
            if (found.OfficialRoot is not null) next.OfficialRoot = found.OfficialRoot;
            if (found.PrivateRoot is not null) next.PrivateRoot = found.PrivateRoot;

            if (next.PrivateRoot is null)
            {
                var p = PickFolder(this, "请选择私服根目录（含 EpinelPS 与 NIKKEG 的那一层）");
                if (p is null) return;
                next.PrivateRoot = p;
            }
            if (next.OfficialRoot is null)
            {
                var p = PickFolder(this, "请选择官服根目录（含 NIKKE\\Launcher 的那一层）");
                if (p is null) return;
                next.OfficialRoot = p;
            }

            _cfg = next;
            Log("配置已保存：" + ConfigStore.Save(_cfg));
            RefreshStatus();
        }
        finally { Cursor.Current = Cursors.Default; }
    }

    void SetButtons(bool on)
    {
        foreach (var b in new[] { _btnPrivate, _btnOfficial, _btnReset })
        {
            b.Enabled = on;
            b.Cursor = on ? Cursors.Hand : Cursors.Default;
        }
    }

    void Log(string msg)
    {
        AppLog.Add(msg); // 同时进缓冲区，导出日志时要用
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
    }
}
