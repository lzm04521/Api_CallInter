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
        var appDir = AppContext.BaseDirectory;
        var logPath = Path.Combine(AppPaths.LogsDir, "update.log");
        Process.Start("powershell", $"-ExecutionPolicy Bypass -File \"{Path.Combine(appDir, "update.ps1")}\" -OldPid {Environment.ProcessId} -SrcDir \"{srcDir}\" -AppDir \"{appDir}\" -LogPath \"{logPath}\"");
        ExitApplication();
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
