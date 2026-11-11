using System.ComponentModel;
using System.Runtime.InteropServices;

namespace NikkeDualClient.Core;

/// <summary>
/// 启动外部程序专用。为什么不用 Process.Start：
///   1) Process.Start 在会话结束时会连带杀掉子进程（父进程挂在作业对象上），
///      表现就是“服务已就绪”，下次打开工具却查不到进程、443 不监听。
///   2) 这里用 CREATE_BREAKAWAY_FROM_JOB 主动脱离作业对象，服务才能真正常驻。
/// 顺带一提：CreateProcess 不对路径做通配符解析，'[Game]' 这种目录名不会被当字符集。
/// </summary>
internal static class ProcessLauncher
{
    const uint CREATE_BREAKAWAY_FROM_JOB = 0x01000000;
    const uint CREATE_NEW_CONSOLE = 0x00000010;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcess(
        string? lpApplicationName, string lpCommandLine,
        IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles,
        uint dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr handle);

    /// <summary>返回新进程 PID。newConsole=true 给控制台程序开独立窗口。</summary>
    public static int Start(string exe, string? workDir, bool newConsole)
    {
        // 首选 WMI：新进程的父进程是 WmiPrvSE.exe，既不在调用方的进程树里，
        // 也不挂调用方的作业对象，调用方退出不会带走它。
        var byWmi = TryStartViaWmi(exe, workDir);
        if (byWmi > 0) return byWmi;

        // 回退：CreateProcess（至少比 Process.Start 多一个 breakaway 机会）
        var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
        var cmd = "\"" + exe + "\"";
        var flags = CREATE_BREAKAWAY_FROM_JOB | (newConsole ? CREATE_NEW_CONSOLE : 0u);

        if (!CreateProcess(null, cmd, IntPtr.Zero, IntPtr.Zero, false, flags, IntPtr.Zero, workDir, ref si, out var pi))
        {
            flags &= ~CREATE_BREAKAWAY_FROM_JOB;
            if (!CreateProcess(null, cmd, IntPtr.Zero, IntPtr.Zero, false, flags, IntPtr.Zero, workDir, ref si, out pi))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"无法启动：{exe}");
        }

        try { return pi.dwProcessId; }
        finally
        {
            if (pi.hProcess != IntPtr.Zero) CloseHandle(pi.hProcess);
            if (pi.hThread != IntPtr.Zero) CloseHandle(pi.hThread);
        }
    }

    static int TryStartViaWmi(string exe, string? workDir)
    {
        try
        {
            using var cls = new System.Management.ManagementClass("Win32_Process");
            using var inParams = cls.GetMethodParameters("Create");
            inParams["CommandLine"] = "\"" + exe + "\"";
            if (!string.IsNullOrEmpty(workDir)) inParams["CurrentDirectory"] = workDir;

            using var outParams = cls.InvokeMethod("Create", inParams, null);
            var code = Convert.ToUInt32(outParams?["ReturnValue"] ?? 1u);
            if (code != 0) return 0;
            return Convert.ToInt32(outParams?["ProcessId"] ?? 0);
        }
        catch { return 0; }
    }
}
