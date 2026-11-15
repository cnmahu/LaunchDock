using RolanClone.Core.Model;

namespace RolanClone.Core.Storage;

/// <summary>
/// 首次运行时写入的示例数据：结构与真实启动板一致，图标在 M1 阶段接入提取器。
/// </summary>
public static class SampleData
{
    public static AppConfig CreateDefault()
    {
        var config = new AppConfig();

        config.Tabs.Add(Tab("开发", 0,
            ("Visual Studio", ItemKind.Exe, @"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\devenv.exe"),
            ("VS Code", ItemKind.Exe, @"C:\Users\Public\AppData\Local\Programs\Microsoft VS Code\Code.exe"),
            ("Git Bash", ItemKind.Exe, @"C:\Program Files\Git\git-bash.exe"),
            ("Windows Terminal", ItemKind.Uwp, "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App"),
            ("PowerShell", ItemKind.Exe, "powershell.exe"),
            ("Node.js", ItemKind.Exe, @"C:\Program Files\nodejs\node.exe"),
            ("Python", ItemKind.Exe, "python.exe"),
            ("Docker Desktop", ItemKind.Exe, @"C:\Program Files\Docker\Docker\Docker Desktop.exe")));

        config.Tabs.Add(Tab("通讯", 1,
            ("微信", ItemKind.Exe, @"C:\Program Files\Tencent\WeChat\WeChat.exe"),
            ("QQ", ItemKind.Exe, @"C:\Program Files\Tencent\QQNT\QQ.exe"),
            ("钉钉", ItemKind.Exe, @"C:\Program Files\DingDing\DingtalkLauncher.exe"),
            ("飞书", ItemKind.Exe, @"C:\Program Files\Lark\Lark.exe"),
            ("企业微信", ItemKind.Exe, @"C:\Program Files\WXWork\WXWork.exe"),
            ("Telegram", ItemKind.Exe, "telegram.exe"),
            ("Discord", ItemKind.Exe, "discord.exe"),
            ("Outlook", ItemKind.Exe, "outlook.exe")));

        config.Tabs.Add(Tab("工具", 2,
            ("计算器", ItemKind.Exe, "calc.exe"),
            ("记事本", ItemKind.Exe, "notepad.exe"),
            ("截图工具", ItemKind.Exe, "snippingtool.exe"),
            ("任务管理器", ItemKind.Exe, "taskmgr.exe"),
            ("控制面板", ItemKind.Command, "control"),
            ("注册表编辑器", ItemKind.Exe, "regedit.exe"),
            ("命令提示符", ItemKind.Exe, "cmd.exe"),
            ("画图", ItemKind.Exe, "mspaint.exe")));

        config.Tabs.Add(Tab("游戏", 3,
            ("Steam", ItemKind.Exe, @"C:\Program Files (x86)\Steam\steam.exe"),
            ("Epic Games", ItemKind.Exe, @"C:\Program Files (x86)\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe"),
            ("Battle.net", ItemKind.Exe, @"C:\Program Files (x86)\Battle.net\Battle.net Launcher.exe"),
            ("Xbox", ItemKind.Uwp, "Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App"),
            ("原神", ItemKind.Exe, @"C:\Program Files\Genshin Impact\Genshin Impact Game\YuanShen.exe"),
            ("鸣潮", ItemKind.Exe, @"C:\Wuthering Waves\Wuthering Waves Game\Client-Win64-Shipping.exe"),
            ("Minecraft", ItemKind.Exe, "minecraft.exe"),
            ("Rolan 官网", ItemKind.Url, "https://getrolan.com/")));

        config.Tabs.Add(Tab("影音", 4,
            ("PotPlayer", ItemKind.Exe, @"C:\Program Files\DAUM\PotPlayer\PotPlayerMini64.exe"),
            ("VLC", ItemKind.Exe, @"C:\Program Files\VideoLAN\VLC\vlc.exe"),
            ("网易云音乐", ItemKind.Exe, @"C:\Program Files\NetEase\CloudMusic\cloudmusic.exe"),
            ("QQ 音乐", ItemKind.Exe, @"C:\Program Files\Tencent\QQMusic\QQMusic.exe"),
            ("哔哩哔哩", ItemKind.Url, "https://www.bilibili.com/"),
            ("YouTube", ItemKind.Url, "https://www.youtube.com/"),
            ("Spotify", ItemKind.Exe, "spotify.exe"),
            ("媒体播放器", ItemKind.Uwp, "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic")));

        config.Window.SelectedTabId = config.Tabs[0].Id;
        return config;
    }

    private static TabModel Tab(string name, int order, params (string Name, ItemKind Kind, string Target)[] items)
    {
        var tab = new TabModel { Name = name, Order = order };
        for (var i = 0; i < items.Length; i++)
        {
            tab.Items.Add(new ItemModel
            {
                DisplayName = items[i].Name,
                Kind = items[i].Kind,
                Target = items[i].Target,
                Order = i
            });
        }

        return tab;
    }
}
