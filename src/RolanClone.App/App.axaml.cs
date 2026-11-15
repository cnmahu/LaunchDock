using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using RolanClone.App.ViewModels;
using RolanClone.App.Views;
using RolanClone.Core.Storage;

namespace RolanClone.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var config = ConfigStore.Load();
            SettingsViewModel.ApplyTheme(config.Settings.Theme);
            SettingsViewModel.ApplyFont(config.Settings);
            desktop.MainWindow = new LauncherWindow(config);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnTrayShowClick(object? sender, EventArgs e) => GetLauncher()?.ShowPanel();

    private void OnTraySettingsClick(object? sender, EventArgs e) => GetLauncher()?.OpenSettings();

    private void OnTrayExitClick(object? sender, EventArgs e) => GetLauncher()?.ExitApp();

    private LauncherWindow? GetLauncher() =>
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as LauncherWindow;
}
