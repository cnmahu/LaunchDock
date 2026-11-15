using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace RolanClone.Windows.Shortcuts;

/// <summary>创建指向本程序的 .lnk 快捷方式（桌面 / 任务栏固定目录）。</summary>
public static class ShortcutService
{
    private const string AppName = "启坞 LaunchDock";

    /// <summary>优先使用同目录下的 apphost，否则退回到当前进程。</summary>
    public static string ExecutablePath
    {
        get
        {
            var exe = Path.Combine(AppContext.BaseDirectory, "RolanClone.exe");
            return File.Exists(exe) ? exe : Environment.ProcessPath ?? exe;
        }
    }

    public static bool CreateDesktopShortcut()
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return Create(Path.Combine(folder, $"{AppName}.lnk"));
    }

    public static bool CreateTaskbarShortcut()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");

        try
        {
            Directory.CreateDirectory(folder);
        }
        catch
        {
            return false;
        }

        return Create(Path.Combine(folder, $"{AppName}.lnk"));
    }

    private static bool Create(string linkPath)
    {
        if (File.Exists(linkPath))
        {
            return true;
        }

        try
        {
            var link = (IShellLinkW)new ShellLink();
            link.SetPath(ExecutablePath);
            link.SetWorkingDirectory(AppContext.BaseDirectory);
            link.SetDescription(AppName);
            ((IPersistFile)link).Save(linkPath, true);
            return File.Exists(linkPath);
        }
        catch
        {
            return false;
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch,
            IntPtr pfd, uint fFlags);

        void GetIDList(out IntPtr ppidl);

        void SetIDList(IntPtr pidl);

        void GetDescription([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        void GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

        void GetArguments([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

        void GetHotkey(out short pwHotkey);

        void SetHotkey(short wHotkey);

        void GetShowCmd(out int piShowCmd);

        void SetShowCmd(int iShowCmd);

        void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch,
            out int piIcon);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);

        void Resolve(IntPtr hwnd, uint fFlags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
