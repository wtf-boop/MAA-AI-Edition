// Part of MaaAssistantArknights, licensed under AGPL-3.0.
#nullable enable
using System;
using System.IO;

namespace MaaWpfGui.Services.AI;

public static class AiDataPaths
{
    public static string BaseDirectory => AppDomain.CurrentDomain.BaseDirectory;
    public static string ConfigDirectory => Path.Combine(BaseDirectory, "config", "ai");
    public static string KnowledgeDirectory => Path.Combine(ConfigDirectory, "knowledge");
    public static string StrategyDirectory => Path.Combine(ConfigDirectory, "strategies");
    public static string BilibiliStrategyDirectory => Path.Combine(StrategyDirectory, "bilibili");
    public static string ExperienceDirectory => Path.Combine(ConfigDirectory, "experience");
    public static string CacheDirectory => Path.Combine(BaseDirectory, "cache", "ai");
    public static string VideoCacheDirectory => Path.Combine(CacheDirectory, "video");
    public static string ImageCacheDirectory => Path.Combine(CacheDirectory, "images");
    public static string OcrCacheDirectory => Path.Combine(CacheDirectory, "ocr");
    public static string RuntimeDirectory => Path.Combine(ConfigDirectory, "runtime");
    public static string RuntimeStatePath => Path.Combine(RuntimeDirectory, "runtime-state.json");
    public static string SessionDirectory => Path.Combine(StrategyDirectory, "execution-sessions");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(KnowledgeDirectory);
        Directory.CreateDirectory(BilibiliStrategyDirectory);
        Directory.CreateDirectory(ExperienceDirectory);
        Directory.CreateDirectory(VideoCacheDirectory);
        Directory.CreateDirectory(ImageCacheDirectory);
        Directory.CreateDirectory(OcrCacheDirectory);
        Directory.CreateDirectory(RuntimeDirectory);
        Directory.CreateDirectory(SessionDirectory);
    }
}
