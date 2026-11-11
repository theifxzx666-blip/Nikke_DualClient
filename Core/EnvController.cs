using System.Diagnostics;

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

        // 只挑游戏相关进程（服务端 + 客户端白名单）。全量枚举所有进程的 MainModule
        // 要两三秒，而这个方法每次点按钮都会走到；诊断报告那边另有全量扫描。
        var (servers, clients) = SplitProcesses(cfg);

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
            Processes = servers.Concat(clients).ToList()
        };
    }

    // ---------------------------------------------------------------- 进程分类

    static bool IsServer(ProcInfo p, AppConfig cfg) =>
        cfg.ServerExePath is not null &&
        string.Equals(p.Path, cfg.ServerExePath, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 只有客户端进程需要用户自己关。EpinelPS 是工具起的后台服务，自动停/自动复用都安全；
    /// 第三方启动器（双端共存启动器、妮姬启动QAQ 之类）虽然也在私服目录下，但不参与判断。
    ///
    /// 先按进程名过滤再读 MainModule —— 全量枚举几百个进程的模块路径要一两秒，
    /// 而这个方法每次点按钮都会走到。
    /// </summary>
    static (List<ProcInfo> Servers, List<ProcInfo> Clients) SplitProcesses(AppConfig cfg)
    {
        var roots = cfg.Roots;
        var servers = new List<ProcInfo>();
        var clients = new List<ProcInfo>();
        if (roots.Length == 0) return (servers, clients);

        var serverName = cfg.ServerExePath is null
            ? null
            : Path.GetFileNameWithoutExtension(cfg.ServerExePath);

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var maybeServer = serverName is not null &&
                                  p.ProcessName.Equals(serverName, StringComparison.OrdinalIgnoreCase);
                var maybeClient = ProcessScanner.IsClient(p.ProcessName);
                if (!maybeServer && !maybeClient) continue;

                var path = p.MainModule?.FileName;
                if (string.IsNullOrEmpty(path) || !ProcessScanner.InRoots(path, roots)) continue;

                if (maybeServer && string.Equals(path, cfg.ServerExePath, StringComparison.OrdinalIgnoreCase))
                    servers.Add(new ProcInfo(p.Id, p.ProcessName, path));
                else if (maybeClient)
                    clients.Add(new ProcInfo(p.Id, p.ProcessName, path));
            }
            catch { }
            finally { p.Dispose(); }
        }
        return (servers, clients);
    }

    /// <summary>
    /// allowOfficial：切官服时传 true。官服启动器开着不构成阻碍，
    /// 它不占用私服的文件和端口，拦它反而会让用户卡死（既关不掉也切不了）。
    /// </summary>
    static void RequireNoClient(EnvStatus s, AppConfig cfg, bool allowOfficial = false)
    {
        // 复用 Check 已经扫好的结果，别再枚举一遍进程
        var clients = s.Processes
            .Where(p => !IsServer(p, cfg) && ProcessScanner.IsClient(p.Name))
            .ToList();

        if (allowOfficial && cfg.OfficialRoot is not null)
            clients = clients.Where(p => !ProcessScanner.InRoot(p.Path, cfg.OfficialRoot)).ToList();

        if (clients.Count == 0) return;

        // launcher 开着的时候，tbs_browser / intl_service 都是它的子进程，
        // 列出来只会让用户以为要关八个东西
        var hasLauncher = clients.Any(p => p.Name.Equals("nikke_launcher", StringComparison.OrdinalIgnoreCase));
        var meaningful = hasLauncher
            ? clients.Where(p => p.Name.Equals("nikke_launcher", StringComparison.OrdinalIgnoreCase)
                                 || p.Name.Equals("nikke", StringComparison.OrdinalIgnoreCase)).ToList()
            : clients;
        if (meaningful.Count == 0) meaningful = clients;

        var summary = string.Join("、", meaningful
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key));

        var extra = clients.Count - meaningful.Count;
        if (extra > 0) summary += $"（另有 {extra} 个子进程）";

        throw new EnvException(
            $"游戏或启动器还开着：{summary}\r\n\r\n" +
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

        // 端口能连上但进程列表里没有 EpinelPS —— 443 被别人占了。
        // 这种情况下服务根本起不来，直接讲清楚比等 90 秒超时有用。
        if (s.PortOpen && !s.Processes.Any(p => IsServer(p, cfg)))
            throw new EnvException(
                $"443 端口被其他程序占用，而且不是 EpinelPS。\r\n\r\n" +
                "先把它关掉（可能是别的私服服务端、IIS、代理软件），再点启动。\r\n" +
                "判断依据：端口能连上，但进程列表里找不到 EpinelPS.exe。");

        // 已经完整在跑就什么都别做，避免连点两下弹一堆提示。
        // hosts 不能只看「有没有区块」—— 内容过期（域名列表变了）也得重写。
        if (launcherRunning && s.PortOpen && s.HostsBlock && s.CaInstalled
            && !s.NewsPolluted && HostsManager.IsBlockCurrent(cfg))
        {
            log("私服已在运行（服务与启动器都在），未做改动");
            return;
        }

        RequireNoClient(s, cfg);

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

        // 判断「官服是否在运行」必须带路径边界，否则 root=`D:\Game` 会把
        // `D:\Game\nikke153.6.10\...` 的私服客户端也算成官服，导致误报「官服已在运行」。
        var officialRunning = s.Processes.Any(p =>
            ProcessScanner.IsClient(p.Name) && ProcessScanner.InRoot(p.Path, cfg.OfficialRoot));

        if (officialRunning && s.Mode == 0)
        {
            log("官服已在运行，未做改动");
            return;
        }

        // 官服启动器开着不算阻碍：它不占私服的文件和端口，拦它会让用户卡死
        RequireNoClient(s, cfg, allowOfficial: true);

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
