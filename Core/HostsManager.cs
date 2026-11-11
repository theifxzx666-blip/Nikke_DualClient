using System.Diagnostics;

namespace NikkeDualClient.Core;

/// <summary>
/// hosts 只动自己标记的那一段，其他行原样保留。
/// 用 Latin1 读写：hosts 是 ANSI 文本，逐字节往返不会损坏原有中文注释。
/// </summary>
public static class HostsManager
{
    public const string MarkerBegin = "# >>> NIKKE-PRIVATE-BEGIN";
    public const string MarkerEnd = "# <<< NIKKE-PRIVATE-END";

    static readonly System.Text.Encoding Raw = System.Text.Encoding.Latin1;

    public static string HostsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        @"System32\drivers\etc\hosts");

    public static List<string> ReadLines()
    {
        var text = File.ReadAllText(HostsPath, Raw);
        return text.Replace("\r\n", "\n").Split('\n').ToList();
    }

    public static bool HasBlock(List<string> lines) =>
        lines.Any(l => l.StartsWith(MarkerBegin, StringComparison.Ordinal));

    public static bool HasBlock() => HasBlock(ReadLines());

    /// <summary>区块内容是否和当前配置一致 —— 配置里改过域名时才需要重写。</summary>
    public static bool IsBlockCurrent(AppConfig cfg)
    {
        var lines = ReadLines();
        var start = lines.FindIndex(l => l.StartsWith(MarkerBegin, StringComparison.Ordinal));
        if (start < 0) return false;
        var end = lines.FindIndex(start + 1, l => l.StartsWith(MarkerEnd, StringComparison.Ordinal));
        if (end < 0) return false;

        var inside = lines.GetRange(start + 1, end - start - 1);
        if (inside.Count != cfg.PrivateHosts.Length) return false;

        for (var i = 0; i < cfg.PrivateHosts.Length; i++)
        {
            if (!inside[i].Contains(cfg.PrivateHosts[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    /// <summary>摘掉标记段，其余行不动。</summary>
    public static List<string> StripBlock(List<string> lines)
    {
        var result = new List<string>(lines.Count);
        var inside = false;
        foreach (var line in lines)
        {
            if (line.StartsWith(MarkerBegin, StringComparison.Ordinal)) { inside = true; continue; }
            if (line.StartsWith(MarkerEnd, StringComparison.Ordinal)) { inside = false; continue; }
            if (!inside) result.Add(line);
        }
        return result;
    }

    public static string Backup(string tag)
    {
        var dir = Path.Combine(ConfigStore.DataDir, "backup");
        Directory.CreateDirectory(dir);
        var dst = Path.Combine(dir, $"hosts_{DateTime.Now:yyyyMMdd_HHmmss}{tag}.bak");
        File.Copy(HostsPath, dst, true);
        return dst;
    }

    static void Save(List<string> lines)
    {
        File.WriteAllText(HostsPath, string.Join("\r\n", lines), Raw);
        FlushDns();
    }

    static void FlushDns()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("ipconfig", "/flushdns")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            });
            p?.WaitForExit(5000);
        }
        catch { }
    }

    /// <summary>写入私服区块：先摘旧块再写新块，等价于“恢复 + 启用”一次完成。</summary>
    public static string Enable(AppConfig cfg)
    {
        var lines = StripBlock(ReadLines());
        var bak = Backup("");

        if (lines.Count > 0 && lines[^1].Trim().Length != 0) lines.Add("");
        lines.Add($"{MarkerBegin} session={DateTime.Now:yyyyMMddHHmmss} time={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        foreach (var d in cfg.PrivateHosts)
            lines.Add($"127.0.0.1\t{d}");
        lines.Add(MarkerEnd);

        Save(lines);
        return bak;
    }

    public static string? Disable()
    {
        var lines = ReadLines();
        if (!HasBlock(lines)) return null;

        var bak = Backup("");
        Save(StripBlock(lines));
        return bak;
    }

    /// <summary>有效行里是否还有新闻域名指向本机 —— 广告页复发的第一信号。</summary>
    public static bool NewsDomainHijacked(List<string> lines)
    {
        foreach (var line in lines)
        {
            var t = line.Trim();
            if (t.Length == 0 || t.StartsWith('#')) continue;
            if (t.Contains("na-community.playerinfinite.com", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>EpinelPS 自己维护的 CDN 解析行，这里只读取展示。</summary>
    public static string? FindCdnLine(List<string> lines)
    {
        foreach (var line in lines)
        {
            var t = line.Trim();
            if (t.Length == 0 || t.StartsWith('#')) continue;
            if (t.Contains("cloud.nikke-kr.com", StringComparison.OrdinalIgnoreCase)) return t;
        }
        return null;
    }
}
