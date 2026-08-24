using Microsoft.Win32;

namespace ApiCallInter.Services;

public interface IAutoStartManager { bool IsEnabled(); void SetEnabled(bool enabled); }

/// <summary>用户级注册表 Run 键开机自启（spec 4.3），与 Web 设置页共用同一真源。</summary>
public class RegistryAutoStartManager : IAutoStartManager
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ApiCallInter";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        // 兼容读取带引号（新）与不带引号（旧安装）两种值，均按去掉引号后与当前 exe 全路径比对
        return key?.GetValue(ValueName) is string v
            && Environment.ProcessPath is { } exe
            && string.Equals(v.Trim('"'), exe, StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        // Run 键值按命令行解析：路径含空格时必须加引号，否则 Windows 会把 "C:\Program" 当可执行文件而静默失效
        if (enabled) key.SetValue(ValueName, $"\"{Environment.ProcessPath!}\"");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
