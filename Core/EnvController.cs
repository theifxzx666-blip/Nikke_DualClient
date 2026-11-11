namespace NikkeDualClient.Core;

public sealed class EnvStatus
{
    public bool OfficialRootOk { get; init; }
    public bool PrivateRootOk { get; init; }
    public bool ServerOk { get; init; }
    public bool OfficialLauncherOk { get; init; }
    public bool PrivateLauncherOk { get; init; }

    public bool HostsBlock { get; init; }
    public bool NewsHijacked { get; init; }
    public string? CdnLine { get; init; }
    public bool CaInstalled { get; init; }
    public bool PortOpen { get; init; }
    public bool NewsPolluted { get; init; }
    public List<ProcInfo> Processes { get; init; } = new();

    /// <summary>0 = 官服基线，1 = 本机私服，2 = 中间态</summary>
    public int Mode
    {
        get
        {
            var flags = (HostsBlock ? 1 : 0) + (CaInstalled ? 1 : 0) + (PortOpen ? 1 : 0);
            return flags == 0 ? 0 : flags == 3 ? 1 : 2;
        }
    }

    public string ModeText => Mode switch
    {
        0 => "官服基线",
        1 => "本机私服",
        _ => "环境不完整"
    };
}

public sealed class EnvException : Exception
{
    public EnvException(string message) : base(message) { }
}

/// <summary>
/// 切换就是一件事：把环境收敛到目标态。调用方不用先点“恢复”，也不用记顺序。
/// 每次进来先体检，缺什么补什么，多余的清掉，已经对就跳过。
/// </summary>
public static class EnvController
{
    public static EnvStatus Check(AppConfig cfg)
    {
        var lines = HostsManager.ReadLines();
        var procs = ProcessScanner.Find(cfg);

        return new EnvStatus
        {
            OfficialRootOk = cfg.OfficialRoot is not null && Directory.Exists(cfg.OfficialRoot),
            PrivateRootOk = cfg.PrivateRoot is not null && Directory.Exists(cfg.PrivateRoot),
            ServerOk = cfg.ServerExePath is not null && File.Exists(cfg.ServerExePath),
            OfficialLauncherOk = (cfg.OfficialLauncherPath is not null && File.Exists(cfg.OfficialLauncherPath))
                                 || (cfg.OfficialFallbackPath is not null && File.Exists(cfg.OfficialFallbackPath)),
            PrivateLauncherOk = cfg.PrivateLauncherPath is not null && File.Exists(cfg.PrivateLauncherPath),
            HostsBlock = HostsManager.HasBlock(lines),
            NewsHijacked = HostsManager.NewsDomainHijacked(lines),
            CdnLine = HostsManager.FindCdnLine(lines),
            CaInstalled = CertManager.IsInstalled(cfg),
            PortOpen = ServerRunner.IsPortOpen(cfg.ServerPort),
            NewsPolluted = NewsRepair.IsPolluted(cfg),
            Processes = procs
        };
    }

    // ---------------------------------------------------------------- 进程分类

    static bool IsServer(ProcInfo p, AppConfig cfg) =>
        cfg.ServerExePath is not null &&
        string.Equals(p.Path, cfg.ServerExePath, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 只有客户端进程需要用户自己关。EpinelPS 是工具起的后台服务，自动停/自动复用都安全；
    /// 第三方启动器（双端共存启动器、妮姬启动QAQ 之类）虽然也在私服目录下，但不参与判断。
    /// </summary>
    static (List<ProcInfo> Servers, List<ProcInfo> Clients) SplitProcesses(AppConfig cfg)
    {
        var all = ProcessScanner.Find(cfg);
        return (
            all.Where(p => IsServer(p, cfg)).ToList(),
            all.Where(p => !IsServer(p, cfg) && ProcessScanner.IsClient(p.Name)).ToList()
        );
    }

    static void RequireNoClient(AppConfig cfg)
    {
        var (_, clients) = SplitProcesses(cfg);
        if (clients.Count == 0) return;

        var names = string.Join("、", clients.Select(p => $"{p.Name}({p.Id})"));
        throw new EnvException(
            $"游戏或启动器还开着：{names}\r\n\r\n" +
            "请完全退出（含右下角托盘图标）后再点一次。\r\n" +
            "工具不会强杀客户端 —— 正在下载更新或存进度时杀掉会出问题。");
    }

    // ---------------------------------------------------------------- 单机

    public static void GoPrivate(AppConfig cfg, Action<string> log)
    {
        if (cfg.ServerExePath is null || !File.Exists(cfg.ServerExePath))
            throw new EnvException(
                $"找不到私服服务端：{cfg.ServerExePath}\r\n\r\n" +
                "如果这台机器只玩官服，直接点「启动官服」就行。\r\n" +
                "要玩单机的话，到「设置 → 路径」把私服根目录指对。");

        if (cfg.PrivateLauncherPath is null || !File.Exists(cfg.PrivateLauncherPath))
            throw new EnvException(
                $"找不到私服客户端启动器：{cfg.PrivateLauncherPath}\r\n\r\n" +
                "私服根目录下应该有 NIKKEG\\Launcher\\nikke_launcher.exe。\r\n" +
                "路径不对的话到「设置 → 路径」重新指一下。");

        var s = Check(cfg);
        var launcherRunning = s.Processes.Any(p =>
            string.Equals(p.Path, cfg.PrivateLauncherPath, StringComparison.OrdinalIgnoreCase));

        // 已经完整在跑就什么都别做，避免连点两下弹一堆提示
        if (launcherRunning && s.PortOpen && s.HostsBlock && s.CaInstalled && !s.NewsPolluted)
        {
            log("私服已在运行（服务与启动器都在），未做改动");
            return;
        }

        RequireNoClient(cfg);

        var todo = new List<string>();
        if (!s.HostsBlock) todo.Add("hosts");
        if (!s.CaInstalled) todo.Add("证书");
        if (s.NewsPolluted) todo.Add("新闻页");
        if (!s.PortOpen) todo.Add("服务");

        log(todo.Count == 0 ? "环境已就绪" : $"自动补齐：{string.Join("、", todo)}");

        if (!s.HostsBlock || !HostsManager.IsBlockCurrent(cfg))
        {
            HostsManager.Enable(cfg);
            log(s.HostsBlock ? "  hosts 区块内容有变，已重写" : "  hosts 私服区块已写入");
        }

        if (!s.CaInstalled)
        {
            var thumb = CertManager.Install(cfg);
            log($"  私服根证书已安装（{thumb[..Math.Min(12, thumb.Length)]}…）");
        }

        if (s.NewsPolluted)
        {
            var r = NewsRepair.Repair(cfg);
            log($"  新闻页已重写（{r.Actions.Count} 项）");
            foreach (var w in r.Warnings) log("  [注意] " + w);
        }

        if (!s.PortOpen)
        {
            // 启动服务前补一次选择器路径（服务端不读它，只是让旧启动器不再显示错路径）
            SelectorSync.WriteIfStale(cfg, log);

            var pid = ServerRunner.Start(cfg, out var reused);
            log(reused ? $"  复用已有服务进程 PID {pid}" : $"  服务已启动 PID {pid}");
            if (!ServerRunner.WaitPort(cfg.ServerPort, cfg.ServerReadyTimeoutSec))
                throw new EnvException(
                    $"服务起来了但 {cfg.ServerReadyTimeoutSec} 秒内 443 仍未监听。\r\n" +
                    $"查看日志：{Path.Combine(Path.GetDirectoryName(cfg.ServerExePath!)!, "logs")}");
            log("  443 已监听");
        }

        ServerRunner.StartClient(cfg, official: false);
        log("已打开私服启动器");
    }

    // ---------------------------------------------------------------- 官服

    public static void GoOfficial(AppConfig cfg, Action<string> log)
    {
        // 先确认官服入口在，别清完环境才发现没得开
        var launcher = cfg.OfficialLauncherPath;
        if (launcher is null || !File.Exists(launcher)) launcher = cfg.OfficialFallbackPath;
        if (launcher is null || !File.Exists(launcher))
            throw new EnvException($"找不到官服启动器：{cfg.OfficialLauncherPath}");

        var s = Check(cfg);
        var officialRunning = s.Processes.Any(p =>
            cfg.OfficialRoot is not null &&
            p.Path.StartsWith(cfg.OfficialRoot, StringComparison.OrdinalIgnoreCase));

        if (officialRunning && s.Mode == 0)
        {
            log("官服已在运行，未做改动");
            return;
        }

        RequireNoClient(cfg);

        var (servers, _) = SplitProcesses(cfg);
        var todo = new List<string>();
        if (servers.Count > 0 || s.PortOpen) todo.Add("停止私服服务");
        if (s.HostsBlock) todo.Add("还原 hosts");
        if (s.CaInstalled) todo.Add("卸载证书");

        if (todo.Count == 0)
        {
            log("环境已是官服基线");
        }
        else
        {
            log($"正在清理私服环境：{string.Join("、", todo)}");

            if (servers.Count > 0 || s.PortOpen)
            {
                var n = ServerRunner.Stop(cfg);
                log($"  私服服务已停止（{n} 个进程）");
            }
            if (s.HostsBlock)
            {
                HostsManager.Disable();
                log("  hosts 已还原");
            }
            if (s.CaInstalled)
            {
                var n = CertManager.Remove(cfg);
                log($"  私服根证书已卸载（{n} 张）");
            }
        }

        ServerRunner.StartClient(cfg, official: true);
        log("已打开官服启动器");
    }

    // ---------------------------------------------------------------- 应急

    /// <summary>只把系统环境收回官服基线，不开任何客户端。环境彻底乱了才用。</summary>
    public static void Restore(AppConfig cfg, Action<string> log)
    {
        var (servers, _) = SplitProcesses(cfg);

        if (servers.Count > 0) log($"停止私服服务（{ServerRunner.Stop(cfg)} 个进程）");
        else log("私服服务未运行");

        log(HostsManager.Disable() is null ? "hosts 无私服区块" : "hosts 已还原");

        var n = CertManager.Remove(cfg);
        log(n == 0 ? "私服根证书未安装" : $"私服根证书已卸载（{n} 张）");

        log("环境已回到官服基线");
    }

    public static NewsRepairResult RepairNews(AppConfig cfg, Action<string> log)
    {
        log("备份并重写新闻页…");
        var r = NewsRepair.Repair(cfg);
        foreach (var a in r.Actions) log("  " + a);
        foreach (var w in r.Warnings) log("  [注意] " + w);
        return r;
    }
}
