using System.Text.Json;
using System.Text.Json.Serialization;

namespace NikkeDualClient.Core;

/// <summary>
/// 全部路径都存“相对根目录”，换设备只需要改两个 root。
/// 这样 153 版叫 NIKKEG、154 版改名、或者用户自定义安装目录都不影响。
/// </summary>
public sealed class AppConfig
{
    public string? OfficialRoot { get; set; }
    public string? PrivateRoot { get; set; }

    public string OfficialLauncherRel { get; set; } = @"NIKKE\Launcher\nikke_launcher.exe";
    public string OfficialFallbackRel { get; set; } = @"pc_2179\nikkeminiloader_official.wg.intl.exe";
    public string PrivateLauncherRel { get; set; } = @"NIKKEG\Launcher\nikke_launcher.exe";
    public string ServerExeRel { get; set; } = @"EpinelPS\EpinelPS.exe";
    public string CaRel { get; set; } = @"EpinelPS\myCA.pem";
    public string NewsDirRel { get; set; } = @"EpinelPS\wwwroot\nikke_launcher";

    public int ServerPort { get; set; } = 443;
    public int ServerReadyTimeoutSec { get; set; } = 90;
    public string CaSubject { get; set; } = "CN=EpinelPS-CA";
    public string CaThumbprint { get; set; } = "";

    public string[] PrivateHosts { get; set; } = DefaultHosts();

    /// <summary>
    /// 官服 lobby 的候选 IP，按顺序做 TLS 握手探测，选第一个通的。
    ///
    /// 为什么必须钉 IP：官方把国际服域名 CNAME 到 black.nikke-kr.com，解析结果是 0.0.0.0 级黑洞，
    /// 公共 DNS（阿里/腾讯/Google/Cloudflare 实测）全部返回不可用地址。
    ///
    /// 为什么做成可编辑列表：官方入口会迁移。2026-11-11 实测：
    ///   124.156.233.126 → 404 / 0.25s（腾讯云新加坡，正常）
    ///   43.169.55.10    → 567 / 1.2s（AWS，被拒）
    ///   43.152.186.225  → 418 / 0.8s（AWS 北美，被拒）
    /// IP 失效时改这里即可，不用等新版本。
    /// </summary>
    public string[] LobbyCandidates { get; set; } = DefaultLobbyCandidates();

    public static string[] DefaultLobbyCandidates() => new[]
    {
        "124.156.233.126",
        "43.169.55.10",
        "43.152.186.225"
    };

    // ---------------------------------------------------------------- 外观

    /// <summary>自定义背景图，空表示纯色。路径失效自动回落，不报错。</summary>
    public string? BackgroundImage { get; set; }

    /// <summary>背景压暗程度 0~95，越大字越清楚。</summary>
    public int BackgroundDim { get; set; } = 65;

    /// <summary>cover=铺满裁切（默认）、contain=等比留边、stretch=拉伸</summary>
    public string BackgroundMode { get; set; } = "cover";

    /// <summary>裁切焦点水平位置 0~100（50 居中）。</summary>
    public int BackgroundPosX { get; set; } = 50;

    /// <summary>裁切焦点垂直位置 0~100（50 居中）。</summary>
    public int BackgroundPosY { get; set; } = 50;

    /// <summary>额外放大倍数 100~300，配合焦点位置可以框住想露出来的部分。</summary>
    public int BackgroundZoom { get; set; } = 100;

    /// <summary>dark / light / system，默认浅色</summary>
    public string ThemeMode { get; set; } = "light";

    public string Author { get; set; } = "嗷润吉";

    public string[] ExcludedDomains { get; set; } =
    {
        "na-community.playerinfinite.com",
        "cloud.nikke-kr.com",
        "cos-dev.nikke-kr.com"
    };

    [JsonIgnore] public string? ServerExePath => Join(PrivateRoot, ServerExeRel);
    [JsonIgnore] public string? CaPath => Join(PrivateRoot, CaRel);
    [JsonIgnore] public string? NewsDirPath => Join(PrivateRoot, NewsDirRel);
    [JsonIgnore] public string? PrivateLauncherPath => Join(PrivateRoot, PrivateLauncherRel);
    [JsonIgnore] public string? OfficialLauncherPath => Join(OfficialRoot, OfficialLauncherRel);
    [JsonIgnore] public string? OfficialFallbackPath => Join(OfficialRoot, OfficialFallbackRel);

    /// <summary>
    /// 进程扫描根。用启动器所在目录反推游戏根，而不是直接用用户填的根 ——
    /// 官服根填成盘符（D:\）时，拿它当扫描范围会把整个盘都算进来。
    /// 找不到启动器就退回用户填的根；服务端目录单独加一条（它不在客户端目录下）。
    /// </summary>
    [JsonIgnore]
    public string[] Roots
    {
        get
        {
            var list = new List<string>();

            var official = LauncherFinder.ResolveOfficial(this);
            var priv = LauncherFinder.Resolve(this);

            list.Add(official is not null ? GameRootOf(official) : OfficialRoot!);
            list.Add(priv is not null ? GameRootOf(priv) : PrivateRoot!);
            if (ServerExePath is not null) list.Add(Path.GetDirectoryName(ServerExePath)!);

            return list
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => r.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    /// <summary>...\NIKKEG\Launcher\nikke_launcher.exe → ...\NIKKEG</summary>
    static string GameRootOf(string exe)
    {
        var dir = Path.GetDirectoryName(exe);
        if (dir is null) return exe;

        return string.Equals(Path.GetFileName(dir), "Launcher", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(dir) ?? dir
            : dir;
    }

    static string? Join(string? root, string rel) =>
        string.IsNullOrWhiteSpace(root) ? null : Path.Combine(root, rel);

    public static string[] DefaultHosts() => new[]
    {
        "global-lobby.nikke-kr.com",
        "jp-lobby.nikke-kr.com",
        "us-lobby.nikke-kr.com",
        "kr-lobby.nikke-kr.com",
        "sea-lobby.nikke-kr.com",
        "hmt-lobby.nikke-kr.com",
        "aws-na-dr.intlgame.com",
        "sg-vas.intlgame.com",
        "aws-na.intlgame.com",
        "common-web.intlgame.com",
        "li-sg.intlgame.com",
        "data-aws-na.intlgame.com",
        "www.jupiterlauncher.com"
    };

    public AppConfig Clone() =>
        JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(this))!;
}

public static class ConfigStore
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string ExeDir => AppContext.BaseDirectory;
    public static string PrimaryPath => Path.Combine(ExeDir, "NikkeDualClient.json");
    public static string FallbackPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NikkeDualClient", "config.json");

    public static string DataDir => Directory.Exists(ExeDir) && IsWritable(ExeDir)
        ? ExeDir
        : Path.GetDirectoryName(FallbackPath)!;

    static bool IsWritable(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, $".w{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    public static AppConfig? Load()
    {
        foreach (var p in new[] { PrimaryPath, FallbackPath })
        {
            try
            {
                if (!File.Exists(p)) continue;
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(p));
                if (cfg is not null) return cfg;
            }
            catch { /* 配置损坏就当没有，走重新发现 */ }
        }
        return null;
    }

    public static string Save(AppConfig cfg)
    {
        var target = DataDir == ExeDir ? PrimaryPath : FallbackPath;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, JsonSerializer.Serialize(cfg, Options));
        return target;
    }
}
