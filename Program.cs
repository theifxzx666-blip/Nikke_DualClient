using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using NikkeDualClient.Core;
using NikkeDualClient.Ui;

namespace NikkeDualClient;

internal static class Program
{
    [DllImport("kernel32.dll")] static extern bool AttachConsole(uint dwProcessId);
    [DllImport("kernel32.dll")] static extern bool AllocConsole();

    [STAThread]
    static int Main(string[] args)
    {
        AppLog.Start(); // 先开日志，崩溃时才有东西可查

        // 诊断用：直接打开设置窗口，便于截图核对布局
        if (args.Length > 0 && args[0].Equals("--settings", StringComparison.OrdinalIgnoreCase))
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var c = ConfigStore.Load() ?? new AppConfig();
            Theme.Apply(c.ThemeMode);
            var page = args.Length > 1 ? args[1].ToLowerInvariant() switch
            {
                "path" => 1,
                "theme" => 2,
                "maint" => 3,
                "about" => 4,
                _ => 0
            } : 0;
            Application.Run(new SettingsForm(c, page));
            return 0;
        }

        if (args.Length > 0) return RunCli(args);

        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject as Exception);
        Application.ThreadException += (_, e) => LogCrash(e.Exception);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            MessageBox.Show(ex.ToString(), "NIKKE 双服切换 启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 3;
        }
    }

    static void LogCrash(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            File.AppendAllText(
                Path.Combine(AppContext.BaseDirectory, "crash.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n\r\n");
        }
        catch { }
    }

    // ---------------------------------------------------------------- CLI
    // 给 GM 工具/批处理调用留的接口，不弹窗口，输出 JSON。

    static int RunCli(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (!AttachConsole(0xFFFFFFFF)) AllocConsole();

        try
        {
            var cfg = LoadOrScan(out var scanNotes);
            if (cfg is null)
            {
                WriteJson(new { ok = false, error = "没有找到官服或私服目录，请先运行图形界面完成定位" });
                return 2;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "--probe":
                {
                    // 只探路不写 hosts：官方入口会迁移，用户觉得连不上时可以先用这个确认。
                    // stdout 在「提权 + 重定向」组合下会被吞，所以结果同时写进 runtime.log。
                    var lines = new List<string>();
                    var r = OfficialProbe.Probe(cfg.LobbyCandidates, 1500, m =>
                    {
                        lines.Add(m.Trim());
                        AppLog.Add("probe " + m.Trim());
                    });

                    var verdict = r.Ok ? $"BEST {r.Ip} {r.LatencyMs}ms" : "ALL FAILED";
                    AppLog.Add("probe 结果: " + verdict);

                    WriteJson(new
                    {
                        ok = r.Ok,
                        best = r.Ok ? r.Ip : null,
                        latencyMs = r.LatencyMs,
                        candidates = cfg.LobbyCandidates,
                        detail = lines,
                        logFile = AppLog.LogFile
                    });
                    return r.Ok ? 0 : 2;
                }

                case "--verify":
                {
                    var s = EnvController.Check(cfg);
                    WriteJson(new
                    {
                        ok = true,
                        mode = s.Mode switch { 0 => "official", 1 => "private", _ => "mixed" },
                        modeText = s.ModeText,
                        officialRoot = cfg.OfficialRoot,
                        privateRoot = cfg.PrivateRoot,
                        hostsBlock = s.HostsBlock,
                        officialLobby = s.OfficialLobby,
                        lobbyIp = s.LobbyIp,
                        lobbyCandidates = cfg.LobbyCandidates,
                        newsHijacked = s.NewsHijacked,
                        caInstalled = s.CaInstalled,
                        portOpen = s.PortOpen,
                        newsPolluted = s.NewsPolluted,
                        processes = s.Processes.Select(p => new { p.Name, p.Id })
                    });
                    return s.Mode == 0 ? 0 : 1;
                }

                case "--restore":
                {
                    var steps = new List<string>();
                    EnvController.Restore(cfg, m => { AppLog.Add(m); steps.Add(m); });
                    WriteJson(new { ok = true, action = "restore", steps });
                    return 0;
                }

                case "--repair":
                {
                    var r = EnvController.RepairNews(cfg, AppLog.Add);
                    WriteJson(new { ok = true, action = "repair", backup = r.BackupDir, warnings = r.Warnings });
                    return 0;
                }

                case "--start":
                {
                    var target = args.Length > 1 ? args[1].ToLowerInvariant() : "private";
                    if (target is not ("private" or "official"))
                    {
                        WriteJson(new { ok = false, error = $"未知目标：{target}（只接受 private 或 official）" });
                        return 2;
                    }

                    var steps = new List<string>();
                    void Sink(string m) { AppLog.Add(m); steps.Add(m); }

                    if (target == "official") EnvController.GoOfficial(cfg, Sink);
                    else EnvController.GoPrivate(cfg, Sink);
                    WriteJson(new { ok = true, action = "start", target, steps });
                    return 0;
                }

                case "--scan":
                    WriteJson(new { ok = true, scanNotes, officialRoot = cfg.OfficialRoot, privateRoot = cfg.PrivateRoot });
                    return 0;

                case "--diag":
                {
                    // 导出诊断日志：带路径就写文件，不带就打到输出
                    var report = Diagnostics.BuildReport(cfg);
                    if (args.Length > 1)
                    {
                        File.WriteAllText(args[1], report, new System.Text.UTF8Encoding(true));
                        WriteJson(new { ok = true, action = "diag", path = Path.GetFullPath(args[1]) });
                    }
                    else
                    {
                        Console.WriteLine(report);
                        WriteJson(new { ok = true, action = "diag" });
                    }
                    return 0;
                }

                default:
                    WriteJson(new { ok = false, error = $"未知参数：{args[0]}" });
                    return 2;
            }
        }
        catch (Exception ex)
        {
            WriteJson(new { ok = false, error = ex.Message });
            return 2;
        }
    }

    static void WriteJson(object o) =>
        Console.WriteLine(JsonSerializer.Serialize(o, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }));

    static AppConfig? LoadOrScan(out List<string> notes)
    {
        notes = new List<string>();

        // 配置存在但路径失效（换机器、挪目录）也要重扫，不能直接信
        var cfg = ConfigStore.Load();
        if (cfg is not null
            && Directory.Exists(cfg.PrivateRoot ?? "")
            && Directory.Exists(cfg.OfficialRoot ?? ""))
            return cfg;

        var found = Locator.Scan();
        notes = found.Notes;
        if (found.OfficialRoot is null && found.PrivateRoot is null) return null;

        cfg ??= new AppConfig();
        if (found.OfficialRoot is not null) cfg.OfficialRoot = found.OfficialRoot;
        if (found.PrivateRoot is not null) cfg.PrivateRoot = found.PrivateRoot;
        ConfigStore.Save(cfg);
        return cfg;
    }
}
