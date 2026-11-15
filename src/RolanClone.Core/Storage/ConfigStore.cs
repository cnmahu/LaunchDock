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

    public static string ConfigDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RolanClone");

    public static string ConfigPath { get; } = Path.Combine(ConfigDirectory, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                var seed = SampleData.CreateDefault();
                Save(seed);
                return seed;
            }

            var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), Options);
            if (config is null || config.Tabs.Count == 0)
            {
                throw new InvalidDataException("配置为空或缺少分组");
            }

            return config;
        }
        catch
        {
            TryBackupBrokenFile();
            var seed = SampleData.CreateDefault();
            Save(seed);
            return seed;
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
