using System.Text;
using System.Text.Json;

namespace NikkeDualClient.Core;

/// <summary>
/// 补写 ServerSelector 的状态文件（serverselectorsettings.json）。
///
/// 说明：EpinelPS 服务端**并不读取**这个文件。对 EpinelPS.exe 做过全文检索
/// （UTF-16 和 UTF-8 两种编码），没有任何 serverselectorsettings / GameRoot /
/// ServerSelector 引用；而且现有配置里 GameRoot 指向已不存在的 E 盘，游戏照样能跑。
/// 这里写入纯粹是兼容性补写，让老的 ServerSelector 下次打开时不显示错路径。
/// </summary>
public static class SelectorSync
{
    /// <summary>只在现有 GameRoot 失效（缺失或目录不存在）时写入，返回写入的路径。</summary>
    public static string? WriteIfStale(AppConfig cfg, Action<string>? log = null)
    {
        var launcher = LauncherFinder.Resolve(cfg);
        var serverExe = cfg.ServerExePath;
        if (launcher is null || serverExe is null) return null;

        // ...\NIKKEG\Launcher\nikke_launcher.exe → ...\NIKKEG
        // 解析到的启动器不一定在 Launcher\ 下（也可能 ...\NIKKE\nikke_launcher.exe），按目录名判断而不是砍两层
        var launcherDir = Path.GetDirectoryName(launcher);
        if (launcherDir is null) return null;
        var gameRoot = string.Equals(Path.GetFileName(launcherDir), "Launcher", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(launcherDir)
            : launcherDir;
        var serverDir = Path.GetDirectoryName(serverExe);
        if (gameRoot is null || serverDir is null) return null;

        var file = Path.Combine(serverDir, "serverselectorsettings.json");

        // 现有值还有效就跳过，别覆盖用户手选的路径
        if (File.Exists(file))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (doc.RootElement.TryGetProperty("GameRoot", out var g))
                {
                    var current = g.GetString();
                    if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current)) return null;
                }
            }
            catch { }
        }

        try
        {
            if (File.Exists(file))
            {
                var backupDir = Path.Combine(ConfigStore.DataDir, "backup");
                Directory.CreateDirectory(backupDir);
                File.Copy(file, Path.Combine(backupDir,
                    $"serverselectorsettings_{DateTime.Now:yyyyMMdd_HHmmss}.json"), true);
            }

            var json = $"{{\"GameRoot\":{JsonSerializer.Serialize(gameRoot)},\"LastIp\":\"127.0.0.1\",\"LastOffset\":0}}";
            File.WriteAllText(file, json, new UTF8Encoding(false));
            log?.Invoke($"  选择器路径已同步：{gameRoot}");
            return gameRoot;
        }
        catch (Exception ex)
        {
            log?.Invoke($"  [注意] 选择器路径写入失败：{ex.Message}");
            return null;
        }
    }
}
