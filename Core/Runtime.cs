using System.Diagnostics;
using System.Net.Sockets;

namespace NikkeDualClient.Core;

public sealed record ProcInfo(int Id, string Name, string Path);

/// <summary>
/// 按完整安装路径识别进程，不按 exe 文件名 —— 否则会误伤另一个客户端。
/// </summary>
public static class ProcessScanner
{
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
                if (!string.IsNullOrEmpty(path) &&
                    roots.Any(r => path.StartsWith(r, StringComparison.OrdinalIgnoreCase)))
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
