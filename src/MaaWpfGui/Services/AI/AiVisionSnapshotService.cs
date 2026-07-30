// Part of MaaAssistantArknights, licensed under AGPL-3.0.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace MaaWpfGui.Services.AI;

public sealed class AiVisionSnapshot
{
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.Now;
    public string ScreenshotPath { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public int? Cost { get; set; }
    public int? Kills { get; set; }
    public int? TotalEnemies { get; set; }
    public double? ElapsedSeconds { get; set; }
    public bool? BattleStarted { get; set; }
    public bool? BattleEnded { get; set; }
    public List<string> DeployedOperators { get; set; } = [];
    public List<string> ReadySkills { get; set; } = [];
    public Dictionary<string, double> NumericSignals { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> Confidence { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Standardizes OCR/vision output produced by an external or future in-process recognizer.
/// It deliberately rejects low-confidence values instead of feeding uncertain data to the planner.
/// </summary>
public sealed class AiVisionSnapshotService
{
    public static string SnapshotPath => Path.Combine(AiDataPaths.CacheDirectory, "vision", "latest.json");
    public double MinimumConfidence { get; set; } = 0.72;

    public string CreateExample()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath)!);
        var snapshot = new AiVisionSnapshot
        {
            ScreenshotPath = "replace-with-screenshot-path.png",
            Stage = "示例关卡",
            Cost = 16,
            Kills = 9,
            TotalEnemies = 42,
            ElapsedSeconds = 18.4,
            BattleStarted = true,
            DeployedOperators = ["桃金娘"],
            ReadySkills = ["桃金娘"],
            Confidence = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["stage"] = 0.95,
                ["cost"] = 0.91,
                ["kills"] = 0.89,
                ["battle_started"] = 0.98,
            },
        };
        File.WriteAllText(SnapshotPath, JsonConvert.SerializeObject(snapshot, Formatting.Indented));
        return SnapshotPath;
    }

    public AiRuntimeState? LoadNormalized(out IReadOnlyList<string> warnings)
    {
        var notes = new List<string>();
        warnings = notes;
        try
        {
            if (!File.Exists(SnapshotPath))
            {
                notes.Add("视觉状态文件不存在。");
                return null;
            }

            var snapshot = JsonConvert.DeserializeObject<AiVisionSnapshot>(File.ReadAllText(SnapshotPath));
            if (snapshot is null)
            {
                notes.Add("视觉状态文件为空或格式错误。");
                return null;
            }

            bool Accept(string key) => !snapshot.Confidence.TryGetValue(key, out var score) || score >= MinimumConfidence;
            var state = new AiRuntimeState
            {
                CapturedAt = snapshot.CapturedAt,
                Stage = Accept("stage") ? snapshot.Stage : string.Empty,
                Cost = Accept("cost") ? snapshot.Cost ?? 0 : 0,
                Kills = Accept("kills") ? snapshot.Kills ?? 0 : 0,
                TotalEnemies = Accept("total_enemies") ? snapshot.TotalEnemies ?? 0 : 0,
                ElapsedSeconds = Accept("elapsed_seconds") ? snapshot.ElapsedSeconds ?? 0 : 0,
                BattleStarted = Accept("battle_started") && snapshot.BattleStarted == true,
                BattleEnded = Accept("battle_ended") && snapshot.BattleEnded == true,
                DeployedOperators = snapshot.DeployedOperators,
                ReadySkills = snapshot.ReadySkills,
                NumericSignals = snapshot.NumericSignals,
            };

            foreach (var pair in snapshot.Confidence)
            {
                if (pair.Value < MinimumConfidence)
                {
                    notes.Add($"{pair.Key} 置信度 {pair.Value:P0} 低于阈值，已忽略。");
                }
            }
            return state;
        }
        catch (Exception ex)
        {
            notes.Add($"视觉状态读取失败：{ex.Message}");
            return null;
        }
    }
}
