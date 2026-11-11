using System.Drawing;
using System.Reflection;

namespace NikkeDualClient.Ui;

/// <summary>
/// 窗体图标从嵌入资源读。不用 Icon.ExtractAssociatedIcon —— 单文件发布会解压到临时目录，路径不可靠。
/// </summary>
internal static class AppIcon
{
    static Icon? _cached;

    public static Icon Load(int size = 32)
    {
        if (_cached is not null) return _cached;

        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("NikkeDualClient.app.ico");
            if (stream is not null)
            {
                _cached = new Icon(stream, size, size);
                return _cached;
            }
        }
        catch { }

        _cached = SystemIcons.Application;
        return _cached;
    }
}
