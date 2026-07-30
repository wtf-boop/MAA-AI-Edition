// Part of MaaAssistantArknights, licensed under AGPL-3.0.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MaaWpfGui.Services.AI;

public sealed class BilibiliStrategyStep
{
    public int Order { get; set; }
    public string Trigger { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty;
    public string Tile { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public sealed class BilibiliStrategyRecord
{
    public string Bvid { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Uploader { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public DateTimeOffset PublishedAt { get; set; }
    public DateTimeOffset IndexedAt { get; set; } = DateTimeOffset.Now;
    public string Stage { get; set; } = string.Empty;
    public string GameVersion { get; set; } = string.Empty;
    public string StrategyType { get; set; } = string.Empty;
    public string Difficulty { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public bool IsCurrentVersion { get; set; } = true;
    public List<string> Operators { get; set; } = [];
    public List<BilibiliStrategyStep> Steps { get; set; } = [];

    [JsonIgnore]
    public string DisplayName => $"{Stage} · {Title}";

    [JsonIgnore]
    public string Summary => $"UP：{Uploader}  类型：{StrategyType}  版本：{GameVersion}  可信度：{Confidence:P0}";
}

public sealed class BilibiliStrategyLibrary
{
    private readonly List<BilibiliStrategyRecord> _records = [];

    public IReadOnlyList<BilibiliStrategyRecord> Records => _records;

    public BilibiliStrategyLibrary()
    {
        AiDataPaths.EnsureDirectories();
        Reload();
    }

    public int Reload()
    {
        _records.Clear();
        foreach (var file in Directory.EnumerateFiles(AiDataPaths.BilibiliStrategyDirectory, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                var token = JToken.Parse(File.ReadAllText(file));
                var records = token.Type == JTokenType.Array
                    ? token.ToObject<List<BilibiliStrategyRecord>>() ?? []
                    : [token.ToObject<BilibiliStrategyRecord>() ?? new BilibiliStrategyRecord()];

                _records.AddRange(records.Where(IsUsable));
            }
            catch
            {
                // An invalid user-supplied strategy file must not prevent MAA from starting.
            }
        }

        _records.Sort((left, right) => Score(right).CompareTo(Score(left)));
        return _records.Count;
    }

    public IReadOnlyList<BilibiliStrategyRecord> Search(string query, int maxResults = 30)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return _records.Take(maxResults).ToArray();
        }

        var terms = query.Split([' ', ',', '，', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return _records
            .Select(record => new { Record = record, Match = MatchScore(record, terms) })
            .Where(item => item.Match > 0)
            .OrderByDescending(item => item.Match)
            .ThenByDescending(item => Score(item.Record))
            .Take(Math.Clamp(maxResults, 1, 100))
            .Select(item => item.Record)
            .ToArray();
    }

    public string CreateExampleFile()
    {
        var path = Path.Combine(AiDataPaths.BilibiliStrategyDirectory, "example-strategy.json");
        if (!File.Exists(path))
        {
            var example = new BilibiliStrategyRecord
            {
                Bvid = "BVxxxxxxxx",
                Title = "示例：低配三星攻略",
                Uploader = "请填写UP主",
                SourceUrl = "https://www.bilibili.com/video/BVxxxxxxxx",
                PublishedAt = DateTimeOffset.Now,
                Stage = "关卡编号",
                GameVersion = "游戏版本/活动复刻批次",
                StrategyType = "低配",
                Difficulty = "普通",
                Confidence = 0.5,
                Operators = ["先锋A", "医疗B"],
                Steps =
                [
                    new BilibiliStrategyStep { Order = 1, Trigger = "cost >= 10", Action = "deploy", Operator = "先锋A", Tile = "D6", Direction = "up" },
                    new BilibiliStrategyStep { Order = 2, Trigger = "skill_ready", Action = "use_skill", Operator = "先锋A" },
                ],
            };
            File.WriteAllText(path, JsonConvert.SerializeObject(example, Formatting.Indented));
        }

        return path;
    }

    private static bool IsUsable(BilibiliStrategyRecord record) =>
        !string.IsNullOrWhiteSpace(record.Title) || !string.IsNullOrWhiteSpace(record.Stage) || !string.IsNullOrWhiteSpace(record.Bvid);

    private static double Score(BilibiliStrategyRecord record)
    {
        var versionWeight = record.IsCurrentVersion ? 1000 : 0;
        var recency = record.PublishedAt == default ? 0 : record.PublishedAt.ToUnixTimeSeconds() / 1_000_000_000d;
        return versionWeight + (record.Confidence * 100) + recency;
    }

    private static int MatchScore(BilibiliStrategyRecord record, IEnumerable<string> terms)
    {
        var haystack = string.Join(' ', record.Stage, record.Title, record.Uploader, record.GameVersion, record.StrategyType, record.Difficulty, string.Join(' ', record.Operators));
        return terms.Sum(term => haystack.Contains(term, StringComparison.OrdinalIgnoreCase) ? Math.Max(1, term.Length) : 0);
    }
}
