using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using RolanClone.Core.Model;

namespace RolanClone.Core.Storage;

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

    public static string ConfigDirectory { get; } = ResolveConfigDirectory();

    private static string ResolveConfigDirectory()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var newPath = Path.Combine(root, "LaunchDock");

        // 旧版本使用 RolanClone 目录，首次运行时整体迁移（配置/便签一并保留）
        if (!Directory.Exists(newPath))
        {
            var oldPath = Path.Combine(root, "RolanClone");
            if (Directory.Exists(oldPath))
            {
                try { Directory.Move(oldPath, newPath); } catch { }
            }
        }

        return newPath;
    }

    public static string ConfigPath { get; } = Path.Combine(ConfigDirectory, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                var fresh = new AppConfig();
                Save(fresh);
                return fresh;
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
            var fresh = new AppConfig();
            Save(fresh);
            return fresh;
        }
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
