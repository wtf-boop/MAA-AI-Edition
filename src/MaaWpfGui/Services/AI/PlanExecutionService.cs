// Part of MaaAssistantArknights, licensed under AGPL-3.0.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace MaaWpfGui.Services.AI;

public enum AiExecutionMode
{
    DryRun,
    MaaCore,
}

public enum AiExecutionState
{
    Idle,
    Validating,
    Ready,
    Running,
    Paused,
    WaitingForRuntimeState,
    Completed,
    Failed,
    RolledBack,
    Blocked,
}

public sealed class AiActionExecutionRecord
{
    public int Order { get; set; }
    public string Action { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public int Attempt { get; set; }
    public string Message { get; set; } = string.Empty;
    public string RuntimeSnapshot { get; set; } = string.Empty;
}

public sealed class AiExecutionSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string PlanId { get; set; } = string.Empty;
    public AiExecutionMode Mode { get; set; } = AiExecutionMode.DryRun;
    public AiExecutionState State { get; set; } = AiExecutionState.Idle;
    public int CurrentActionIndex { get; set; }
    public int MaximumRetries { get; set; } = 2;
    public int ConsecutiveRuntimeMisses { get; set; }
    public int MaximumRuntimeMisses { get; set; } = 20;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public string LastError { get; set; } = string.Empty;
    public List<AiActionExecutionRecord> History { get; set; } = [];
}

public sealed record AiExecutionValidation(bool IsValid, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public string Summary => IsValid
        ? $"验证通过；{Warnings.Count} 条警告"
        : $"验证失败；{Errors.Count} 条错误，{Warnings.Count} 条警告";
}

public interface IAiMaaCoreExecutionBridge
{
    bool IsAvailable { get; }

    AiBridgeResult Execute(AiPlanAction action, AiRuntimeState? runtimeState);
}

public sealed record AiBridgeResult(bool Success, bool ShouldWait, string Message);

public sealed class UnavailableMaaCoreExecutionBridge : IAiMaaCoreExecutionBridge
{
    public bool IsAvailable => false;

    public AiBridgeResult Execute(AiPlanAction action, AiRuntimeState? runtimeState) =>
        new(false, false, "MaaCore AI 执行适配器尚未实现；未向游戏发送任何操作。");
}

public sealed class PlanExecutionService
{
    private readonly IAiMaaCoreExecutionBridge _bridge;
    private readonly IAiRuntimeStateProvider _runtimeStateProvider;
    private readonly AiConditionEvaluator _conditionEvaluator = new();

    public PlanExecutionService(IAiMaaCoreExecutionBridge? bridge = null, IAiRuntimeStateProvider? runtimeStateProvider = null)
    {
        _bridge = bridge ?? new UnavailableMaaCoreExecutionBridge();
        _runtimeStateProvider = runtimeStateProvider ?? new JsonFileRuntimeStateProvider();
    }

    public string RuntimeProviderStatus => _runtimeStateProvider.IsAvailable
        ? $"{_runtimeStateProvider.Name} 可用"
        : $"{_runtimeStateProvider.Name} 不可用";

    public AiExecutionValidation Validate(AiBattlePlan? plan, AiExecutionMode mode)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        if (plan is null)
        {
            errors.Add("没有作战计划。");
            return new(false, errors, warnings);
        }

        if (plan.Actions.Count == 0)
        {
            errors.Add("计划没有任何动作。");
        }

        if (string.IsNullOrWhiteSpace(plan.Stage))
        {
            errors.Add("计划缺少关卡编号。");
        }

        var ordered = plan.Actions.OrderBy(action => action.Order).ToList();
        var duplicateOrders = ordered.GroupBy(action => action.Order).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
        if (duplicateOrders.Count > 0)
        {
            errors.Add($"动作序号重复：{string.Join(", ", duplicateOrders)}。");
        }

        if (ordered.Any(action => action.Order <= 0))
        {
            errors.Add("动作序号必须大于 0。");
        }

        foreach (var action in ordered)
        {
            if (string.IsNullOrWhiteSpace(action.Action))
            {
                errors.Add($"第 {action.Order} 步缺少动作类型。");
            }

            if (action.RequiresRuntimeValidation && string.IsNullOrWhiteSpace(action.Condition))
            {
                warnings.Add($"第 {action.Order} 步需要运行时校验，但没有明确条件。");
            }
        }

        if (plan.FinalScore < 0.55)
        {
            warnings.Add("计划综合评分低于 55%。");
        }

        if (mode == AiExecutionMode.MaaCore && !_bridge.IsAvailable)
        {
            errors.Add("MaaCore AI 执行适配器不可用。");
        }

        return new(errors.Count == 0, errors, warnings);
    }

    public AiExecutionSession CreateSession(AiBattlePlan plan, AiExecutionMode mode)
    {
        var validation = Validate(plan, mode);
        var session = new AiExecutionSession
        {
            PlanId = plan.Id,
            Mode = mode,
            State = validation.IsValid ? AiExecutionState.Ready : AiExecutionState.Blocked,
            LastError = validation.IsValid ? string.Empty : string.Join(" ", validation.Errors),
        };
        SaveSession(session);
        return session;
    }

    public AiExecutionSession Step(AiBattlePlan plan, AiExecutionSession session)
    {
        if (session.State is AiExecutionState.Completed or AiExecutionState.Blocked or AiExecutionState.RolledBack or AiExecutionState.Failed)
        {
            return session;
        }

        var orderedActions = plan.Actions.OrderBy(item => item.Order).ToList();
        if (session.CurrentActionIndex >= orderedActions.Count)
        {
            session.State = AiExecutionState.Completed;
            Persist(session);
            return session;
        }

        session.State = AiExecutionState.Running;
        var action = orderedActions[session.CurrentActionIndex];
        var runtimeState = _runtimeStateProvider.Capture();
        var condition = _conditionEvaluator.Evaluate(action.Condition, runtimeState);

        if (action.RequiresRuntimeValidation && !condition.IsSatisfied)
        {
            session.ConsecutiveRuntimeMisses++;
            session.State = session.ConsecutiveRuntimeMisses >= session.MaximumRuntimeMisses
                ? AiExecutionState.Paused
                : AiExecutionState.WaitingForRuntimeState;
            session.LastError = condition.Explanation;
            session.History.Add(new AiActionExecutionRecord
            {
                Order = action.Order,
                Action = Describe(action),
                State = condition.RequiresMoreData ? "RuntimeDataMissing" : "ConditionWaiting",
                Attempt = session.ConsecutiveRuntimeMisses,
                Message = condition.Explanation,
                RuntimeSnapshot = SummarizeRuntime(runtimeState),
            });
            Persist(session);
            return session;
        }

        session.ConsecutiveRuntimeMisses = 0;
        var attempt = session.History.Count(item => item.Order == action.Order && item.State is "Failed" or "Succeeded" or "Validated") + 1;
        AiBridgeResult result;
        if (session.Mode == AiExecutionMode.DryRun)
        {
            result = new AiBridgeResult(true, false, $"干运行：条件已满足；动作格式有效。{condition.Explanation}");
        }
        else
        {
            result = _bridge.Execute(action, runtimeState);
        }

        session.History.Add(new AiActionExecutionRecord
        {
            Order = action.Order,
            Action = Describe(action),
            State = result.Success ? (session.Mode == AiExecutionMode.DryRun ? "Validated" : "Succeeded") : "Failed",
            Attempt = attempt,
            Message = result.Message,
            RuntimeSnapshot = SummarizeRuntime(runtimeState),
        });

        if (result.Success)
        {
            session.CurrentActionIndex++;
            session.State = result.ShouldWait ? AiExecutionState.WaitingForRuntimeState : AiExecutionState.Ready;
            session.LastError = string.Empty;
        }
        else if (attempt <= session.MaximumRetries)
        {
            session.State = AiExecutionState.Paused;
            session.LastError = $"第 {action.Order} 步失败，可重试：{result.Message}";
        }
        else
        {
            session.State = AiExecutionState.Failed;
            session.LastError = $"第 {action.Order} 步超过最大重试次数：{result.Message}";
        }

        if (session.CurrentActionIndex >= orderedActions.Count && session.State != AiExecutionState.Failed)
        {
            session.State = AiExecutionState.Completed;
        }

        Persist(session);
        return session;
    }

    public AiExecutionSession ContinueAfterRuntimeValidation(AiExecutionSession session)
    {
        if (session.State is AiExecutionState.WaitingForRuntimeState or AiExecutionState.Paused)
        {
            session.State = AiExecutionState.Ready;
            session.LastError = string.Empty;
            Persist(session);
        }

        return session;
    }

    public AiExecutionSession Rollback(AiExecutionSession session, string reason)
    {
        session.State = AiExecutionState.RolledBack;
        session.LastError = reason;
        session.History.Add(new AiActionExecutionRecord
        {
            Order = 0,
            Action = "rollback",
            State = "RolledBack",
            Attempt = 1,
            Message = reason,
        });
        Persist(session);
        return session;
    }

    public AiExecutionSession? LoadLatestSession(string? planId = null)
    {
        AiDataPaths.EnsureDirectories();
        var files = Directory.EnumerateFiles(AiDataPaths.SessionDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc);

        foreach (var file in files)
        {
            try
            {
                var session = JsonConvert.DeserializeObject<AiExecutionSession>(File.ReadAllText(file.FullName));
                if (session is not null && (string.IsNullOrWhiteSpace(planId) || session.PlanId == planId))
                {
                    return session;
                }
            }
            catch
            {
                // Ignore broken session files and continue searching.
            }
        }

        return null;
    }

    public IReadOnlyList<AiExecutionSession> LoadRecentSessions(int maximum = 20)
    {
        AiDataPaths.EnsureDirectories();
        var result = new List<AiExecutionSession>();
        foreach (var path in Directory.EnumerateFiles(AiDataPaths.SessionDirectory, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                var session = JsonConvert.DeserializeObject<AiExecutionSession>(File.ReadAllText(path));
                if (session is not null)
                {
                    result.Add(session);
                }
            }
            catch
            {
                // Best effort recovery.
            }

            if (result.Count >= Math.Clamp(maximum, 1, 100))
            {
                break;
            }
        }

        return result;
    }

    private static string Describe(AiPlanAction action) =>
        $"{action.Action} {action.Operator} {action.Position} {action.Direction}".Trim();

    private static string SummarizeRuntime(AiRuntimeState? state)
    {
        if (state is null)
        {
            return "unavailable";
        }

        return $"stage={state.Stage};cost={state.Cost};kills={state.Kills}/{state.TotalEnemies};started={state.BattleStarted};ended={state.BattleEnded}";
    }

    private static void Persist(AiExecutionSession session)
    {
        session.UpdatedAt = DateTimeOffset.Now;
        AiDataPaths.EnsureDirectories();
        var path = Path.Combine(AiDataPaths.SessionDirectory, $"{session.Id}.json");
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonConvert.SerializeObject(session, Formatting.Indented));
        File.Move(temp, path, true);
    }

    private static void SaveSession(AiExecutionSession session) => Persist(session);
}
