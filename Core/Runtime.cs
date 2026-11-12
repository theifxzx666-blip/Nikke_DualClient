using System.Diagnostics;
using System.Net.Sockets;

namespace NikkeDualClient.Core;

public sealed record ProcInfo(int Id, string Name, string Path);

/// <summary>
/// 按完整安装路径识别进程，不按 exe 文件名 —— 否则会误伤另一个客户端。
/// 另外只把真正的游戏进程算“客户端”：私服包根目录里常混着第三方启动器
/// （双端共存启动器、妮姬启动QAQ、ServerSelector 之类），它们也躺在同一个目录下，
/// 但关掉它们没有任何意义，不该拦用户。
/// </summary>
public static class ProcessScanner
{
    /// <summary>
    /// 只认真正的游戏进程。tbs_browser / intl_service / version_service / assistant
    /// 都是启动器的子进程，启动器一关它们自己就退 —— 以前把它们算进来，
    /// 结果是「明明只开着 QQ，却说你游戏没关」。宁可少拦，不能拦死。
    /// </summary>
    static readonly HashSet<string> ClientNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "nikke",
        "nikke_launcher"
    };

    /// <summary>
    /// 永远不算客户端：
    /// crashpad_handler 是 CEF/Electron 通用的崩溃上报进程（启动器、UU、QQ 都在用同一个名字，
    /// 也常在启动器退出后变成孤儿）；GameViewer* 是网易 UU 加速器组件。
    /// 把它们算成「要关的客户端」，用户就会遇到「明明全关了还被拦」。
    /// </summary>
    static readonly HashSet<string> Noise = new(StringComparer.OrdinalIgnoreCase)
    {
        "crashpad_handler",
        "gameviewer", "gameviewerservice", "gameviewerserver",
        "gameviewerhealthd", "gameviewer_healthd"
    };

    /// <summary>进程名是否属于游戏客户端。</summary>
    public static bool IsClient(string processName) =>
        ClientNames.Contains(processName) && !Noise.Contains(processName);

    /// <summary>
    /// 判断路径是否在某个根目录**内部**。必须带分隔符边界 ——
    /// 否则 root=`D:\Game\Nikke` 会把 `D:\Game\Nikke_Local\...` 也算进去。
    /// </summary>
    public static bool InRoot(string path, string? root)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrEmpty(path)) return false;

        var r = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (path.Equals(r, StringComparison.OrdinalIgnoreCase)) return true;

        return path.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static bool InRoots(string path, string[] roots) =>
        roots.Any(r => InRoot(path, r));

    /// <summary>
    /// 真正需要用户关的进程。启动器在跑时 intl_service / tbs_browser 都是它的子进程，
    /// 关掉主进程它们自己就退了；全列出来用户会以为要关九个。
    /// 状态行和拦截弹窗都走这一套，避免一处说 9 个、一处说 1 个。
    /// </summary>
    public static List<ProcInfo> MustClose(IEnumerable<ProcInfo> clients)
    {
        var list = clients.ToList();
        var hasLauncher = list.Any(p => p.Name.Equals("nikke_launcher", StringComparison.OrdinalIgnoreCase));
        if (!hasLauncher) return list;

        var main = list.Where(p => p.Name.Equals("nikke_launcher", StringComparison.OrdinalIgnoreCase)
                                   || p.Name.Equals("nikke", StringComparison.OrdinalIgnoreCase)).ToList();
        return main.Count > 0 ? main : list;
    }

    /// <summary>只挑客户端：先按进程名过滤，避免对几百个无关进程访问 MainModule。</summary>
    public static List<ProcInfo> FindClients(AppConfig cfg)
    {
        var roots = cfg.Roots;
        var result = new List<ProcInfo>();
        if (roots.Length == 0) return result;

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (!IsClient(p.ProcessName)) continue; // 名字都不沾边，不用碰 MainModule

                var path = p.MainModule?.FileName;
                if (!string.IsNullOrEmpty(path) && InRoots(path, roots))
                    result.Add(new ProcInfo(p.Id, p.ProcessName, path));
            }
            catch { }
            finally { p.Dispose(); }
        }
        return result;
    }

    public static List<ProcInfo> Find(AppConfig cfg)
    {
        var roots = cfg.Roots;
        var result = new List<ProcInfo>();
        if (roots.Length == 0) return result;

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var path = p.MainModule?.FileName;
                if (!string.IsNullOrEmpty(path) && InRoots(path, roots))
                {
                    result.Add(new ProcInfo(p.Id, p.ProcessName, path));
                }
            }
            catch { /* 系统进程拿不到 MainModule，跳过 */ }
            finally { p.Dispose(); }
        }
        return result;
    }

    public static bool IsRunning(AppConfig cfg, string exePath) =>
        Find(cfg).Any(p => string.Equals(p.Path, exePath, StringComparison.OrdinalIgnoreCase));
}

public static class ServerRunner
{
    public static bool IsPortOpen(int port, int timeoutMs = 700)
    {
        using var client = new TcpClient();
        try
        {
            var ar = client.BeginConnect("127.0.0.1", port, null, null);
            return ar.AsyncWaitHandle.WaitOne(timeoutMs) && client.Connected;
        }
        catch { return false; }
    }

    public static bool WaitPort(int port, int timeoutSec, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSec);
        while (DateTime.UtcNow < deadline)
        {
            if (ct.IsCancellationRequested) return false;
            if (IsPortOpen(port)) return true;
            Thread.Sleep(600);
        }
        return false;
    }

    /// <summary>启动服务并返回 PID。已存在同路径进程时直接复用。</summary>
    public static int Start(AppConfig cfg, out bool reused)
    {
        var exe = cfg.ServerExePath;
        if (exe is null || !File.Exists(exe))
            throw new FileNotFoundException($"找不到私服服务端：{exe}");

        var existing = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe))
            .FirstOrDefault(p =>
            {
                try { return string.Equals(p.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            });

        if (existing is not null)
        {
            reused = true;
            return existing.Id;
        }

        reused = false;
        // 服务是控制台程序，给独立窗口 —— 用户要能看到那个黑框，也方便关
        return ProcessLauncher.Start(exe, Path.GetDirectoryName(exe), newConsole: true);
    }

    public static int Stop(AppConfig cfg)
    {
        var exe = cfg.ServerExePath;
        if (exe is null) return 0;

        var killed = 0;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (string.Equals(p.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase))
                {
                    p.Kill(true);
                    p.WaitForExit(5000);
                    killed++;
                }
            }
            catch { }
            finally { p.Dispose(); }
        }
        return killed;
    }

    /// <summary>启动客户端启动器。主入口缺失时用备用入口（官服有 miniloader 兜底）。</summary>
    public static void StartClient(AppConfig cfg, bool official)
    {
        // 官服的 miniloader 兜底已经收进 ResolveOfficial，这里不再单独判一次
        var exe = official ? LauncherFinder.ResolveOfficial(cfg) : LauncherFinder.Resolve(cfg);

        if (exe is null || !File.Exists(exe))
            throw new FileNotFoundException($"找不到启动器：{exe}");

        ProcessLauncher.Start(exe, Path.GetDirectoryName(exe), newConsole: false);
    }
}
