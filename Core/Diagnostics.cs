using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace NikkeDualClient.Core;

/// <summary>
/// 导出诊断信息。目标是让用户反馈问题时只发一个 txt，
/// 里面能还原出「什么环境、卡在哪一步」。
/// </summary>
public static class Diagnostics
{
    public static string BuildReport(AppConfig cfg)
    {
        var sb = new StringBuilder();

        sb.AppendLine("=== NIKKE 双服切换 · 诊断日志 ===");
        sb.AppendLine($"导出时间    {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"工具版本    {typeof(Diagnostics).Assembly.GetName().Version}");
        sb.AppendLine($"系统        {Environment.OSVersion}");
        sb.AppendLine($"64 位进程   {Environment.Is64BitProcess}");
        sb.AppendLine($"管理员权限  {(IsAdmin() ? "是" : "否")}");
        sb.AppendLine($"进程架构    {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine($"配置目录    {ConfigStore.DataDir}");
        sb.AppendLine();

        sb.AppendLine("=== 路径 ===");
        sb.AppendLine($"官服根      {cfg.OfficialRoot ?? "(未设置)"}");
        sb.AppendLine($"私服根      {cfg.PrivateRoot ?? "(未设置)"}");
        sb.AppendLine($"服务端      {cfg.ServerExePath ?? "(未设置)"}   {(File.Exists(cfg.ServerExePath) ? "√" : "×")}");
        var hitOfficial = LauncherFinder.ResolveOfficial(cfg);
        sb.AppendLine($"官服启动器  {hitOfficial ?? "(未找到)"}   {(hitOfficial is null ? "×" : "√")}");
        var hitLauncher = LauncherFinder.Resolve(cfg);
        sb.AppendLine($"私服启动器  {hitLauncher ?? "(未找到)"}   {(hitLauncher is null ? "×" : "√")}");
        sb.AppendLine($"主题 / 背景  {cfg.ThemeMode} / {(cfg.BackgroundImage is null ? "纯色" : Path.GetFileName(cfg.BackgroundImage))}");
        sb.AppendLine();

        sb.AppendLine("=== 授权文件（EpinelPS\\auth.txt）===");
        try
        {
            var serverDir = cfg.ServerExePath is null ? null : Path.GetDirectoryName(cfg.ServerExePath);
            var authPath = serverDir is null ? null : Path.Combine(serverDir, "auth.txt");

            if (authPath is not null && File.Exists(authPath))
            {
                var info = new FileInfo(authPath);
                var raw = File.ReadAllText(authPath).Trim();
                var parts = raw.Split('=', 2);

                // 订单号是购买凭证，只留前 4 位，免得诊断文件转发出去泄露
                var order = parts.Length > 0 ? parts[0] : "";
                var masked = order.Length > 4 ? order[..4] + "***" : "***";
                var bound = parts.Length > 1 ? parts[1].Trim() : "(无)";

                var cpu = CpuId();
                var match = cpu is not null && bound.Equals(cpu, StringComparison.OrdinalIgnoreCase);

                sb.AppendLine($"文件            {authPath}");
                sb.AppendLine($"状态            存在，{info.Length} 字节，改于 {info.LastWriteTime:yyyy-MM-dd HH:mm}");
                sb.AppendLine($"订单号          {masked}");
                sb.AppendLine($"绑定机器码      {bound}");
                sb.AppendLine($"本机 CPU ID     {cpu ?? "(读取失败)"}");
                sb.AppendLine($"机器码匹配      {(match ? "是" : "否 —— 这个 auth.txt 不是本机激活的")}");
            }
            else
            {
                sb.AppendLine("状态            不存在（服务端可能要求激活）");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"读取失败：{ex.Message}");
        }
        sb.AppendLine();

        sb.AppendLine("=== 环境状态 ===");
        try
        {
            var s = EnvController.Check(cfg);
            sb.AppendLine($"模式        {s.ModeText}");
            sb.AppendLine($"hosts 区块  {(s.HostsBlock ? "有" : "无")}");
            sb.AppendLine($"新闻域名    {(s.NewsHijacked ? "被劫持" : "正常")}");
            sb.AppendLine($"根证书      {(s.CaInstalled ? "已安装" : "未安装")}");
            sb.AppendLine($"443         {(s.PortOpen ? "监听中" : "未监听")}");
            sb.AppendLine($"新闻页      {(s.NewsPolluted ? "含推广" : "干净")}");
            sb.AppendLine();
            sb.AppendLine("原始 JSON:");
            sb.AppendLine(JsonSerializer.Serialize(s, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }));
        }
        catch (Exception ex)
        {
            sb.AppendLine($"体检失败：{ex.Message}");
        }
        sb.AppendLine();

        sb.AppendLine("=== 相关进程 ===");
        try
        {
            var procs = ProcessScanner.Find(cfg);
            if (procs.Count == 0) sb.AppendLine("(无)");
            else
            {
                foreach (var p in procs)
                {
                    var tag = ProcessScanner.IsClient(p.Name) ? "客户端"
                        : string.Equals(p.Path, cfg.ServerExePath, StringComparison.OrdinalIgnoreCase) ? "服务端"
                        : "其他";
                    sb.AppendLine($"[{tag}] {p.Name}({p.Id})  {p.Path}");
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"枚举失败：{ex.Message}");
        }
        sb.AppendLine();

        sb.AppendLine("=== 运行日志（本次会话）===");
        sb.AppendLine(AppLog.Dump());
        sb.AppendLine();
        sb.AppendLine($"=== 历史日志（{Path.GetFileName(AppLog.LogFile)} 最近 100 行）===");
        sb.AppendLine(AppLog.Tail(100));

        var crash = Path.Combine(ConfigStore.DataDir, "crash.log");
        if (File.Exists(crash))
        {
            sb.AppendLine();
            sb.AppendLine("=== crash.log ===");
            try { sb.AppendLine(File.ReadAllText(crash)); } catch { }
        }

        return sb.ToString();
    }

    static bool IsAdmin()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    /// <summary>读 CPU 序列号，用来和 auth.txt 里绑定的机器码比对。</summary>
    static string? CpuId()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT ProcessorId FROM Win32_Processor");

            foreach (System.Management.ManagementObject mo in searcher.Get())
            {
                using (mo) return mo["ProcessorId"]?.ToString();
            }
        }
        catch { }
        return null;
    }
}
