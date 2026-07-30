// Part of MaaAssistantArknights, licensed under AGPL-3.0.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace MaaWpfGui.Services.AI;

public sealed class AiPlanAction
{
    public int Order { get; set; }
    public string Condition { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public bool RequiresRuntimeValidation { get; set; } = true;
}

public sealed class AiBattlePlan
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public string Stage { get; set; } = string.Empty;
    public string GameVersion { get; set; } = string.Empty;
    public string Goal { get; set; } = "三星通关";
    public string SourceType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public double SourceConfidence { get; set; }
    public double AccountCompatibility { get; set; }
    public double FinalScore { get; set; }
    public string ExecutionState { get; set; } = "Draft";
    public string Limitation { get; set; } = "尚未接入 MaaCore 执行桥；当前仅生成可审计计划。";
    public List<AiPlanAction> Actions { get; set; } = [];
}

public sealed class StrategyPlanningService
{
    private static string PlanDirectory => Path.Combine(AiDataPaths.StrategyDirectory, "generated");

    public AiBattlePlan Build(BilibiliStrategyRecord strategy, DoctorProfileService profile)
    {
        var compatibility = profile.CompatibilityScore(strategy.Operators);
        var versionScore = strategy.IsCurrentVersion ? 1d : 0.35d;
        var confidence = Math.Clamp(strategy.Confidence, 0, 1);
        var finalScore = (versionScore * 0.30) + (confidence * 0.30) + (compatibility * 0.30) + (Math.Min(strategy.Steps.Count, 20) / 20d * 0.10);

        return new AiBattlePlan
        {
            Stage = strategy.Stage,
            GameVersion = strategy.GameVersion,
            SourceType = "BilibiliStructuredStrategy",
            SourceId = strategy.Bvid,
            SourceUrl = strategy.SourceUrl,
            SourceConfidence = confidence,
            AccountCompatibility = compatibility,
            FinalScore = finalScore,
            Actions = strategy.Steps.OrderBy(step => step.Order).Select(step => new AiPlanAction
            {
                Order = step.Order,
                Condition = NormalizeTrigger(step.Trigger),
                Action = step.Action,
                Operator = step.Operator,
                Position = step.Tile,
                Direction = step.Direction,
                Notes = step.Notes,
                RequiresRuntimeValidation = true,
            }).ToList(),
        };
    }

    public string Save(AiBattlePlan plan)
    {
        Directory.CreateDirectory(PlanDirectory);
        var safeStage = string.Concat((plan.Stage.Length == 0 ? "unknown" : plan.Stage).Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var path = Path.Combine(PlanDirectory, $"{safeStage}-{plan.Id}.json");
        File.WriteAllText(path, JsonConvert.SerializeObject(plan, Formatting.Indented));
        return path;
    }

    private static string NormalizeTrigger(string trigger)
    {
        if (string.IsNullOrWhiteSpace(trigger))
        {
            return "manual_or_runtime_state";
        }

        return trigger.Trim().Replace("费用", "cost", StringComparison.OrdinalIgnoreCase);
    }
}
