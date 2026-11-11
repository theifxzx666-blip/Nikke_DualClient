using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;

namespace NikkeDualClient.Core;

/// <summary>
/// 官方 lobby 入口探测。
///
/// 官方在 DNS 层把国际服域名 CNAME 到 black.nikke-kr.com（解析为 0.0.0.1），
/// 公共 DNS 拿不到可用地址，只能靠候选 IP 硬连。入口会迁移，
/// 所以每次切官服都重新探一次，而不是把某个 IP 写死在代码里。
/// </summary>
public static class OfficialProbe
{
    /// <summary>用哪个域名做 SNI —— 官方证书是 *.nikke-kr.com，任一 lobby 域名都能通过校验。</summary>
    const string Sni = "global-lobby.nikke-kr.com";

    public readonly record struct Result(string Ip, int LatencyMs, bool Ok)
    {
        public string Text => Ok ? $"{Ip}（{LatencyMs} ms）" : "全部不可达";
    }

    /// <summary>按顺序探测，返回第一个握手成功的 IP；全失败返回 Ok=false。</summary>
    public static Result Probe(string[] candidates, int timeoutMs = 1500, Action<string>? log = null)
    {
        foreach (var raw in candidates)
        {
            var ip = raw?.Trim();
            if (string.IsNullOrEmpty(ip)) continue;

            var ms = TryHandshake(ip, timeoutMs);
            if (ms >= 0)
            {
                log?.Invoke($"  入口 {ip} 可用（{ms} ms）");
                return new Result(ip, ms, true);
            }
            log?.Invoke($"  入口 {ip} 不可用，试下一个");
        }
        return new Result("", -1, false);
    }

    /// <summary>返回 TLS 握手耗时（毫秒）；失败返回 -1。</summary>
    static int TryHandshake(string ip, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var tcp = new TcpClient();

            // ConnectAsync 不接受毫秒超时，用 Wait 控制
            if (!tcp.ConnectAsync(ip, 443).Wait(timeoutMs)) return -1;

            // 只连上端口不够 —— 被劫持/黑洞的 IP 也能完成 TCP 握手。
            // 必须做一次带 SNI 的 TLS 握手，证书对得上才算真入口。
            using var ssl = new SslStream(tcp.GetStream(), false);
            if (!ssl.AuthenticateAsClientAsync(Sni).Wait(timeoutMs)) return -1;

            return (int)sw.ElapsedMilliseconds;
        }
        catch
        {
            return -1;
        }
    }
}
