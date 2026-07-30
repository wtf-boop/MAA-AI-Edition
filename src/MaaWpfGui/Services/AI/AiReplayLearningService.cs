// Part of MaaAssistantArknights, licensed under AGPL-3.0.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace MaaWpfGui.Services.AI;

public sealed class AiReplayEvent
{
    public double TimeSeconds { get; set; }
    public string Mode { get; set; } = "battle";
    public string Action { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
    public double Reward { get; set; }
}

public sealed class AiReplayLearningService
{
    public static string ImportPath => Path.Combine(AiDataPaths.KnowledgeDirectory, "replay_import.json");
    private static string StorePath => Path.Combine(AiDataPaths.KnowledgeDirectory, "replay_experience.json");

    public string CreateExample()
    {
        AiDataPaths.EnsureDirectories();
        var events = new[]
        {
            new AiReplayEvent { TimeSeconds = 0, Mode = "roguelike", Action = "recruit", Target = "先锋", Context = "开局", Reward = 8 },
            new AiReplayEvent { TimeSeconds = 24.5, Mode = "battle", Action = "deploy", Target = "桃金娘", Context = "cost>=10", Reward = 3 },
            new AiReplayEvent { TimeSeconds = 48.1, Mode = "reclamation", Action = "collect", Target = "wood", Context = "day=1", Reward = 12 },
        };
        File.WriteAllText(ImportPath, JsonConvert.SerializeObject(events, Formatting.Indented));
        return ImportPath;
    }

    public string Import()
    {
        if (!File.Exists(ImportPath)) return "回放事件文件不存在。";
        var incoming = JsonConvert.DeserializeObject<List<AiReplayEvent>>(File.ReadAllText(ImportPath)) ?? [];
        var stored = File.Exists(StorePath)
            ? JsonConvert.DeserializeObject<List<AiReplayEvent>>(File.ReadAllText(StorePath)) ?? []
            : [];
        stored.AddRange(incoming.Where(x => !string.IsNullOrWhiteSpace(x.Action)));
        stored = stored.OrderByDescending(x => x.Reward).ThenByDescending(x => x.TimeSeconds).Take(10000).ToList();
        File.WriteAllText(StorePath, JsonConvert.SerializeObject(stored, Formatting.Indented));
        return $"已导入 {incoming.Count} 条回放事件；经验库共 {stored.Count} 条。";
    }
}
