using LaunchDock.Core.Model;

namespace LaunchDock.Windows.Library;

/// <summary>
/// 收集系统里可以被添加到启动板的内容：标准文件夹、系统工具、开始菜单应用、商店应用。
/// </summary>
public static class AppLibraryService
{
    public static IReadOnlyList<LibraryEntry> GetStandardFolders()
    {
        var list = new List<LibraryEntry>
        {
            Folder("此电脑", "shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"),
            Folder("回收站", "shell:::{645FF040-5081-101B-9F08-00AA002F954E}"),
            Folder("桌面", "shell:Desktop"),
            Folder("下载", "shell:Downloads"),
            Folder("文档", "shell:Personal"),
            // 这三个是带空格的规范别名，不带空格时 Shell 解析不到图标
            Folder("图片", "shell:My Pictures"),
            Folder("音乐", "shell:My Music"),
            Folder("视频", "shell:My Video"),
            Folder("网络", "shell:NetworkPlacesFolder"),
            Folder("网络连接", "shell:::{7007ACC7-3202-11D1-AAD2-00805FC1270E}"),
            Folder("打印机", "shell:::{2227A280-3AEA-1069-A2DE-08002B30309D}"),
            Folder("所有控制面板项", "shell:::{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}")
        };

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady)
                {
                    continue;
                }

                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? (drive.DriveType == DriveType.Removable ? "可移动磁盘" : "本地磁盘")
                    : drive.VolumeLabel;

                list.Add(new LibraryEntry($"{label} ({drive.Name.TrimEnd('\\')})",
                    drive.RootDirectory.FullName, ItemKind.Folder));
            }
        }
        catch
        {
            // 驱动器枚举失败时保留已知文件夹
        }

        return list;
    }

    public static IReadOnlyList<LibraryEntry> GetSystemTools()
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var entries = new (string Name, string File)[]
        {
            ("本地安全策略", "secpol.msc"),
            ("步骤记录器", "psr.exe"),
            ("磁盘清理", "cleanmgr.exe"),
            ("打印管理", "printmanagement.msc"),
            ("服务", "services.msc"),
            ("高级安全 Windows 防火墙", "wf.msc"),
            ("恢复驱动器", "RecoveryDrive.exe"),
            ("计算机管理", "compmgmt.msc"),
            ("控制面板", "control.exe"),
            ("命令提示符", "cmd.exe"),
            ("任务管理器", "taskmgr.exe"),
            ("任务计划程序", "taskschd.msc"),
            ("事件查看器", "eventvwr.msc"),
            ("碎片整理和优化驱动器", "dfrgui.exe"),
            ("系统配置", "msconfig.exe"),
            ("系统信息", "msinfo32.exe"),
            ("性能监视器", "perfmon.msc"),
            ("远程桌面连接", "mstsc.exe"),
            ("注册表编辑器", "regedit.exe"),
            ("资源监视器", "resmon.exe"),
            ("字符映射表", "charmap.exe"),
            ("组件服务", "dcomcnfg.exe"),
            ("iSCSI 发起程序", "iscsicpl.exe"),
            ("ODBC 数据源 (64 位)", "odbcad32.exe"),
            ("Windows 内存诊断", "MdSched.exe"),
            ("Windows Media Player", "wmplayer.exe")
        };

        var list = new List<LibraryEntry>();
        foreach (var (name, file) in entries)
        {
            var path = Path.Combine(system, file);
            if (File.Exists(path))
            {
                list.Add(new LibraryEntry(name, path, ItemKind.Exe));
            }
        }

        return list;
    }

    public static IReadOnlyList<LibraryEntry> GetDesktopApps()
    {
        var paths = new List<string>();
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
                 })
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var programs = Path.Combine(root, "Programs");
            if (!Directory.Exists(programs))
            {
                continue;
            }

            try
            {
                paths.AddRange(Directory.EnumerateFiles(programs, "*.lnk", SearchOption.AllDirectories));
            }
            catch
            {
                // 个别目录无权限时跳过
            }
        }

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (Directory.Exists(desktop))
        {
            try
            {
                paths.AddRange(Directory.EnumerateFiles(desktop, "*.lnk", SearchOption.TopDirectoryOnly));
            }
            catch
            {
                // 忽略
            }
        }

        var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        var list = new List<LibraryEntry>();
        foreach (var path in paths)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(name) || !seen.Add(name))
            {
                continue;
            }

            list.Add(new LibraryEntry(name, path, ItemKind.Exe));
        }

        list.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.CurrentCulture));
        return list;
    }

    public static IReadOnlyList<LibraryEntry> GetStoreApps()
    {
        var list = new List<LibraryEntry>();

        try
        {
            var manager = new global::Windows.Management.Deployment.PackageManager();
            foreach (var package in manager.FindPackagesForUser(string.Empty))
            {
                try
                {
                    var packageName = package.DisplayName;
                    foreach (var entry in package.GetAppListEntries())
                    {
                        var name = entry.DisplayInfo.DisplayName;
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            name = packageName;
                        }

                        if (string.IsNullOrWhiteSpace(name))
                        {
                            name = entry.AppUserModelId;
                        }

                        list.Add(new LibraryEntry(name, entry.AppUserModelId, ItemKind.Uwp));
                    }
                }
                catch
                {
                    // 单个包读取失败不影响整体
                }
            }
        }
        catch
        {
            // 当前系统不支持商店应用枚举时返回空列表
        }

        list.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.CurrentCulture));
        return list;
    }

    private static LibraryEntry Folder(string name, string shellPath) =>
        new(name, "explorer.exe", ItemKind.Folder, shellPath, shellPath);
}
