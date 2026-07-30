// Part of MaaAssistantArknights, licensed under AGPL-3.0.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace MaaWpfGui.Services.AI;

public sealed record AiPolicyDecision(string Action, double Score, string Reason);

public sealed class AiModePolicyService
{
    public AiPolicyDecision EvaluateRoguelike(IEnumerable<RoguelikeExperienceRecord> records)
    {
        var data = records.ToList();
        if (data.Count == 0) return new("继续收集经验", 0, "尚无足够肉鸽记录。" );
        var best = data.GroupBy(x => string.IsNullOrWhiteSpace(x.Opening) ? "未知开局" : x.Opening)
            .Select(g => new { Name = g.Key, Score = g.Average(x => x.Reward), Count = g.Count() })
            .OrderByDescending(x => x.Score).First();
        return new(best.Name, best.Score, $"基于 {best.Count} 条同类经验，平均回报 {best.Score:F1}。" );
    }

    public AiPolicyDecision EvaluateReclamation(IEnumerable<ReclamationExperienceRecord> records)
    {
        var data = records.ToList();
        if (data.Count == 0) return new("继续收集经验", 0, "尚无足够生息演算记录。" );
        var best = data.GroupBy(x => string.IsNullOrWhiteSpace(x.Decision) ? "未知决策" : x.Decision)
            .Select(g => new { Name = g.Key, Score = g.Average(x => x.Reward), Count = g.Count() })
            .OrderByDescending(x => x.Score).First();
        return new(best.Name, best.Score, $"基于 {best.Count} 条同类经验，平均回报 {best.Score:F1}。" );
    }
}
