// Part of MaaAssistantArknights, licensed under AGPL-3.0.
#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using MaaWpfGui.Services.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Stylet;

namespace MaaWpfGui.ViewModels.UI;

public sealed class AiAssistantViewModel : Screen
{
    private readonly ArknightsKnowledgeService _knowledge = new();
    private readonly BilibiliStrategyLibrary _strategyLibrary = new();
    private readonly BilibiliStrategyImportService _bilibiliImport = new();
    private readonly ReclamationLearningService _reclamationLearning = new();
    private readonly RoguelikeLearningService _roguelikeLearning = new();
    private readonly DoctorProfileService _doctorProfile = new();
    private readonly StrategyPlanningService _strategyPlanning = new();
    private readonly AiBrainService _brain = new();
    private readonly StrategyVersionGuardService _versionGuard = new();
    private readonly PlanExecutionService _execution = new();
    private readonly AiCacheManager _cacheManager = new();
    private readonly MaaCopilotExportService _copilotExport = new();
    private readonly AiStrategyFeedbackService _feedback = new();
    private readonly AiVisionSnapshotService _vision = new();
    private readonly AiReplayLearningService _replay = new();
    private readonly AiModePolicyService _modePolicy = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private AiAssistantSettings _settings = AiAssistantSettings.Load();

    public AiAssistantViewModel()
    {
        DisplayName = "AI 助手";
        AiDataPaths.EnsureDirectories();
        ApiUrl = _settings.ApiUrl;
        ApiKey = _settings.ApiKey;
        Model = _settings.Model;
        CacheRetentionDays = _settings.CacheRetentionDays;
        CacheMaximumMegabytes = _settings.CacheMaximumMegabytes;
        AutoCleanCache = _settings.AutoCleanCache;
        CurrentGameVersion = _settings.CurrentGameVersion;
        Transcript = "AI-MAA 已嵌入原版 MAA。攻略执行、肉鸽与生息演算学习采用可追踪的结构化数据。\n";
        RefreshStrategies();
        RefreshMaintenanceStatus();
        RefreshRuntimeStateStatus();

        if (AutoCleanCache)
        {
            _ = RunStartupCleanupAsync();
        }
    }

    public string Question { get => field; set => SetAndNotify(ref field, value); } = string.Empty;
    public string Transcript { get => field; set => SetAndNotify(ref field, value); } = string.Empty;
    public string Status { get => field; set => SetAndNotify(ref field, value); } = string.Empty;
    public string ApiUrl { get => field; set => SetAndNotify(ref field, value); } = string.Empty;
    public string ApiKey { get => field; set => SetAndNotify(ref field, value); } = string.Empty;
    public string Model { get => field; set => SetAndNotify(ref field, value); } = string.Empty;
    public string StrategyQuery { get => field; set => SetAndNotify(ref field, value); } = string.Empty;
    public string BilibiliMetadataPath { get => field; set => SetAndNotify(ref field, value); } = string.Empty;
    public string BilibiliTranscriptPath { get => field; set => SetAndNotify(ref field, value); } = string.Empty;
    public string BilibiliStageOverride { get => field; set => SetAndNotify(ref field, value); } = string.Empty;
    public string BilibiliVersionOverride { get => field; set => SetAndNotify(ref field, value); } = string.Empty;
    public string BilibiliImportStatus { get => field; set => SetAndNotify(ref field, value); } = "等待导入用户提供的B站元数据与字幕/人工步骤文本。";
    public BilibiliStrategyRecord? SelectedStrategy { get => field; set => SetAndNotify(ref field, value); }
    public string StrategyStatus { get => field; set => SetAndNotify(ref field, value); } = string.Empty;
    public string ReclamationStatus { get => field; set => SetAndNotify(ref field, value); } = string.Empty;
    public string RoguelikeStatus { get => field; set => SetAndNotify(ref field, value); } = string.Empty;
    public string DoctorProfileStatus { get => field; set => SetAndNotify(ref field, value); } = string.Empty;
    public string PlanStatus { get => field; set => SetAndNotify(ref field, value); } = "尚未生成作战计划。";
    public string BrainStatus { get => field; set => SetAndNotify(ref field, value); } = "AI Brain 等待计划。";
    public AiBattlePlan? CurrentPlan { get => field; set => SetAndNotify(ref field, value); }
    public AiExecutionSession? CurrentExecutionSession { get => field; set => SetAndNotify(ref field, value); }
    public string ExecutionStatus { get => field; set => SetAndNotify(ref field, value); } = "尚未创建执行会话。";
    public string RuntimeStateStatus { get => field; set => SetAndNotify(ref field, value); } = "运行时状态尚未配置。";
    public string VisionStatus { get => field; set => SetAndNotify(ref field, value); } = "视觉/OCR快照尚未配置。";
    public string ReplayStatus { get => field; set => SetAndNotify(ref field, value); } = "尚未导入回放事件。";
    public string PolicyStatus { get => field; set => SetAndNotify(ref field, value); } = "策略评估等待经验数据。";
    public string CopilotExportStatus { get => field; set => SetAndNotify(ref field, value); } = "尚未导出 MAA Copilot 作业。";
    public string FeedbackStatus { get => field; set => SetAndNotify(ref field, value); } = "尚无实战反馈。";
    public string VersionGuardStatus { get => field; set => SetAndNotify(ref field, value); } = "等待版本检查。";
    public string CurrentGameVersion { get => field; set => SetAndNotify(ref field, value); } = string.Empty;
    public string CacheStatus { get => field; set => SetAndNotify(ref field, value); } = string.Empty;
    public int CacheRetentionDays { get => field; set => SetAndNotify(ref field, Math.Clamp(value, 1, 365)); }
    public long CacheMaximumMegabytes { get => field; set => SetAndNotify(ref field, Math.Clamp(value, 128, 1024 * 100)); }
    public bool AutoCleanCache { get => field; set => SetAndNotify(ref field, value); }
    public bool IsBusy { get => field; set => SetAndNotify(ref field, value); }
    public bool CanSend => !IsBusy && !string.IsNullOrWhiteSpace(Question);
    public ObservableCollection<BilibiliStrategyRecord> Strategies { get; } = [];

    public async Task Send()
    {
        if (!CanSend)
        {
            return;
        }

        var question = Question.Trim();
        Question = string.Empty;
        SetBusy(true);
        Transcript += $"\n你：{question}\n\n";
        Status = "AI 正在回答…";

        try
        {
            SaveSettings();
            var context = _knowledge.Search(question, _settings.MaxKnowledgeResults);
            var strategyContext = BuildStrategyContext(question);
            var system = "你是嵌入 MAA 的明日方舟助手。优先依据本地知识和已索引攻略作答；不确定时明确说明。回答使用中文。不得声称已经执行尚未接入 MaaCore 的操作。";
            var user = $"本地知识：\n{context}\n\n攻略索引：\n{strategyContext}\n\n用户问题：{question}";
            var endpoint = ApiUrl.TrimEnd('/') + "/chat/completions";
            var body = new
            {
                model = Model,
                messages = new[]
                {
                    new { role = "system", content = system },
                    new { role = "user", content = user },
                },
                temperature = 0.3,
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
            if (!string.IsNullOrWhiteSpace(ApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
            }

            using var response = await _http.SendAsync(request);
            var raw = await response.Content.ReadAsStringAsync();
            response.EnsureSuccessStatusCode();
            var answer = JObject.Parse(raw)["choices"]?[0]?["message"]?["content"]?.ToString();
            Transcript += $"AI：{answer ?? "模型返回了空内容。"}\n";
            Status = $"完成 · 知识库 {_knowledge.Count} 个片段 · B站攻略 {_strategyLibrary.Records.Count} 条";
        }
        catch (Exception ex)
        {
            Transcript += $"AI：连接模型失败：{ex.Message}\n";
            Status = "模型连接失败，请检查设置";
        }
        finally
        {
            SetBusy(false);
        }
    }

    public async Task UpdateKnowledge()
    {
        if (IsBusy)
        {
            return;
        }

        SetBusy(true);
        Status = "正在扫描 MAA 资源并重建知识库…";
        try
        {
            var count = await _knowledge.RebuildAsync();
            Status = $"知识库更新完成：{count} 个片段";
            Transcript += $"\n系统：知识库已更新，共 {count} 个片段。\n";
        }
        catch (Exception ex)
        {
            Status = "知识库更新失败";
            Transcript += $"\n系统：知识库更新失败：{ex.Message}\n";
        }
        finally
        {
            SetBusy(false);
        }
    }

    public void RefreshStrategies()
    {
        _strategyLibrary.Reload();
        var results = _strategyLibrary.Search(StrategyQuery);
        Strategies.Clear();
        foreach (var record in results)
        {
            Strategies.Add(record);
        }

        SelectedStrategy = Strategies.Count > 0 ? Strategies[0] : null;
        StrategyStatus = $"已索引 {_strategyLibrary.Records.Count} 条；当前显示 {Strategies.Count} 条。仅保留结构化攻略和原视频来源，不打包视频。";
    }

    public void CreateStrategyExample()
    {
        var path = _strategyLibrary.CreateExampleFile();
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        RefreshStrategies();
    }


    public void CreateBilibiliImportExamples()
    {
        BilibiliMetadataPath = _bilibiliImport.CreateMetadataExample();
        BilibiliTranscriptPath = _bilibiliImport.CreateTranscriptExample();
        BilibiliImportStatus = $"已创建导入模板：\n{BilibiliMetadataPath}\n{BilibiliTranscriptPath}";
        Process.Start(new ProcessStartInfo(BilibiliMetadataPath) { UseShellExecute = true });
        Process.Start(new ProcessStartInfo(BilibiliTranscriptPath) { UseShellExecute = true });
    }

    public void ImportBilibiliStrategy()
    {
        var result = _bilibiliImport.ImportFromFiles(BilibiliMetadataPath, BilibiliTranscriptPath, BilibiliStageOverride, BilibiliVersionOverride);
        BilibiliImportStatus = $"{result.Message}\n{result.FilePath}\n{string.Join(" ", result.Warnings.Take(10))}";
        if (result.Success)
        {
            RefreshStrategies();
            SelectedStrategy = Strategies.FirstOrDefault(x => string.Equals(x.Bvid, result.Record?.Bvid, StringComparison.OrdinalIgnoreCase)) ?? Strategies.FirstOrDefault();
        }
    }

    public void OpenStrategySource()
    {
        if (SelectedStrategy is null || string.IsNullOrWhiteSpace(SelectedStrategy.SourceUrl))
        {
            StrategyStatus = "当前攻略没有可打开的来源地址。";
            return;
        }

        Process.Start(new ProcessStartInfo(SelectedStrategy.SourceUrl) { UseShellExecute = true });
    }

    public void OpenStrategyFolder()
    {
        AiDataPaths.EnsureDirectories();
        Process.Start(new ProcessStartInfo(AiDataPaths.BilibiliStrategyDirectory) { UseShellExecute = true });
    }

    public void GenerateBattlePlan()
    {
        if (SelectedStrategy is null)
        {
            PlanStatus = "请先选择一条结构化攻略。";
            return;
        }

        var assessment = _versionGuard.Assess(SelectedStrategy, CurrentGameVersion);
        VersionGuardStatus = $"状态：{assessment.Status}；版本评分 {assessment.Score:P0}。\n{string.Join(" ", assessment.Reasons)}";
        if (assessment.IsBlocked)
        {
            PlanStatus = "版本守卫阻止生成计划。请更新攻略版本信息或选择其他攻略。";
            CurrentPlan = null;
            CurrentExecutionSession = null;
            return;
        }

        CurrentPlan = _strategyPlanning.Build(SelectedStrategy, _doctorProfile);
        CurrentPlan.FinalScore = Math.Clamp(CurrentPlan.FinalScore * assessment.Score, 0, 1);
        var path = _strategyPlanning.Save(CurrentPlan);
        var decision = _brain.EvaluatePlan(CurrentPlan);
        PlanStatus = $"已生成 {CurrentPlan.Actions.Count} 步计划；评分 {CurrentPlan.FinalScore:P0}；账号适配 {CurrentPlan.AccountCompatibility:P0}。\n保存位置：{path}";
        BrainStatus = $"状态：{decision.State}\n下一步：{decision.NextAction}\n原因：{decision.Reason}\n风险：{decision.Risk}\n置信度：{decision.Confidence:P0}";
        CurrentExecutionSession = null;
        ExecutionStatus = "计划已生成，可先进行安全干运行。";
    }



    public void ExportMaaCopilot()
    {
        if (CurrentPlan is null)
        {
            CopilotExportStatus = "请先生成作战计划。";
            return;
        }

        var result = _copilotExport.Export(CurrentPlan);
        CopilotExportStatus = $"{result.Message}\n{result.FilePath}\n{string.Join(" ", result.Warnings.Take(8))}";
        if (result.Success)
        {
            Process.Start(new ProcessStartInfo(result.FilePath) { UseShellExecute = true });
        }
    }

    public void RecordSuccessFeedback()
    {
        if (CurrentPlan is null)
        {
            FeedbackStatus = "请先生成计划。";
            return;
        }

        _feedback.Record(new AiStrategyFeedback { PlanId = CurrentPlan.Id, Stage = CurrentPlan.Stage, Success = true });
        FeedbackStatus = _feedback.Summary(CurrentPlan.Stage);
    }

    public void RecordFailureFeedback()
    {
        if (CurrentPlan is null)
        {
            FeedbackStatus = "请先生成计划。";
            return;
        }

        _feedback.Record(new AiStrategyFeedback { PlanId = CurrentPlan.Id, Stage = CurrentPlan.Stage, Success = false, FailureReason = string.IsNullOrWhiteSpace(CurrentExecutionSession?.LastError) ? "用户标记失败" : CurrentExecutionSession.LastError });
        FeedbackStatus = _feedback.Summary(CurrentPlan.Stage);
    }

    public void ValidateAndCreateDryRun()
    {
        if (CurrentPlan is null)
        {
            ExecutionStatus = "请先生成作战计划。";
            return;
        }

        var validation = _execution.Validate(CurrentPlan, AiExecutionMode.DryRun);
        CurrentExecutionSession = _execution.CreateSession(CurrentPlan, AiExecutionMode.DryRun);
        ExecutionStatus = $"{validation.Summary}。会话状态：{CurrentExecutionSession.State}。\n{string.Join(" ", validation.Errors.Concat(validation.Warnings).Take(8))}";
    }

    public void RunNextDryStep()
    {
        if (CurrentPlan is null || CurrentExecutionSession is null)
        {
            ExecutionStatus = "请先创建干运行会话。";
            return;
        }

        CurrentExecutionSession = _execution.Step(CurrentPlan, CurrentExecutionSession);
        var last = CurrentExecutionSession.History.LastOrDefault();
        ExecutionStatus = $"状态：{CurrentExecutionSession.State}；进度 {CurrentExecutionSession.CurrentActionIndex}/{CurrentPlan.Actions.Count}。\n{last?.Message ?? CurrentExecutionSession.LastError}";
        RefreshRuntimeStateStatus();
    }

    public void CreateRuntimeStateExample()
    {
        var provider = new JsonFileRuntimeStateProvider();
        var path = provider.CreateExample();
        RuntimeStateStatus = $"已创建运行时状态模板：{path}";
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public void RefreshRuntimeStateStatus()
    {
        var provider = new JsonFileRuntimeStateProvider();
        var state = provider.Capture();
        RuntimeStateStatus = state is null
            ? $"{_execution.RuntimeProviderStatus}。创建模板后可测试费用、击杀数、技能就绪等条件。"
            : $"{_execution.RuntimeProviderStatus}；关卡 {state.Stage}；费用 {state.Cost}；击杀 {state.Kills}/{state.TotalEnemies}；更新时间 {state.CapturedAt.LocalDateTime:G}。";
    }


    public void CreateVisionSnapshotExample()
    {
        var path = _vision.CreateExample();
        VisionStatus = $"已创建视觉/OCR标准化模板：{path}";
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public void ImportVisionSnapshot()
    {
        var state = _vision.LoadNormalized(out var warnings);
        if (state is null)
        {
            VisionStatus = string.Join(" ", warnings);
            return;
        }

        AiDataPaths.EnsureDirectories();
        File.WriteAllText(AiDataPaths.RuntimeStatePath, JsonConvert.SerializeObject(state, Formatting.Indented));
        VisionStatus = $"已标准化视觉状态并写入执行器：费用 {state.Cost}，击杀 {state.Kills}/{state.TotalEnemies}。 {string.Join(" ", warnings.Take(6))}";
        RefreshRuntimeStateStatus();
    }

    public void CreateReplayExample()
    {
        var path = _replay.CreateExample();
        ReplayStatus = $"已创建回放事件模板：{path}";
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public void ImportReplayLearning() => ReplayStatus = _replay.Import();

    public void EvaluateModePolicies()
    {
        var rogue = _modePolicy.EvaluateRoguelike(_roguelikeLearning.Records);
        var reclamation = _modePolicy.EvaluateReclamation(_reclamationLearning.Records);
        PolicyStatus = $"肉鸽建议：{rogue.Action}（{rogue.Reason}）\n生息演算建议：{reclamation.Action}（{reclamation.Reason}）";
    }

    public void RestoreLatestExecutionSession()
    {
        var restored = _execution.LoadLatestSession(CurrentPlan?.Id);
        if (restored is null)
        {
            ExecutionStatus = CurrentPlan is null ? "没有可恢复的执行会话。" : "当前计划没有可恢复的执行会话。";
            return;
        }

        CurrentExecutionSession = restored;
        ExecutionStatus = $"已恢复会话 {restored.Id}；状态 {restored.State}；进度 {restored.CurrentActionIndex}/{CurrentPlan?.Actions.Count ?? 0}。";
    }

    public void RollbackExecution()
    {
        if (CurrentExecutionSession is null)
        {
            ExecutionStatus = "没有可回退的执行会话。";
            return;
        }

        CurrentExecutionSession = _execution.Rollback(CurrentExecutionSession, "用户手动回退；未向游戏发送操作。");
        ExecutionStatus = $"会话已回退：{CurrentExecutionSession.State}。";
    }

    public void CheckSelectedStrategyVersion()
    {
        if (SelectedStrategy is null)
        {
            VersionGuardStatus = "请先选择攻略。";
            return;
        }

        var assessment = _versionGuard.Assess(SelectedStrategy, CurrentGameVersion);
        VersionGuardStatus = $"状态：{assessment.Status}；版本评分 {assessment.Score:P0}。\n{string.Join(" ", assessment.Reasons)}";
    }

    public void CreateDoctorProfileExample()
    {
        var path = _doctorProfile.CreateExampleFile();
        DoctorProfileStatus = _doctorProfile.GetSummary();
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public void ReloadDoctorProfile()
    {
        _doctorProfile.Load();
        DoctorProfileStatus = _doctorProfile.GetSummary();
    }

    public void RecordRoguelikeExample()
    {
        _roguelikeLearning.RecordExample();
        RoguelikeStatus = _roguelikeLearning.GetSummary();
    }

    public void RecordReclamationExample()
    {
        _reclamationLearning.RecordExample();
        ReclamationStatus = _reclamationLearning.GetSummary();
    }

    public void CleanCache()
    {
        SaveSettings();
        var result = _cacheManager.Cleanup(TimeSpan.FromDays(CacheRetentionDays), CacheMaximumMegabytes * 1024L * 1024L);
        var sessions = _cacheManager.PruneExecutionSessions(TimeSpan.FromDays(Math.Max(CacheRetentionDays * 2, 30)), 50);
        CacheStatus = $"清理完成：缓存 {result.DisplayText}；旧执行会话 {sessions.DisplayText}。经验库、配置与攻略索引未删除。";
    }

    public void RefreshMaintenanceStatus()
    {
        ReclamationStatus = _reclamationLearning.GetSummary();
        RoguelikeStatus = _roguelikeLearning.GetSummary();
        DoctorProfileStatus = _doctorProfile.GetSummary();
        CacheStatus = $"当前 AI 缓存：{FormatBytes(_cacheManager.CalculateCacheSize())}；保留 {CacheRetentionDays} 天；上限 {CacheMaximumMegabytes} MB。";
        Status = $"知识库 {_knowledge.Count} 个片段 · B站攻略 {_strategyLibrary.Records.Count} 条";
    }

    public void SaveSettings()
    {
        _settings.ApiUrl = ApiUrl.Trim();
        _settings.ApiKey = ApiKey.Trim();
        _settings.Model = Model.Trim();
        _settings.CacheRetentionDays = CacheRetentionDays;
        _settings.CacheMaximumMegabytes = CacheMaximumMegabytes;
        _settings.AutoCleanCache = AutoCleanCache;
        _settings.CurrentGameVersion = CurrentGameVersion.Trim();
        _settings.Save();
        Status = "AI 设置已保存";
    }

    public void ClearChat() => Transcript = string.Empty;

    public void OpenKnowledgeFolder()
    {
        AiDataPaths.EnsureDirectories();
        Process.Start(new ProcessStartInfo(AiDataPaths.KnowledgeDirectory) { UseShellExecute = true });
    }

    private async Task RunStartupCleanupAsync()
    {
        await Task.Yield();
        try
        {
            var result = _cacheManager.Cleanup(TimeSpan.FromDays(CacheRetentionDays), CacheMaximumMegabytes * 1024L * 1024L);
            CacheStatus = result.FilesDeleted > 0
                ? $"启动维护：{result.DisplayText}"
                : $"启动维护完成；缓存 {FormatBytes(_cacheManager.CalculateCacheSize())}";
        }
        catch (Exception ex)
        {
            CacheStatus = $"自动清理未完成：{ex.Message}";
        }
    }

    private string BuildStrategyContext(string query)
    {
        var builder = new StringBuilder();
        foreach (var strategy in _strategyLibrary.Search(query, 5))
        {
            builder.AppendLine($"[{strategy.Stage}] {strategy.Title} / UP:{strategy.Uploader} / 版本:{strategy.GameVersion} / 类型:{strategy.StrategyType} / 可信度:{strategy.Confidence:P0}");
            foreach (var step in strategy.Steps.OrderBy(item => item.Order).Take(20))
            {
                builder.AppendLine($"  {step.Order}. {step.Trigger} -> {step.Action} {step.Operator} {step.Tile} {step.Direction} {step.Notes}".TrimEnd());
            }
        }

        return builder.ToString();
    }

    private void SetBusy(bool value)
    {
        IsBusy = value;
        NotifyOfPropertyChange(nameof(CanSend));
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:F1} {units[unit]}";
    }
}
