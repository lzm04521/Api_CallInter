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
        return key?.GetValue(ValueName) is string;
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, Environment.ProcessPath!);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
