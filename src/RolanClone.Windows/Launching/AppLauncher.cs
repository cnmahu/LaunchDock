using System.Diagnostics;
using RolanClone.Core.Model;

namespace RolanClone.Windows.Launching;

public static class AppLauncher
{
    /// <summary>启动一个条目，返回是否成功发起了启动。</summary>
    public static bool Launch(ItemModel item, bool forceAdmin = false)
    {
        if (string.IsNullOrWhiteSpace(item.Target) && item.Kind != ItemKind.Builtin)
        {
            return false;
        }

        if (item.Kind == ItemKind.Uwp)
        {
            return UwpActivator.Activate(item.Target, item.Arguments);
        }

        if (item.Kind == ItemKind.Builtin)
        {
            return BuiltinActions.Run(item.Target);
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = item.Target,
                UseShellExecute = true
            };

            if (!string.IsNullOrWhiteSpace(item.Arguments))
            {
                startInfo.Arguments = item.Arguments;
            }

            if (!string.IsNullOrWhiteSpace(item.WorkingDirectory))
            {
                startInfo.WorkingDirectory = item.WorkingDirectory;
            }

            if (item.RunAsAdmin || forceAdmin)
            {
                startInfo.Verb = "runas";
            }

            Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"启动失败: {item.Target} -> {ex.Message}");
            return false;
        }
    }
}
