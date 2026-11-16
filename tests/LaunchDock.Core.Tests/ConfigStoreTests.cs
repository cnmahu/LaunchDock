using LaunchDock.Core.Model;
using LaunchDock.Core.Storage;
using Xunit;

namespace LaunchDock.Core.Tests;

/// <summary>
/// ConfigStore 回归测试：通过 internal 钩子把配置目录重定向到临时目录，
/// 每个用例独立目录，避免污染真实 %APPDATA%。
/// </summary>
public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _directory;

    public ConfigStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "launchdock-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        ConfigStore.SetConfigDirectoryForTests(_directory);
    }

    public void Dispose()
    {
        ConfigStore.SetConfigDirectoryForTests(null);
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // 清理失败不影响测试结果
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaultAndSavesToDisk()
    {
        var config = ConfigStore.Load();

        Assert.Equal(1, config.SchemaVersion);
        Assert.Empty(config.Tabs);
        Assert.True(File.Exists(ConfigStore.ConfigPath), "首次加载应把默认配置落盘");
    }

    [Fact]
    public void Load_CorruptJson_BacksUpAndReturnsDefault()
    {
        File.WriteAllText(ConfigStore.ConfigPath, "{ 这不是合法 JSON !!!");

        var config = ConfigStore.Load();

        Assert.Equal(1, config.SchemaVersion);
        Assert.Empty(config.Tabs);
        // 损坏的配置应先备份再重建
        Assert.Single(Directory.GetFiles(_directory, "config.json.broken-*"));
        Assert.True(File.Exists(ConfigStore.ConfigPath), "重建后应有可用的默认配置");
    }

    [Fact]
    public void Load_NullJsonContent_ReturnsDefault()
    {
        // Deserialize 对 "null" 返回 null，应触发备份 + 默认配置分支
        File.WriteAllText(ConfigStore.ConfigPath, "null");

        var config = ConfigStore.Load();

        Assert.Equal(1, config.SchemaVersion);
        Assert.Empty(config.Tabs);
    }

    [Fact]
    public void Load_UnwritableDirectory_ReturnsDefaultWithoutThrowing()
    {
        // 用含非法字符的路径模拟不可写目录：Save 必须失败，但 Load 不能抛异常
        ConfigStore.SetConfigDirectoryForTests(Path.Combine(_directory, "bad<dir?"));

        var config = ConfigStore.Load();

        Assert.Equal(1, config.SchemaVersion);
        Assert.Empty(config.Tabs);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllData()
    {
        var original = new AppConfig();
        var tab = new TabModel { Name = "工具", Kind = TabKind.LiveFolder, SourcePath = @"C:\某目录" };
        tab.Items.Add(new ItemModel
        {
            DisplayName = "记事本",
            Kind = ItemKind.Exe,
            Target = @"C:\Windows\System32\notepad.exe",
            RunAsAdmin = true,
            UseCount = 3
        });
        original.Tabs.Add(tab);
        original.Settings.Hotkey = "Ctrl+Alt+Q";
        original.Settings.IconSizePx = 64;
        original.Window.Left = 100;
        original.Window.Top = 200;

        ConfigStore.Save(original);
        var loaded = ConfigStore.Load();

        var loadedTab = Assert.Single(loaded.Tabs);
        Assert.Equal("工具", loadedTab.Name);
        Assert.Equal(TabKind.LiveFolder, loadedTab.Kind);
        Assert.Equal(@"C:\某目录", loadedTab.SourcePath);

        var loadedItem = Assert.Single(loadedTab.Items);
        Assert.Equal("记事本", loadedItem.DisplayName);
        Assert.Equal(ItemKind.Exe, loadedItem.Kind);
        Assert.Equal(@"C:\Windows\System32\notepad.exe", loadedItem.Target);
        Assert.True(loadedItem.RunAsAdmin);
        Assert.Equal(3, loadedItem.UseCount);

        Assert.Equal("Ctrl+Alt+Q", loaded.Settings.Hotkey);
        Assert.Equal(64, loaded.Settings.IconSizePx);
        Assert.Equal(100, loaded.Window.Left);
        Assert.Equal(200, loaded.Window.Top);
    }
}
