using System.Reflection;
using Microsoft.Win32;

namespace RolanClone.Windows.Startup;

public static class AutostartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LaunchDock";
    private const string LegacyValueName = "RolanClone";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return;
            }

            // 清理旧版本遗留的自启项（指向已不存在的 RolanClone.exe）
            key.DeleteValue(LegacyValueName, throwOnMissingValue: false);

            if (enabled)
            {
                key.SetValue(ValueName, BuildCommand());
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // 自启写入失败不影响主流程
        }
    }

    private static string BuildCommand()
    {
        var processPath = Environment.ProcessPath ?? "";
        var entryAssembly = Assembly.GetEntryAssembly()?.Location;

        if (processPath.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(entryAssembly))
        {
            return $"\"{processPath}\" \"{entryAssembly}\"";
        }

        return $"\"{processPath}\"";
    }
}
