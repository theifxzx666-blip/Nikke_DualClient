namespace NikkeDualClient.Core;

public sealed record DiscoveryResult(string? OfficialRoot, string? PrivateRoot, List<string> Notes);

/// <summary>
/// 不认目录名，认特征文件组合：
///   私服根 = 目录下有 EpinelPS\EpinelPS.exe
///   官服根 = 目录下有 NIKKE\Launcher\nikke_launcher.exe
/// 所以 [Game]Nikke、我的游戏、NIKKE_153 这些命名都能认出来。
/// </summary>
public static class Locator
{
    static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        "Windows", "Program Files", "Program Files (x86)", "ProgramData", "$Recycle.Bin",
        "System Volume Information", "Users", "AppData", "Recovery", "PerfLogs",
        "$WinREAgent", "MSOCache", "Config.Msi", "Documents and Settings", "Windows.old"
    };

    /// <summary>先试常见位置，命中就省掉全盘扫描。</summary>
    static readonly string[] QuickNames =
    {
        "[Game]Nikke", "[Game]Nikke_Local", "Nikke", "NIKKE", "nikke",
        "Nikke_Local", "NIKKE_Local", "Games\\Nikke", "Game\\Nikke", "游戏\\Nikke"
    };

    public static bool IsPrivateRoot(string dir) =>
        File.Exists(Path.Combine(dir, @"EpinelPS\EpinelPS.exe"));

    public static bool IsOfficialRoot(string dir) =>
        File.Exists(Path.Combine(dir, @"NIKKE\Launcher\nikke_launcher.exe"));

    public static DiscoveryResult Scan(IProgress<string>? progress = null)
    {
        string? priv = null, offi = null;
        var notes = new List<string>();

        var drives = DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => d.RootDirectory.FullName)
            .ToList();

        // 第一轮：常见位置，通常 1 秒内命中
        foreach (var drive in drives)
        {
            foreach (var name in QuickNames)
            {
                var dir = Path.Combine(drive, name);
                if (!Directory.Exists(dir)) continue;
                progress?.Report(dir);
                if (priv is null && IsPrivateRoot(dir)) priv = dir;
                if (offi is null && IsOfficialRoot(dir)) offi = dir;
            }
        }
        if (priv is not null && offi is not null)
            return new(offi, priv, new List<string> { $"快速命中: 官服 {offi}", $"快速命中: 私服 {priv}" });

        // 第二轮：固定盘扫两层
        foreach (var drive in drives)
        {
            foreach (var dir in Candidates(drive, 2))
            {
                progress?.Report(dir);
                try
                {
                    if (priv is null && IsPrivateRoot(dir)) { priv = dir; notes.Add($"私服根: {dir}"); }
                    if (offi is null && IsOfficialRoot(dir)) { offi = dir; notes.Add($"官服根: {dir}"); }
                }
                catch { }
                if (priv is not null && offi is not null)
                    return new(offi, priv, notes);
            }
        }
        return new(offi, priv, notes);
    }

    static IEnumerable<string> Candidates(string root, int depth)
    {
        yield return root;
        IEnumerable<string> lvl1;
        try { lvl1 = Directory.EnumerateDirectories(root); }
        catch { yield break; }

        foreach (var d1 in lvl1)
        {
            if (Skip.Contains(Path.GetFileName(d1))) continue;
            yield return d1;
            if (depth < 2) continue;

            IEnumerable<string> lvl2;
            try { lvl2 = Directory.EnumerateDirectories(d1); }
            catch { continue; }
            foreach (var d2 in lvl2)
            {
                if (Skip.Contains(Path.GetFileName(d2))) continue;
                yield return d2;
            }
        }
    }
}
