using System.IO.Compression;
using System.Text;

namespace NikkeDualClient.Core;

public sealed record NewsRepairResult(bool Changed, string? BackupDir, List<string> Actions, List<string> Warnings);

/// <summary>
/// 广告页是三层叠加的，必须逐层处理，只改一层都还会看到旧页：
///   1 本地 wwwroot 的 index.html（卖家页）
///   2 同目录的 .gz / .br 压缩副本（内容和主 HTML 不一致时，协商会命中旧页）
///   3 启动器 WebView 缓存（同一 URL 的页面被缓存）
/// </summary>
public static class NewsRepair
{
    static readonly string[] SellerMarks = { "taobao.com", "毛哥", "9.png", "QQ群" };

    public static bool IsPolluted(AppConfig cfg)
    {
        var dir = cfg.NewsDirPath;
        if (dir is null) return false;
        var html = Path.Combine(dir, "index.html");
        if (!File.Exists(html)) return false;
        var text = File.ReadAllText(html);
        return SellerMarks.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase));
    }

    public static NewsRepairResult Repair(AppConfig cfg)
    {
        var actions = new List<string>();
        var warnings = new List<string>();
        var dir = cfg.NewsDirPath;

        if (dir is null || !Directory.Exists(dir))
            return new NewsRepairResult(false, null, actions, new List<string> { $"新闻页目录不存在：{dir}" });

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var bakDir = Path.Combine(ConfigStore.DataDir, "backup", $"news_{stamp}");
        Directory.CreateDirectory(bakDir);

        // 1 备份原件
        foreach (var name in new[] { "index.html", "index.html.gz", "index.html.br", "9.png" })
        {
            var src = Path.Combine(dir, name);
            if (File.Exists(src))
            {
                File.Copy(src, Path.Combine(bakDir, name), true);
                actions.Add($"备份 {name}");
            }
        }

        // 2 推广图移出静态目录
        var promo = Path.Combine(dir, "9.png");
        if (File.Exists(promo))
        {
            File.Move(promo, Path.Combine(bakDir, "9.png"), true);
            actions.Add("9.png 移出 wwwroot");
        }

        // 3 写中性页
        var htmlPath = Path.Combine(dir, "index.html");
        File.WriteAllText(htmlPath, NeutralHtml, new UTF8Encoding(false));
        actions.Add("index.html 已重写");

        // 4 重算 gzip，移走 brotli
        var bytes = Encoding.UTF8.GetBytes(NeutralHtml);
        var gzPath = Path.Combine(dir, "index.html.gz");
        using (var fs = File.Create(gzPath))
        using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
            gz.Write(bytes, 0, bytes.Length);
        actions.Add($"index.html.gz 已重算（{new FileInfo(gzPath).Length} 字节）");

        var brPath = Path.Combine(dir, "index.html.br");
        if (File.Exists(brPath))
        {
            File.Move(brPath, Path.Combine(bakDir, "index.html.br"), true);
            actions.Add("index.html.br 移入备份（.NET 侧无 Brotli 编码器，移走让服务端回落）");
        }

        // 5 清 WebView 缓存，保留登录库与 Cookies
        var cacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "nikke_launcher", "tbs_cache");
        if (Directory.Exists(cacheRoot))
        {
            var cacheBak = Path.Combine(bakDir, "tbs_cache");
            Directory.CreateDirectory(cacheBak);
            foreach (var name in new[] { "Cache", "Code Cache", "GPUCache" })
            {
                var src = Path.Combine(cacheRoot, name);
                if (!Directory.Exists(src)) continue;
                var dst = Path.Combine(cacheBak, name);
                if (Directory.Exists(dst)) continue;
                try
                {
                    Directory.Move(src, dst);
                    actions.Add($"缓存 {name} 已移走");
                }
                catch (Exception ex)
                {
                    warnings.Add($"缓存 {name} 移走失败（启动器可能还开着）：{ex.Message}");
                }
            }
        }
        else
        {
            warnings.Add($"未找到启动器缓存目录：{cacheRoot}");
        }

        // 6 校验
        var check = File.ReadAllText(htmlPath);
        var left = SellerMarks.Where(m => check.Contains(m, StringComparison.OrdinalIgnoreCase)).ToList();
        if (left.Count > 0) warnings.Add("index.html 仍残留：" + string.Join(" / ", left));

        return new NewsRepairResult(true, bakDir, actions, warnings);
    }

    /// <summary>
    /// 中性公告页：不放任何外链、不引推广图、不写群号。
    /// 真正的修复是让新闻请求走官方（hosts 白名单排除新闻域名），这一页只是兜底。
    /// </summary>
    const string NeutralHtml = """
<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="UTF-8">
<meta http-equiv="Cache-Control" content="no-cache, no-store, must-revalidate">
<meta http-equiv="Pragma" content="no-cache">
<meta http-equiv="Expires" content="0">
<title>EpinelPS</title>
<style>
  html,body{margin:0;padding:0;min-height:100%;background:#0d0f16;color:#e6e8f0;
    font-family:"Microsoft YaHei","Segoe UI",sans-serif;}
  .wrap{max-width:900px;margin:0 auto;padding:26px 24px 40px;}
  h1{font-size:21px;font-weight:600;letter-spacing:.08em;color:#8fb4ff;margin:0 0 4px;}
  .sub{font-size:12px;color:#6b7280;margin-bottom:20px;}
  h2{font-size:14px;color:#c9d3ff;border-left:3px solid #4a6cf7;padding-left:10px;margin:20px 0 8px;}
  ul{margin:0;padding-left:20px;line-height:1.95;font-size:13px;color:#c7ccda;}
  code{background:#1a1f2e;padding:1px 6px;border-radius:3px;font-size:12px;color:#9fd0ff;}
  .tip{margin-top:24px;padding:11px 14px;background:#141a26;border:1px solid #232c40;
    border-radius:6px;font-size:12px;color:#8b93a7;line-height:1.8;}
</style>
</head>
<body>
<div class="wrap">
  <h1>EPINELPS</h1>
  <div class="sub">本机单机服务 · 非官方页面</div>

  <h2>运行状态</h2>
  <ul>
    <li>服务地址：<code>https://127.0.0.1</code></li>
    <li>本页由本机服务提供，官方新闻请用官服启动器查看。</li>
  </ul>

  <div class="tip">
    仍显示旧页面时，在工具里点一次「自检并修复」清理启动器 WebView 缓存，再重开启动器。
  </div>
</div>
</body>
</html>
""";
}
