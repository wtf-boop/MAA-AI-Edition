// Part of MaaAssistantArknights, licensed under AGPL-3.0.
#nullable enable
using System;
using System.IO;
using Newtonsoft.Json;

namespace MaaWpfGui.Services.AI;

public sealed class AiAssistantSettings
{
    public string ApiUrl { get; set; } = "http://127.0.0.1:11434/v1";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "qwen2.5:7b";
    public int MaxKnowledgeResults { get; set; } = 6;
    public int CacheRetentionDays { get; set; } = 14;
    public long CacheMaximumMegabytes { get; set; } = 2048;
    public bool AutoCleanCache { get; set; } = true;
    public string CurrentGameVersion { get; set; } = string.Empty;

    private static string SettingsPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "ai-assistant.json");

    public static AiAssistantSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AiAssistantSettings();
            }

            return JsonConvert.DeserializeObject<AiAssistantSettings>(File.ReadAllText(SettingsPath)) ?? new AiAssistantSettings();
        }
        catch
        {
            return new AiAssistantSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(this, Formatting.Indented));
    }
}
