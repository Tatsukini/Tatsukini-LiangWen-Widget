using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiangWenWidget;

public sealed class ModelPricing
{
    // CNY per 1,000,000 tokens. Editable in config.json.
    public double CacheHitPerM { get; set; }
    public double CacheMissPerM { get; set; }
    public double OutputPerM { get; set; }
}

public sealed class AppConfig
{
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.deepseek.com";

    // off-peak (谷价) window in UTC+8, "HH:mm". Outside -> peak (峰价).
    public string OffPeakStart { get; set; } = "00:30";
    public string OffPeakEnd { get; set; } = "08:30";

    public int PollIntervalSec { get; set; } = 60;
    public int TimeoutSec { get; set; } = 12;
    // a successful request slower than this counts as "拥挤"
    public int CrowdedLatencyMs { get; set; } = 8000;
    // balance below this many cache-miss input tokens -> "停"
    public long LowBalanceTokenThreshold { get; set; } = 1_000_000;
    // how long "活" shows after recovery
    public int ReviveDisplaySec { get; set; } = 5;

    public string Model { get; set; } = "flash"; // "flash" | "pro"
    public string? BackgroundImagePath { get; set; }
    public bool AvatarEnabled { get; set; } = true;

    public Dictionary<string, ModelPricing> Pricing { get; set; } = new()
    {
        ["flash"] = new ModelPricing { CacheHitPerM = 1.0, CacheMissPerM = 4.0, OutputPerM = 16.0 },
        ["pro"]   = new ModelPricing { CacheHitPerM = 4.0, CacheMissPerM = 16.0, OutputPerM = 64.0 },
    };

    [JsonIgnore]
    public static string Path => System.IO.Path.Combine(AppContext.BaseDirectory, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var json = File.ReadAllText(Path);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                if (cfg is not null) return cfg;
            }
        }
        catch { /* fall through to defaults */ }
        return new AppConfig();
    }

    public void Save()
    {
        var opts = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(Path, JsonSerializer.Serialize(this, opts));
    }
}
