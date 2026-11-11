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
    static readonly HashSet<string> ClientNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "nikke",
        "nikke_launcher",
        "tbs_browser",
        "intl_service",
        "version_service",
        "assistant"
    };

    /// <summary>进程名是否属于游戏客户端。</summary>
    public static bool IsClient(string processName) => ClientNames.Contains(processName);

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
        var exe = official ? cfg.OfficialLauncherPath : cfg.PrivateLauncherPath;
        if (official && (exe is null || !File.Exists(exe)))
            exe = cfg.OfficialFallbackPath;

        if (exe is null || !File.Exists(exe))
            throw new FileNotFoundException($"找不到启动器：{exe}");

        ProcessLauncher.Start(exe, Path.GetDirectoryName(exe), newConsole: false);
    }
}
