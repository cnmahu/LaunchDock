using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using LaunchDock.Core.Model;

namespace LaunchDock.Core.Storage;

public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static string? _directoryOverrideForTests;

    /// <summary>仅测试使用：把配置目录重定向到指定位置（传 null 复位）。生产代码不调用。</summary>
    internal static void SetConfigDirectoryForTests(string? directory) =>
        _directoryOverrideForTests = directory;

    public static string ConfigDirectory => _directoryOverrideForTests ?? ResolveConfigDirectory();

    private static string ResolveConfigDirectory()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(root, "LaunchDock");
    }

    public static string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return CreateDefaultAndTrySave();
            }

            var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), Options);
            if (config is null)
            {
                throw new InvalidDataException("配置无法解析");
            }

            return config;
        }
        catch
        {
            TryBackupBrokenFile();
            return CreateDefaultAndTrySave();
        }
    }

    /// <summary>构造默认配置并尽力落盘；磁盘不可写时也返回内存默认值，保证应用可启动。</summary>
    private static AppConfig CreateDefaultAndTrySave()
    {
        var fresh = new AppConfig();
        try
        {
            Save(fresh);
        }
        catch
        {
            // 写入失败（只读目录/磁盘满）时仅使用内存默认配置
        }

        return fresh;
    }

    public static void Save(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDirectory);
        var temp = ConfigPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(config, Options));

        if (File.Exists(ConfigPath))
        {
            File.Replace(temp, ConfigPath, ConfigPath + ".bak", ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temp, ConfigPath);
        }
    }

    private static void TryBackupBrokenFile()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                File.Copy(ConfigPath, ConfigPath + $".broken-{DateTime.Now:yyyyMMddHHmmss}", overwrite: true);
            }
        }
        catch
        {
            // 备份失败不影响启动
        }
    }
}
