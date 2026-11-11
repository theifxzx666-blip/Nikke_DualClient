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

    /// <summary>
    /// 被本工具临时注释掉的第三方条目（OfficialPins / 加速器 / ServerSelector 等）。
    /// 必须留标记：Disable 时要原样还原，用户原有的段不能被打乱。
    /// </summary>
    public const string ShadowPrefix = "#NIKKE-PRIV-OFF# ";

    /// <summary>
    /// 本工具以及历史手工脚本用过的注释标记。旧版本留下的是 "#PIN-OFF# "，
    /// Unshadow 必须一并识别 —— 否则切换官服时那些行还原不回来，
    /// 官方域名就会因为没有 IP 可用而解析失败（DNS 只给 0.0.0.1）。
    /// </summary>
    static readonly string[] ShadowPrefixes = [ShadowPrefix, "#PIN-OFF# "];

    /// <summary>
    /// 本工具自己写的官方入口区块。只在用户机器上完全没有 lobby 条目时才写 ——
    /// 用户自己有 OfficialPins / UsbEAm（#UHE_）之类的就尊重原配置，绝不叠一份。
    /// </summary>
    public const string OfficialBegin = "# >>> NIKKE-OFFICIAL-BEGIN";
    public const string OfficialEnd = "# <<< NIKKE-OFFICIAL-END";

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

    static (int Start, int End) FindBlock(List<string> lines)
    {
        var start = lines.FindIndex(l => l.StartsWith(MarkerBegin, StringComparison.Ordinal));
        if (start < 0) return (-1, -1);
        var end = lines.FindIndex(start + 1, l => l.StartsWith(MarkerEnd, StringComparison.Ordinal));
        return (start, end);
    }

    /// <summary>
    /// 区块内容与当前配置一致，且区块外不再有名为同一域名的生效条目。
    /// 后者是关键：Windows 会把 hosts 里同名域名的所有 IP 一起交给客户端，
    /// 只要还剩一条官方 IP（OfficialPins / 加速器 / ServerSelector 写的），
    /// 客户端就可能连到官方服务器，表现就是「加载失败」。
    /// </summary>
    public static bool IsBlockCurrent(AppConfig cfg)
    {
        var lines = ReadLines();
        var (start, end) = FindBlock(lines);
        if (start < 0 || end < 0) return false;

        var inside = lines.GetRange(start + 1, end - start - 1);
        if (inside.Count != cfg.PrivateHosts.Length) return false;

        for (var i = 0; i < cfg.PrivateHosts.Length; i++)
        {
            if (!inside[i].Contains(cfg.PrivateHosts[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return CountLiveConflicts(lines, cfg, start, end) == 0;
    }

    /// <summary>区块外、仍处于生效状态的同名条目数量。</summary>
    static int CountLiveConflicts(List<string> lines, AppConfig cfg, int skipFrom = -1, int skipTo = -1)
    {
        var set = new HashSet<string>(cfg.PrivateHosts, StringComparer.OrdinalIgnoreCase);
        var n = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            if (i >= skipFrom && i <= skipTo) continue;
            var t = lines[i].Trim();
            if (t.Length == 0 || t.StartsWith('#')) continue;
            var parts = t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && set.Contains(parts[1])) n++;
        }
        return n;
    }

    /// <summary>把区块外的同名条目注释掉（加前缀），返回处理条数。幂等。</summary>
    static int ShadowConflicts(List<string> lines, AppConfig cfg)
    {
        var set = new HashSet<string>(cfg.PrivateHosts, StringComparer.OrdinalIgnoreCase);
        var n = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (t.Length == 0 || t.StartsWith('#')) continue;
            var parts = t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !set.Contains(parts[1])) continue;
            lines[i] = ShadowPrefix + lines[i];
            n++;
        }
        return n;
    }

    /// <summary>还原被注释掉的条目（兼容所有历史前缀），返回还原条数。</summary>
    static int Unshadow(List<string> lines)
    {
        var n = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            foreach (var p in ShadowPrefixes)
            {
                var idx = lines[i].IndexOf(p, StringComparison.Ordinal);
                if (idx < 0) continue;
                lines[i] = lines[i].Substring(0, idx) + lines[i].Substring(idx + p.Length);
                n++;
                break;
            }
        }
        return n;
    }

    /// <summary>行里是否带任一历史注释标记。</summary>
    static bool HasAnyShadow(string line) =>
        ShadowPrefixes.Any(p => line.Contains(p, StringComparison.Ordinal));

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

    /// <summary>
    /// 写入私服区块：先摘旧块、注释掉其他来源的同名条目，再把区块放到文件最前。
    /// 注释这一步不能省 —— hosts 里同名域名的多条记录会被一起返回，
    /// 只移动区块位置无法阻止客户端连到官方 IP。
    /// </summary>
    public static string Enable(AppConfig cfg)
    {
        var lines = StripBlock(ReadLines());
        var bak = Backup("");

        ShadowConflicts(lines, cfg);

        var block = new List<string>
        {
            $"{MarkerBegin} session={DateTime.Now:yyyyMMddHHmmss} time={DateTime.Now:yyyy-MM-dd HH:mm:ss}"
        };
        foreach (var d in cfg.PrivateHosts)
            block.Add($"127.0.0.1\t{d}");
        block.Add(MarkerEnd);
        block.Add("");

        var merged = new List<string>(block.Count + lines.Count);
        merged.AddRange(block);
        merged.AddRange(lines);

        if (merged.Count > 0 && merged[^1].Trim().Length != 0) merged.Add("");

        Save(merged);
        return bak;
    }

    /// <summary>摘掉私服区块，并还原被注释掉的第三方条目。</summary>
    public static string? Disable()
    {
        var lines = ReadLines();
        var hadBlock = HasBlock(lines);
        var hadShadow = lines.Any(HasAnyShadow);
        if (!hadBlock && !hadShadow) return null;

        var bak = Backup("");
        var outLines = StripBlock(lines);
        Unshadow(outLines);
        Save(outLines);
        return bak;
    }

    // ---------------------------------------------------------------- 官服基线

    /// <summary>只有 lobby 域名需要钉 IP；intlgame 那批走 DNS 能正常解析，不碰。</summary>
    public static bool IsLobbyDomain(string d) =>
        d.EndsWith("-lobby.nikke-kr.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>文件里还有多少条生效的 lobby 条目。</summary>
    public static int CountLiveLobby(List<string> lines)
    {
        var n = 0;
        foreach (var line in lines)
        {
            var t = line.Trim();
            if (t.Length == 0 || t.StartsWith('#')) continue;
            var parts = t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && IsLobbyDomain(parts[1])) n++;
        }
        return n;
    }

    /// <summary>生效的 lobby 条目用的 IP（取第一条），没有则 null。</summary>
    public static string? FindLobbyIp(List<string> lines)
    {
        foreach (var line in lines)
        {
            var t = line.Trim();
            if (t.Length == 0 || t.StartsWith('#')) continue;
            var parts = t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && IsLobbyDomain(parts[1])) return parts[0];
        }
        return null;
    }

    public static bool HasOfficialBlock(List<string> lines) =>
        lines.Any(l => l.StartsWith(OfficialBegin, StringComparison.Ordinal));

    static List<string> StripOfficial(List<string> lines)
    {
        var result = new List<string>(lines.Count);
        var inside = false;
        foreach (var line in lines)
        {
            if (line.StartsWith(OfficialBegin, StringComparison.Ordinal)) { inside = true; continue; }
            if (line.StartsWith(OfficialEnd, StringComparison.Ordinal)) { inside = false; continue; }
            if (!inside) result.Add(line);
        }
        return result;
    }

    /// <summary>
    /// 收敛到官服基线：摘私服区块 → 摘自己的官方区块 → 还原被注释的第三方条目。
    /// 还原后如果用户本来就有 lobby 条目（OfficialPins / UsbEAm / 加速器写的），就不写，
    /// 尊重原配置；完全没有才用探测到的 ip 补一段，保证客户端拿得到入口。
    /// </summary>
    public static (string Backup, bool Wrote, string Detail) EnableOfficial(AppConfig cfg, string ip)
    {
        var lines = StripBlock(ReadLines());
        lines = StripOfficial(lines);
        var restored = Unshadow(lines);
        var bak = Backup("");

        var live = CountLiveLobby(lines);
        if (live > 0)
        {
            Save(lines);
            var extra = restored > 0 ? $"，还原 {restored} 条" : "";
            return (bak, false, $"沿用已有 {live} 条官方入口{extra}");
        }

        if (string.IsNullOrWhiteSpace(ip))
            return (bak, false, "无可用官方入口 IP，hosts 未改动");

        var block = new List<string>
        {
            $"{OfficialBegin} ip={ip} time={DateTime.Now:yyyy-MM-dd HH:mm:ss}"
        };
        foreach (var d in cfg.PrivateHosts.Where(IsLobbyDomain))
            block.Add($"{ip}\t{d}");
        block.Add(OfficialEnd);
        block.Add("");

        var merged = new List<string>(block.Count + lines.Count);
        merged.AddRange(block);
        merged.AddRange(lines);
        if (merged.Count > 0 && merged[^1].Trim().Length != 0) merged.Add("");

        Save(merged);
        return (bak, true, $"写入 {block.Count - 2} 条 lobby → {ip}");
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
