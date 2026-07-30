// Part of MaaAssistantArknights, licensed under AGPL-3.0.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace MaaWpfGui.Services.AI;

public sealed class AiRuntimeState
{
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.Now;
    public string Stage { get; set; } = string.Empty;
    public int Cost { get; set; }
    public int Kills { get; set; }
    public int TotalEnemies { get; set; }
    public double ElapsedSeconds { get; set; }
    public bool BattleStarted { get; set; }
    public bool BattleEnded { get; set; }
    public bool LeakedEnemy { get; set; }
    public List<string> DeployedOperators { get; set; } = [];
    public List<string> ReadySkills { get; set; } = [];
    public Dictionary<string, double> NumericSignals { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record AiConditionResult(bool IsSatisfied, bool RequiresMoreData, string Explanation);

public interface IAiRuntimeStateProvider
{
    string Name { get; }
    bool IsAvailable { get; }
    AiRuntimeState? Capture();
}

/// <summary>
/// Development provider. Reads a user-editable JSON snapshot and never captures the screen.
/// A future vision adapter can implement IAiRuntimeStateProvider without changing the execution state machine.
/// </summary>
public sealed class JsonFileRuntimeStateProvider : IAiRuntimeStateProvider
{
    public string Name => "JSON运行时状态";
    public bool IsAvailable => File.Exists(AiDataPaths.RuntimeStatePath);

    public AiRuntimeState? Capture()
    {
        try
        {
            if (!File.Exists(AiDataPaths.RuntimeStatePath))
            {
                return null;
            }

            return JsonConvert.DeserializeObject<AiRuntimeState>(File.ReadAllText(AiDataPaths.RuntimeStatePath));
        }
        catch
        {
            return null;
        }
    }

    public string CreateExample()
    {
        AiDataPaths.EnsureDirectories();
        var state = new AiRuntimeState
        {
            Stage = "示例关卡",
            Cost = 18,
            Kills = 12,
            TotalEnemies = 45,
            ElapsedSeconds = 22.5,
            BattleStarted = true,
            DeployedOperators = ["桃金娘"],
            ReadySkills = ["桃金娘"],
            NumericSignals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["boss_hp_percent"] = 100,
                ["life_points"] = 3,
            },
        };
        File.WriteAllText(AiDataPaths.RuntimeStatePath, JsonConvert.SerializeObject(state, Formatting.Indented));
        return AiDataPaths.RuntimeStatePath;
    }
}

public sealed class AiConditionEvaluator
{
    public AiConditionResult Evaluate(string? condition, AiRuntimeState? state)
    {
        if (string.IsNullOrWhiteSpace(condition) || condition.Equals("manual_or_runtime_state", StringComparison.OrdinalIgnoreCase))
        {
            return new(false, true, "条件需要人工或视觉确认。" );
        }

        if (state is null)
        {
            return new(false, true, "没有可用的运行时状态。" );
        }

        var normalized = condition.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
        var clauses = normalized.Split(new[] { "&&", " and ", " AND " }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (clauses.Length > 1)
        {
            var results = clauses.Select(clause => Evaluate(clause, state)).ToArray();
            var satisfied = results.All(item => item.IsSatisfied);
            var needsData = results.Any(item => item.RequiresMoreData);
            return new(satisfied, needsData, string.Join(" ", results.Select(item => item.Explanation)));
        }

        if (TryCompareInteger(normalized, "cost", state.Cost, out var costResult))
        {
            return costResult;
        }

        if (TryCompareInteger(normalized, "kills", state.Kills, out var killResult))
        {
            return killResult;
        }

        if (TryCompareDouble(normalized, "elapsed_seconds", state.ElapsedSeconds, out var elapsedResult))
        {
            return elapsedResult;
        }

        if (normalized.Equals("battle_started", StringComparison.OrdinalIgnoreCase))
        {
            return new(state.BattleStarted, false, state.BattleStarted ? "战斗已开始。" : "等待战斗开始。" );
        }

        if (normalized.Equals("battle_ended", StringComparison.OrdinalIgnoreCase))
        {
            return new(state.BattleEnded, false, state.BattleEnded ? "战斗已结束。" : "战斗尚未结束。" );
        }

        if (normalized.StartsWith("skill_ready:", StringComparison.OrdinalIgnoreCase))
        {
            var name = normalized["skill_ready:".Length..];
            var ready = state.ReadySkills.Any(item => item.Equals(name, StringComparison.OrdinalIgnoreCase));
            return new(ready, false, ready ? $"{name} 技能已就绪。" : $"等待 {name} 技能就绪。" );
        }

        if (normalized.StartsWith("deployed:", StringComparison.OrdinalIgnoreCase))
        {
            var name = normalized["deployed:".Length..];
            var deployed = state.DeployedOperators.Any(item => item.Equals(name, StringComparison.OrdinalIgnoreCase));
            return new(deployed, false, deployed ? $"{name} 已部署。" : $"{name} 尚未部署。" );
        }

        foreach (var signal in state.NumericSignals)
        {
            if (TryCompareDouble(normalized, signal.Key, signal.Value, out var signalResult))
            {
                return signalResult;
            }
        }

        return new(false, true, $"暂不支持条件表达式：{condition}" );
    }

    private static bool TryCompareInteger(string expression, string key, int actual, out AiConditionResult result)
        => TryCompareDouble(expression, key, actual, out result);

    private static bool TryCompareDouble(string expression, string key, double actual, out AiConditionResult result)
    {
        foreach (var op in new[] { ">=", "<=", "==", ">", "<" })
        {
            var prefix = key + op;
            if (!expression.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!double.TryParse(expression[prefix.Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out var expected))
            {
                result = new(false, true, $"条件数值无法解析：{expression}" );
                return true;
            }

            var satisfied = op switch
            {
                ">=" => actual >= expected,
                "<=" => actual <= expected,
                "==" => Math.Abs(actual - expected) < 0.0001,
                ">" => actual > expected,
                "<" => actual < expected,
                _ => false,
            };
            result = new(satisfied, false, satisfied
                ? $"条件满足：{key}={actual} {op} {expected}。"
                : $"等待条件：{key}={actual}，目标 {op}{expected}。" );
            return true;
        }

        result = new(false, true, string.Empty);
        return false;
    }
}
