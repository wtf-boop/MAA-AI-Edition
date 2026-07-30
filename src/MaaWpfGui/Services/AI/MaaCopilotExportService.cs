// Part of MaaAssistantArknights, licensed under AGPL-3.0.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MaaWpfGui.Services.AI;

public sealed record MaaCopilotExportResult(bool Success, string FilePath, IReadOnlyList<string> Warnings, string Message);

/// <summary>
/// Converts an audited AI battle plan into MaaCore's native Copilot JSON format.
/// The generated file is executed by the original Copilot workflow instead of direct UI clicking.
/// </summary>
public sealed class MaaCopilotExportService
{
    private static string OutputDirectory => Path.Combine(AiDataPaths.StrategyDirectory, "maa-copilot");

    public MaaCopilotExportResult Export(AiBattlePlan? plan)
    {
        var warnings = new List<string>();
        if (plan is null || plan.Actions.Count == 0)
        {
            return new(false, string.Empty, warnings, "没有可导出的作战计划。");
        }

        var actions = new JArray();
        foreach (var step in plan.Actions.OrderBy(x => x.Order))
        {
            var converted = ConvertAction(step, warnings);
            if (converted is not null)
            {
                actions.Add(converted);
            }
        }

        if (actions.Count == 0)
        {
            return new(false, string.Empty, warnings, "计划中没有能够转换为 MAA Copilot 的动作。");
        }

        var opers = new JArray(plan.Actions
            .Where(x => !string.IsNullOrWhiteSpace(x.Operator))
            .Select(x => x.Operator.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => new JObject { ["name"] = name }));

        var root = new JObject
        {
            ["minimum_required"] = "v5.0.0",
            ["stage_name"] = plan.Stage,
            ["opers"] = opers,
            ["groups"] = new JArray(),
            ["actions"] = actions,
            ["doc"] = new JObject
            {
                ["title"] = $"AI-MAA {plan.Stage} {plan.SourceId}".Trim(),
                ["details"] = $"由 AI-MAA 从结构化攻略生成。来源：{plan.SourceUrl}\n版本：{plan.GameVersion}\n评分：{plan.FinalScore:P0}\n生成时间：{plan.CreatedAt.LocalDateTime:G}",
            },
        };

        var validationError = ValidateDocument(root);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            return new(false, string.Empty, warnings, $"生成的 Copilot 作业未通过兼容性校验：{validationError}");
        }

        Directory.CreateDirectory(OutputDirectory);
        var safeStage = SafeName(string.IsNullOrWhiteSpace(plan.Stage) ? "unknown" : plan.Stage);
        var path = Path.Combine(OutputDirectory, $"AI_{safeStage}_{plan.Id}.json");
        AtomicWrite(path, root.ToString(Formatting.Indented));
        return new(true, path, warnings, $"已导出 {actions.Count} 个原生 Copilot 动作。");
    }

    private static JObject? ConvertAction(AiPlanAction step, List<string> warnings)
    {
        var action = step.Action.Trim().ToLowerInvariant();
        switch (action)
        {
            case "deploy":
            case "部署":
            {
                if (string.IsNullOrWhiteSpace(step.Operator) || !TryParseLocation(step.Position, out var x, out var y))
                {
                    warnings.Add($"第 {step.Order} 步部署信息不完整，已跳过。");
                    return null;
                }

                return new JObject
                {
                    ["type"] = "Deploy",
                    ["name"] = step.Operator,
                    ["location"] = new JArray(x, y),
                    ["direction"] = NormalizeDirection(step.Direction),
                    ["kills"] = ExtractIntCondition(step.Condition, "kills"),
                    ["costs"] = ExtractIntCondition(step.Condition, "cost"),
                    ["doc"] = step.Notes,
                };
            }

            case "skill":
            case "useskill":
            case "use_skill":
            case "开技能":
                return new JObject
                {
                    ["type"] = "Skill",
                    ["name"] = step.Operator,
                    ["kills"] = ExtractIntCondition(step.Condition, "kills"),
                    ["doc"] = step.Notes,
                };

            case "retreat":
            case "撤退":
                return new JObject
                {
                    ["type"] = "Retreat",
                    ["name"] = step.Operator,
                    ["kills"] = ExtractIntCondition(step.Condition, "kills"),
                    ["doc"] = step.Notes,
                };

            case "speedup":
            case "倍速":
                return new JObject { ["type"] = "SpeedUp" };

            case "skilldaemon":
            case "自动技能":
                return new JObject { ["type"] = "SkillDaemon" };

            case "wait":
            case "等待":
                return new JObject
                {
                    ["type"] = "Output",
                    ["doc"] = string.IsNullOrWhiteSpace(step.Notes) ? $"等待条件：{step.Condition}" : step.Notes,
                };

            default:
                warnings.Add($"第 {step.Order} 步动作“{step.Action}”不是受支持的 Copilot 动作，已跳过。");
                return null;
        }
    }

    private static bool TryParseLocation(string value, out int x, out int y)
    {
        x = y = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Trim().Trim('[', ']', '(', ')').Split([',', '，', ' ', ';'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out x) && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out y);
    }

    private static string NormalizeDirection(string direction) => direction.Trim().ToLowerInvariant() switch
    {
        "up" or "上" => "Up",
        "down" or "下" => "Down",
        "left" or "左" => "Left",
        "right" or "右" => "Right",
        _ => "None",
    };

    private static int ExtractIntCondition(string condition, string key)
    {
        if (string.IsNullOrWhiteSpace(condition))
        {
            return 0;
        }

        var normalized = condition.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);
        var marker = key + ">=";
        var index = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return 0;
        }

        var digits = new string(normalized[(index + marker.Length)..].TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : 0;
    }

    private static string ValidateDocument(JObject root)
    {
        try
        {
            var stage = root.Value<string>("stage_name");
            var actions = root["actions"] as JArray;
            if (string.IsNullOrWhiteSpace(stage)) return "stage_name 为空。";
            if (actions is null || actions.Count == 0) return "actions 为空。";
            foreach (var action in actions.OfType<JObject>())
            {
                var type = action.Value<string>("type") ?? "Deploy";
                if (type == "Deploy")
                {
                    if (string.IsNullOrWhiteSpace(action.Value<string>("name"))) return "部署动作缺少 name。";
                    if (action["location"] is not JArray location || location.Count != 2) return "部署动作缺少二维 location。";
                    if (string.IsNullOrWhiteSpace(action.Value<string>("direction"))) return "部署动作缺少 direction。";
                }
            }
            return string.Empty;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static string SafeName(string value) => string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private static void AtomicWrite(string path, string content)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, true);
    }
}
