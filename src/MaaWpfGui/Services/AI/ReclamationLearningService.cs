// Part of MaaAssistantArknights, licensed under AGPL-3.0.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace MaaWpfGui.Services.AI;

public sealed class ReclamationExperienceRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public string GameVersion { get; set; } = string.Empty;
    public string Map { get; set; } = string.Empty;
    public int Day { get; set; }
    public int WoodDelta { get; set; }
    public int StoneDelta { get; set; }
    public int FoodDelta { get; set; }
    public int BaseDamage { get; set; }
    public bool Success { get; set; }
    public string Decision { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public double Reward { get; set; }
}

public sealed class ReclamationLearningService
{
    private readonly List<ReclamationExperienceRecord> _records = [];
    public IReadOnlyList<ReclamationExperienceRecord> Records => _records;
    private static string StorePath => Path.Combine(AiDataPaths.ExperienceDirectory, "reclamation-experience.json");

    public int Count => _records.Count;

    public ReclamationLearningService()
    {
        AiDataPaths.EnsureDirectories();
        Load();
    }

    public void Record(ReclamationExperienceRecord record)
    {
        record.Reward = CalculateReward(record);
        _records.Add(record);
        Compact();
        Save();
    }

    public string GetSummary()
    {
        if (_records.Count == 0)
        {
            return "尚无生息演算经验记录。运行结果接入后，会按资源收益、基地损伤和成功率积累经验。";
        }

        var recent = _records.OrderByDescending(item => item.CreatedAt).Take(100).ToArray();
        var successRate = recent.Count(item => item.Success) / (double)recent.Length;
        var averageReward = recent.Average(item => item.Reward);
        var best = recent.OrderByDescending(item => item.Reward).First();
        return $"经验 {Count} 条；最近成功率 {successRate:P0}；平均奖励 {averageReward:F1}；最佳决策：{best.Decision}";
    }

    public void RecordExample()
    {
        Record(new ReclamationExperienceRecord
        {
            GameVersion = "待接入运行版本",
            Map = "示例地图",
            Day = 1,
            WoodDelta = 12,
            StoneDelta = 5,
            FoodDelta = -2,
            BaseDamage = 0,
            Success = true,
            Decision = "优先采集木材并保留体力",
            Result = "示例记录，仅用于验证经验库写入。",
        });
    }

    private static double CalculateReward(ReclamationExperienceRecord record) =>
        (record.Success ? 100 : -40) + (record.WoodDelta * 1.2) + (record.StoneDelta * 1.5) + record.FoodDelta - (record.BaseDamage * 3);

    private void Compact()
    {
        const int maxRecords = 5000;
        if (_records.Count <= maxRecords)
        {
            return;
        }

        var retained = _records
            .OrderByDescending(item => item.CreatedAt > DateTimeOffset.Now.AddDays(-60))
            .ThenByDescending(item => item.Reward)
            .ThenByDescending(item => item.CreatedAt)
            .Take(maxRecords)
            .ToList();
        _records.Clear();
        _records.AddRange(retained);
    }

    private void Load()
    {
        try
        {
            if (File.Exists(StorePath))
            {
                _records.AddRange(JsonConvert.DeserializeObject<List<ReclamationExperienceRecord>>(File.ReadAllText(StorePath)) ?? []);
            }
        }
        catch
        {
            _records.Clear();
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, JsonConvert.SerializeObject(_records, Formatting.Indented));
    }
}
