using System.Security.Cryptography.X509Certificates;

namespace NikkeDualClient.Core;

/// <summary>
/// 只按指纹装卸这一张私服 CA，不碰证书库里的其他东西。
/// 卸载失败（比如别处还在用）就如实报错，不做清库这种危险动作。
/// </summary>
public static class CertManager
{
    static X509Store OpenStore(OpenFlags flags)
    {
        var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
        store.Open(flags);
        return store;
    }

    /// <summary>返回匹配的指纹列表（可能不止一张，比如以前装过旧版本）。</summary>
    public static List<string> FindThumbprints(AppConfig cfg)
    {
        var hits = new List<string>();
        using var store = OpenStore(OpenFlags.ReadOnly);
        foreach (var c in store.Certificates)
        {
            var match = !string.IsNullOrEmpty(cfg.CaThumbprint)
                ? string.Equals(c.Thumbprint, cfg.CaThumbprint, StringComparison.OrdinalIgnoreCase)
                : c.Subject.Contains(cfg.CaSubject, StringComparison.OrdinalIgnoreCase);
            if (match) hits.Add(c.Thumbprint);
        }
        return hits;
    }

    public static bool IsInstalled(AppConfig cfg) => FindThumbprints(cfg).Count > 0;

    public static string Install(AppConfig cfg)
    {
        if (IsInstalled(cfg)) return cfg.CaThumbprint;

        var pem = cfg.CaPath;
        if (pem is null || !File.Exists(pem))
            throw new FileNotFoundException($"找不到私服根证书：{pem}");

        // 注意：不能用 CreateFromPemFile —— 它在 keyPemFilePath 为空时会去同一个文件里找私钥，
        // 而 myCA.pem 只有 CERTIFICATE 块，于是报 “key contents do not contain a PEM”。
        // CreateFromPem 只解析证书块，不碰私钥。
        var pemText = File.ReadAllText(pem);
        using var cert = X509Certificate2.CreateFromPem(pemText);
        using var store = OpenStore(OpenFlags.ReadWrite);
        store.Add(cert);

        var thumb = cert.Thumbprint;
        if (!FindThumbprints(cfg).Any(t => string.Equals(t, thumb, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"证书已写入，但按指纹 {thumb} 读不回来，请检查证书库权限");

        cfg.CaThumbprint = thumb;
        return thumb;
    }

    public static int Remove(AppConfig cfg)
    {
        var targets = FindThumbprints(cfg);
        if (targets.Count == 0) return 0;

        using var store = OpenStore(OpenFlags.ReadWrite);
        var removed = 0;
        foreach (var c in store.Certificates)
        {
            if (!targets.Contains(c.Thumbprint, StringComparer.OrdinalIgnoreCase)) continue;
            store.Remove(c);
            removed++;
        }
        return removed;
    }
}
