namespace RolanClone.Core.Model;

public enum PanelLayout
{
    Horizontal,
    Vertical,
    SinglePage
}

public enum LabelPosition
{
    Bottom,
    Right,
    None
}

public enum AppTheme
{
    FollowSystem,
    Light,
    Dark
}

public enum WindowMaterial
{
    Acrylic,
    Mica,
    Solid
}

/// <summary>点击标题栏关闭按钮时的行为。</summary>
public enum CloseBehavior
{
    /// <summary>直接退出程序。</summary>
    Exit,
    /// <summary>隐藏到系统托盘，后台继续运行。</summary>
    MinimizeToTray
}

/// <summary>界面基础字重。</summary>
public enum AppFontWeight
{
    Light,
    Regular,
    Medium,
    SemiBold,
    Bold
}

public enum ShowPosition
{
    /// <summary>保持上次的位置。</summary>
    Current,
    /// <summary>每次唤出都居中显示在主屏。</summary>
    Center,
    /// <summary>在鼠标指针附近唤出。</summary>
    Mouse
}

public sealed class SettingsModel
{
    public PanelLayout Layout { get; set; } = PanelLayout.Horizontal;

    public AppTheme Theme { get; set; } = AppTheme.FollowSystem;

    public WindowMaterial Material { get; set; } = WindowMaterial.Acrylic;

    /// <summary>图标边长（像素）。</summary>
    public int IconSizePx { get; set; } = 48;

    /// <summary>图标之间的固定间距（像素），不随窗口大小变化。</summary>
    public double IconSpacing { get; set; } = 24;

    public LabelPosition LabelPosition { get; set; } = LabelPosition.Bottom;

    public double Opacity { get; set; } = 0.96;
    public bool Blur { get; set; } = true;
    public bool Animation { get; set; } = true;
    public bool TopMost { get; set; }
    public bool EdgeSnap { get; set; } = true;
    public bool LockPosition { get; set; }

    public bool AutoStart { get; set; }
    public bool StartHidden { get; set; }
    public bool TrayIcon { get; set; } = true;
    public bool HideOnLostFocus { get; set; } = true;
    public bool HideAfterLaunch { get; set; }
    public bool HideOnEmptyDoubleClick { get; set; } = true;
    public bool CtrlPreventHide { get; set; } = true;

    public string? LockPasswordHash { get; set; }
    public bool AutoLock { get; set; }

    /// <summary>true 表示单击图标即打开；false 表示双击才运行。</summary>
    public bool SingleClickRun { get; set; } = true;

    public bool AutoSortByUsage { get; set; }
    public string Hotkey { get; set; } = "Alt+Space";

    /// <summary>是否启用全局键盘快捷键。</summary>
    public bool EnableHotkey { get; set; } = true;

    /// <summary>唤出面板时的位置策略。</summary>
    public ShowPosition ShowPosition { get; set; } = ShowPosition.Current;

    /// <summary>按下鼠标扩展键 1（后退键）唤出面板。</summary>
    public bool ShowOnXButton1 { get; set; }

    /// <summary>按下鼠标扩展键 2（前进键）唤出面板。</summary>
    public bool ShowOnXButton2 { get; set; }

    /// <summary>按下鼠标中键唤出面板。</summary>
    public bool ShowOnMiddleClick { get; set; }

    /// <summary>按下 Esc 收起面板。</summary>
    public bool CloseOnEsc { get; set; } = true;

    /// <summary>便签存储目录；为空时使用程序目录下的 Notes 子目录。</summary>
    public string? NotesDirectory { get; set; }

    /// <summary>点击关闭按钮时的行为，默认最小化到托盘。</summary>
    public CloseBehavior CloseBehavior { get; set; } = CloseBehavior.MinimizeToTray;

    /// <summary>界面字体族名称；为空时使用默认字体（微软雅黑 UI）。</summary>
    public string? FontFamily { get; set; }

    /// <summary>界面基准字号（10–18），所有字号按该值等比缩放。</summary>
    public double FontSize { get; set; } = 12;

    /// <summary>界面基础字重。</summary>
    public AppFontWeight FontWeight { get; set; } = AppFontWeight.Light;
}
