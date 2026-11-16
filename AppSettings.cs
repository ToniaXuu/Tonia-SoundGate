using System.Text.Json;

namespace ToniaSoundGate;

/// <summary>用户配置：记住上次选择的授权时长与开机自启偏好。存储于用户目录，无需管理员权限。</summary>
internal sealed class AppSettings
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Tonia-SoundGate");
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "settings.json");

    /// <summary>上次选择的授权分钟数；默认一天（1440 分钟）。</summary>
    public int LastDurationMinutes { get; set; } = 1440;

    /// <summary>是否开机自启。</summary>
    public bool StartWithWindows { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings is not null) return settings;
            }
        }
        catch
        {
            // 配置损坏时回退到默认值，保证程序始终可启动
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        }
        catch
        {
            // 保存失败不影响核心功能
        }
    }
}
