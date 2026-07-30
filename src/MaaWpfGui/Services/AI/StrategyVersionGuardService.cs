// Part of MaaAssistantArknights, licensed under AGPL-3.0.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace MaaWpfGui.Services.AI;

public sealed record StrategyVersionAssessment(string Status, double Score, IReadOnlyList<string> Reasons)
{
    public bool IsBlocked => Status == "Blocked";
}

public sealed class StrategyVersionGuardService
{
    public StrategyVersionAssessment Assess(BilibiliStrategyRecord strategy, string currentGameVersion)
    {
        var reasons = new List<string>();
        var score = 1d;

        if (string.IsNullOrWhiteSpace(strategy.GameVersion))
        {
            score -= 0.30;
            reasons.Add("攻略未标注游戏版本。");
        }
        else if (!string.IsNullOrWhiteSpace(currentGameVersion) && !VersionMatches(strategy.GameVersion, currentGameVersion))
        {
            score -= 0.45;
            reasons.Add($"攻略版本 {strategy.GameVersion} 与当前版本 {currentGameVersion} 不一致。");
        }

        if (!strategy.IsCurrentVersion)
        {
            score -= 0.25;
            reasons.Add("攻略已被索引标记为非当前版本。");
        }

        if (strategy.Steps.Count == 0)
        {
            score = 0;
            reasons.Add("攻略没有结构化步骤。");
        }

        if (strategy.Confidence < 0.45)
        {
            score -= 0.20;
            reasons.Add("来源可信度较低。");
        }

        score = Math.Clamp(score, 0, 1);
        var status = score < 0.35 ? "Blocked" : score < 0.70 ? "NeedsVerification" : "Usable";
        if (reasons.Count == 0)
        {
            reasons.Add("版本标记和结构化步骤未发现明显问题。");
        }

        return new(status, score, reasons);
    }

    private static bool VersionMatches(string strategyVersion, string currentVersion)
    {
        var left = Normalize(strategyVersion);
        var right = Normalize(currentVersion);
        return left == right || left.StartsWith(right, StringComparison.OrdinalIgnoreCase) || right.StartsWith(left, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
