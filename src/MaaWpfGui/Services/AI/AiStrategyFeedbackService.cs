// Part of MaaAssistantArknights, licensed under AGPL-3.0.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace MaaWpfGui.Services.AI;

public sealed class AiStrategyFeedback
{
    public string PlanId { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public bool Success { get; set; }
    public int Attempts { get; set; } = 1;
    public double DurationSeconds { get; set; }
    public string FailureReason { get; set; } = string.Empty;
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class AiStrategyFeedbackService
{
    private static string FilePath => Path.Combine(AiDataPaths.ExperienceDirectory, "battle-feedback.json");

    public void Record(AiStrategyFeedback feedback)
    {
        Directory.CreateDirectory(AiDataPaths.ExperienceDirectory);
        var all = Load().ToList();
        all.Add(feedback);
        all = all.OrderByDescending(x => x.RecordedAt).Take(5000).ToList();
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonConvert.SerializeObject(all, Formatting.Indented));
        File.Move(temp, FilePath, true);
    }

    public IReadOnlyList<AiStrategyFeedback> Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonConvert.DeserializeObject<List<AiStrategyFeedback>>(File.ReadAllText(FilePath)) ?? []
                : [];
        }
        catch
        {
            return [];
        }
    }

    public string Summary(string? stage = null)
    {
        var rows = Load().Where(x => string.IsNullOrWhiteSpace(stage) || string.Equals(x.Stage, stage, StringComparison.OrdinalIgnoreCase)).ToList();
        if (rows.Count == 0)
        {
            return "尚无实战反馈。";
        }

        var successRate = rows.Count(x => x.Success) / (double)rows.Count;
        var failures = rows.Where(x => !x.Success && !string.IsNullOrWhiteSpace(x.FailureReason))
            .GroupBy(x => x.FailureReason).OrderByDescending(x => x.Count()).Take(3)
            .Select(x => $"{x.Key}×{x.Count()}");
        return $"反馈 {rows.Count} 次；成功率 {successRate:P0}；常见失败：{string.Join("、", failures)}";
    }
}
