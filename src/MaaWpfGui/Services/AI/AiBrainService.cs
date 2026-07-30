// Part of MaaAssistantArknights, licensed under AGPL-3.0.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace MaaWpfGui.Services.AI;

public sealed class AiBrainDecision
{
    public string Goal { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string NextAction { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Risk { get; set; } = string.Empty;
    public double Confidence { get; set; }
}

public sealed class AiBrainService
{
    public AiBrainDecision EvaluatePlan(AiBattlePlan? plan)
    {
        if (plan is null)
        {
            return new AiBrainDecision { Goal = "生成作战计划", State = "MissingPlan", NextAction = "选择攻略", Reason = "尚未生成计划。", Risk = "无法执行", Confidence = 0 };
        }

        var missing = plan.Actions.Count(action => string.IsNullOrWhiteSpace(action.Action));
        var next = plan.Actions.OrderBy(action => action.Order).FirstOrDefault();
        var risk = plan.AccountCompatibility < 0.7 ? "账号干员匹配不足" : plan.SourceConfidence < 0.6 ? "攻略可信度偏低" : "需要运行时视觉校验";
        return new AiBrainDecision
        {
            Goal = plan.Goal,
            State = missing == 0 ? "PlanReady" : "PlanIncomplete",
            NextAction = next is null ? "补充攻略步骤" : $"{next.Action} {next.Operator} {next.Position}",
            Reason = $"计划评分 {plan.FinalScore:P0}，账号适配 {plan.AccountCompatibility:P0}，来源可信度 {plan.SourceConfidence:P0}。",
            Risk = risk,
            Confidence = Math.Clamp(plan.FinalScore - (missing * 0.05), 0, 1),
        };
    }
}
