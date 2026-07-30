// Part of MaaAssistantArknights, licensed under AGPL-3.0.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace MaaWpfGui.Services.AI;

public sealed class RoguelikeExperienceRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public string Theme { get; set; } = string.Empty;
    public string GameVersion { get; set; } = string.Empty;
    public string Opening { get; set; } = string.Empty;
    public List<string> Recruits { get; set; } = [];
    public List<string> Collectibles { get; set; } = [];
    public List<string> Route { get; set; } = [];
    public string Ending { get; set; } = string.Empty;
    public bool Success { get; set; }
    public int FloorReached { get; set; }
    public double Reward { get; set; }
    public string FailureReason { get; set; } = string.Empty;
}

public sealed class RoguelikeLearningService
{
    private readonly List<RoguelikeExperienceRecord> _records = [];
    public IReadOnlyList<RoguelikeExperienceRecord> Records => _records;
    private static string StorePath => Path.Combine(AiDataPaths.ExperienceDirectory, "roguelike-experience.json");

    public RoguelikeLearningService()
    {
        AiDataPaths.EnsureDirectories();
        Load();
    }

    public void Record(RoguelikeExperienceRecord record)
    {
        record.Reward = (record.Success ? 100 : -25) + (record.FloorReached * 12) + (record.Collectibles.Count * 1.5);
        _records.Add(record);
        if (_records.Count > 5000)
        {
            var retained = _records.OrderByDescending(item => item.CreatedAt > DateTimeOffset.Now.AddDays(-90)).ThenByDescending(item => item.Reward).Take(5000).ToList();
            _records.Clear();
            _records.AddRange(retained);
        }
        Save();
    }

    public void RecordExample() => Record(new RoguelikeExperienceRecord
    {
        Theme = "示例主题",
        GameVersion = "待接入运行版本",
        Opening = "先锋+近卫",
        Recruits = ["先锋A", "近卫B"],
        Collectibles = ["示例藏品"],
        Route = ["战斗", "商店", "安全屋"],
        FloorReached = 3,
        Success = true,
        Ending = "示例结局",
    });

    public string GetSummary()
    {
        if (_records.Count == 0)
        {
            return "尚无肉鸽经验；接入运行回调后会学习开局、招募、藏品、路线和失败原因。";
        }

        var recent = _records.OrderByDescending(x => x.CreatedAt).Take(100).ToArray();
        var bestOpening = recent.GroupBy(x => x.Opening).OrderByDescending(group => group.Average(x => x.Reward)).First().Key;
        return $"经验 {_records.Count} 条；最近成功率 {recent.Count(x => x.Success) / (double)recent.Length:P0}；当前高分开局：{bestOpening}。";
    }

    private void Load()
    {
        try
        {
            if (File.Exists(StorePath))
            {
                _records.AddRange(JsonConvert.DeserializeObject<List<RoguelikeExperienceRecord>>(File.ReadAllText(StorePath)) ?? []);
            }
        }
        catch
        {
            _records.Clear();
        }
    }

    private void Save() => File.WriteAllText(StorePath, JsonConvert.SerializeObject(_records, Formatting.Indented));
}
