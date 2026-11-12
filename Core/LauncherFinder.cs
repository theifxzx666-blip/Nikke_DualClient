namespace NikkeDualClient.Core;

/// <summary>
/// 启动器定位。不认目录名，只认文件名。
///
/// 一键包有四种常见形态：NIKKEG\Launcher、NIKKE\Launcher、自建目录，
/// 以及「根目录直接指到游戏目录本身」（少一层 Launcher 的父目录）。
/// 写死一条相对路径，换个包就误报「缺失」，用户还得手改 json。
/// 这里按「配置 → 常见布局 → 浅层搜索」三级回退，私服官服共用一套。
/// </summary>
public static class LauncherFinder
{
    const string LauncherName = "nikke_launcher.exe";
    const string MiniLoaderPattern = "nikkeminiloader*.exe";

    /// <summary>搜索深度，报错文案要跟这个数对齐。</summary>
    public const int Depth = 3;

    const int MaxDirs = 2000;   // 根被误配成盘符时的熔断

    /// <summary>私服常见布局，顺序即优先级。</summary>
    static readonly string[] PrivateCommon =
    {
        @"NIKKEG\Launcher\nikke_launcher.exe",
        @"NIKKE\Launcher\nikke_launcher.exe",
        @"NIKKEG\nikke_launcher.exe",
        @"NIKKE\nikke_launcher.exe",
        @"Launcher\nikke_launcher.exe",
        "nikke_launcher.exe"
    };

    /// <summary>官服常见布局：比私服多了「根就是游戏目录」这一种（…\NIKKE 下直接 Launcher）。</summary>
    static readonly string[] OfficialCommon =
    {
        @"NIKKE\Launcher\nikke_launcher.exe",
        @"Launcher\nikke_launcher.exe",
        "nikke_launcher.exe"
    };

    static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        "Windows", "Program Files", "Program Files (x86)", "ProgramData", "$Recycle.Bin",
        "System Volume Information", "Users", "AppData", "Recovery", "PerfLogs",
        "$WinREAgent", "MSOCache", "Config.Msi", "Windows.old", "node_modules", ".git"
    };

    // ---------------------------------------------------------------- 私服

    /// <summary>返回可直接启动的私服客户端启动器，找不到返回 null。</summary>
    public static string? Resolve(AppConfig cfg)
    {
        var root = cfg.PrivateRoot;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return null;

        // 1) 配置里记的那条，用户手改过就优先尊重
        var configured = cfg.PrivateLauncherPath;
        if (configured is not null && File.Exists(configured)) return configured;

        // 2) 常见布局
        foreach (var rel in PrivateCommon)
        {
            var p = Path.Combine(root, rel);
            if (File.Exists(p)) return p;
        }

        // 3) 只认文件名不认目录名：客户端被改名塞进自建目录时靠这一步兜住
        foreach (var file in Outside(cfg, root, LauncherName))
            return file;

        return null;
    }

    // ---------------------------------------------------------------- 官服

    /// <summary>
    /// 官服启动器。除了标准布局，还要兼容「官服根指到游戏目录本身」
    /// 和 nikkeminiloader 兜底（版本号后缀每次更新都变，只能通配）。
    /// </summary>
    public static string? ResolveOfficial(AppConfig cfg)
    {
        var root = cfg.OfficialRoot;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return null;

        var configured = cfg.OfficialLauncherPath;
        if (configured is not null && File.Exists(configured)) return configured;

        foreach (var rel in OfficialCommon)
        {
            var p = Path.Combine(root, rel);
            if (File.Exists(p)) return p;
        }

        // 迷你加载器：配置里那条固定名，加根下任意 nikkeminiloader_*.exe
        var fallback = cfg.OfficialFallbackPath;
        if (fallback is not null && File.Exists(fallback)) return fallback;

        foreach (var file in Outside(cfg, root, LauncherName))
            return file;

        foreach (var file in Outside(cfg, root, MiniLoaderPattern))
            return file;

        return null;
    }

    // ---------------------------------------------------------------- 公共

    /// <summary>
    /// 搜索时跳过官服根里的同名文件：私服根如果配成整个盘，会先把官服那份捞出来。
    /// 两个根相同则不做跳过（有的用户两个根指同一处）。
    /// </summary>
    static IEnumerable<string> Outside(AppConfig cfg, string root, string pattern)
    {
        var officialRoot = cfg.OfficialRoot;
        var same = !string.IsNullOrWhiteSpace(officialRoot)
                   && string.Equals(root.TrimEnd('\\'), officialRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

        foreach (var file in Walk(root, pattern))
        {
            if (!same && !string.IsNullOrWhiteSpace(officialRoot)
                && ProcessScanner.InRoot(file, officialRoot)) continue;
            yield return file;
        }
    }

    /// <summary>广度优先浅搜，命中上限即停。</summary>
    static IEnumerable<string> Walk(string root, string pattern)
    {
        var queue = new Queue<(string Dir, int Depth)>();
        queue.Enqueue((root, 0));
        var seen = 0;

        while (queue.Count > 0 && seen < MaxDirs)
        {
            var (dir, depth) = queue.Dequeue();
            seen++;

            string[] files;
            try { files = Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly); }
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
