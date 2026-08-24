using System.Diagnostics;

namespace ApiCallInter.Services;

public static class AppRestarter
{
    /// <summary>普通重启：拉起带 --delayed-start 的新实例（等旧进程退出释放端口/Mutex），当前进程随后退出。</summary>
    public static void RestartDelayed()
    {
        Process.Start(Environment.ProcessPath!, "--delayed-start 3000");
        ExitApplication();
    }

    /// <summary>升级重启：启动 update.ps1（等旧进程退出→覆盖→拉起新版），当前进程随后退出。</summary>
    public static void StartUpdaterAndExit(string srcDir)
    {
        Process.Start(BuildUpdaterStartInfo(srcDir));
        ExitApplication();
    }

    /// <summary>
    /// 构造 update.ps1 启动参数。AppContext.BaseDirectory 必去尾反斜杠：
    /// 手工拼命令行时 `"...\"` 的反斜杠会被 CommandLineToArgvW 当作转义引号，
    /// -AppDir 与 -LogPath 粘连成坏 token，脚本在 Start-Transcript 即失败（旧进程已退出，应用被留在关机状态）。
    /// 改用 ArgumentList 由 .NET 逐参数负责转义。
    /// </summary>
    internal static ProcessStartInfo BuildUpdaterStartInfo(string srcDir)
    {
        var appDir = AppContext.BaseDirectory.TrimEnd('\\');
        var psi = new ProcessStartInfo("powershell")
        {
            ArgumentList =
            {
                "-ExecutionPolicy", "Bypass",
                "-File", Path.Combine(appDir, "update.ps1"),
                "-OldPid", Environment.ProcessId.ToString(),
                "-SrcDir", srcDir,
                "-AppDir", appDir,
                "-LogPath", Path.Combine(AppPaths.LogsDir, "update.log"),
            }
        };
        return psi;
    }

    /// <summary>
    /// 退出当前进程。重启会从 Web 请求线程调用（非 UI 线程）：
    /// 优先 Application.Exit() 走正常消息循环退出；跨线程调用若抛异常则 Environment.Exit(0) 兜底，确保进程绝不卡死。
    /// </summary>
    public static void ExitApplication()
    {
        try { System.Windows.Forms.Application.Exit(); }
        catch { Environment.Exit(0); }
    }
}
