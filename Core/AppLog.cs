using System.Text;

namespace NikkeDualClient.Core;

/// <summary>
/// 运行日志。内存里保留最近一批供“导出日志”打包，同时追加写盘，
/// 这样程序崩溃或者被强杀之后，还能从 runtime.log 里翻到死在哪里。
/// </summary>
public static class AppLog
{
    const int Capacity = 800;
    const long MaxFileBytes = 2 * 1024 * 1024;

    static readonly List<string> Lines = new();
    static readonly object Gate = new();

    public static string LogFile => Path.Combine(ConfigStore.DataDir, "runtime.log");

    /// <summary>启动时打个分隔，顺手把超过 2 MB 的旧日志滚成 .old。</summary>
    public static void Start()
    {
        try
        {
            var file = LogFile;
            if (File.Exists(file) && new FileInfo(file).Length > MaxFileBytes)
                File.Move(file, file + ".old", overwrite: true);

            File.AppendAllText(file,
                $"===== 启动 {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====={Environment.NewLine}",
                Encoding.UTF8);
        }
        catch { /* 日志写不了不能影响主流程 */ }
    }

    public static void Add(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";

        lock (Gate)
        {
            Lines.Add(line);
            if (Lines.Count > Capacity) Lines.RemoveRange(0, Lines.Count - Capacity);
        }

        try { File.AppendAllText(LogFile, line + Environment.NewLine, Encoding.UTF8); }
        catch { }
    }

    public static string Dump()
    {
        lock (Gate)
        {
            return Lines.Count == 0
                ? "(本次运行还没有日志)"
                : string.Join(Environment.NewLine, Lines);
        }
    }

    /// <summary>读磁盘日志的最后若干行，用于跨会话排查。</summary>
    public static string Tail(int count)
    {
        try
        {
            if (!File.Exists(LogFile)) return "(无)";
            var all = File.ReadAllLines(LogFile);
            if (all.Length == 0) return "(无)";

            var take = Math.Min(count, all.Length);
            return string.Join(Environment.NewLine, all.Skip(all.Length - take));
        }
        catch (Exception ex) { return $"(读取失败：{ex.Message})"; }
    }
}
