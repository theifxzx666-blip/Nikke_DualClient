namespace NikkeDualClient.Core;

/// <summary>
/// 私服客户端启动器定位。
///
/// 为什么不认死 NIKKEG：一键包有 NIKKEG / NIKKE / 自建目录三种常见形态，
/// 写死一条相对路径，换个包就误报「缺失」，用户还得手改 json。
/// 这里按「配置 → 常见布局 → 浅层搜索」三级回退，命中即用。
/// </summary>
public static class LauncherFinder
{
    /// <summary>常见布局，顺序即优先级。</summary>
    static readonly string[] Common =
    {
        @"NIKKEG\Launcher\nikke_launcher.exe",
        @"NIKKE\Launcher\nikke_launcher.exe",
        @"NIKKEG\nikke_launcher.exe",
        @"NIKKE\nikke_launcher.exe",
        @"Launcher\nikke_launcher.exe",
        "nikke_launcher.exe"
    };

    const string FileName = "nikke_launcher.exe";

    /// <summary>搜索深度，报错文案要跟这个数对齐。</summary>
    public const int Depth = 3;

    const int MaxDirs = 2000;   // 私服根被误配成盘符时的熔断

    static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        "Windows", "Program Files", "Program Files (x86)", "ProgramData", "$Recycle.Bin",
        "System Volume Information", "Users", "AppData", "Recovery", "PerfLogs",
        "$WinREAgent", "MSOCache", "Config.Msi", "Windows.old", "node_modules", ".git"
    };

    /// <summary>返回可直接启动的完整路径，找不到返回 null。</summary>
    public static string? Resolve(AppConfig cfg)
    {
        var root = cfg.PrivateRoot;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return null;

        // 1) 配置里记的那条，用户手改过就优先尊重
        var configured = cfg.PrivateLauncherPath;
        if (configured is not null && File.Exists(configured)) return configured;

        // 2) 常见布局
        foreach (var rel in Common)
        {
            var p = Path.Combine(root, rel);
            if (File.Exists(p)) return p;
        }

        // 3) 只认文件名不认目录名：客户端被改名塞进自建目录时靠这一步兜住
        var official = cfg.OfficialLauncherPath;
        foreach (var file in Walk(root))
        {
            if (official is not null && string.Equals(file, official, StringComparison.OrdinalIgnoreCase)) continue;
            return file;
        }

        return null;
    }

    /// <summary>广度优先浅搜，命中上限即停。</summary>
    static IEnumerable<string> Walk(string root)
    {
        var queue = new Queue<(string Dir, int Depth)>();
        queue.Enqueue((root, 0));
        var seen = 0;

        while (queue.Count > 0 && seen < MaxDirs)
        {
            var (dir, depth) = queue.Dequeue();
            seen++;

            string[] files;
            try { files = Directory.GetFiles(dir, FileName, SearchOption.TopDirectoryOnly); }
            catch { files = Array.Empty<string>(); }

            foreach (var f in files) yield return f;

            if (depth >= Depth) continue;

            string[] subs;
            try { subs = Directory.GetDirectories(dir); }
            catch { continue; }

            foreach (var s in subs)
            {
                if (Skip.Contains(Path.GetFileName(s))) continue;
                queue.Enqueue((s, depth + 1));
            }
        }
    }
}
