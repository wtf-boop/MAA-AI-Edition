// Part of MaaAssistantArknights, licensed under AGPL-3.0.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MaaWpfGui.Services.AI;

public sealed record BilibiliImportResult(bool Success, string FilePath, BilibiliStrategyRecord? Record, IReadOnlyList<string> Warnings, string Message);

/// <summary>
/// Imports user-provided Bilibili metadata/subtitle exports into the local structured strategy library.
/// It deliberately does not download or redistribute video files. Network acquisition is left to an
/// explicitly configured external adapter so the core MAA build remains stable and auditable.
/// </summary>
public sealed class BilibiliStrategyImportService
{
    private static readonly Regex BvidRegex = new(@"BV[0-9A-Za-z]{10}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TimePrefixRegex = new(@"^\s*(?:\[(?<m>\d{1,2}):(?<s>\d{2})(?:\.(?<ms>\d{1,3}))?\]|(?<sec>\d+(?:\.\d+)?)s)\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DeployRegex = new(@"(?:部署|下)(?<op>[\p{L}\p{N}·_\-]{1,20})(?:到|在)?(?<tile>[A-Za-z]\d+|\d+\s*[,，]\s*\d+)?(?:朝向|向)?(?<dir>上|下|左|右|up|down|left|right)?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SkillRegex = new(@"(?:(?<op>[\p{L}\p{N}·_\-]{1,20})\s*)?(?:开|使用|释放)(?:技能|技)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RetreatRegex = new(@"(?:撤退|撤)(?<op>[\p{L}\p{N}·_\-]{1,20})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CostRegex = new(@"(?:费用|cost)\s*(?:到|>=|≥)?\s*(?<v>\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex KillsRegex = new(@"(?:击杀|kills?)\s*(?:到|>=|≥)?\s*(?<v>\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public BilibiliImportResult ImportFromFiles(string metadataPath, string transcriptPath, string stageOverride = "", string versionOverride = "")
    {
        var warnings = new List<string>();
        try
        {
            if (string.IsNullOrWhiteSpace(metadataPath) || !File.Exists(metadataPath))
            {
                return new(false, string.Empty, null, warnings, "找不到B站元数据JSON文件。");
            }

            var metadata = JObject.Parse(File.ReadAllText(metadataPath));
            var record = BuildRecord(metadata, stageOverride, versionOverride, warnings);
            if (!string.IsNullOrWhiteSpace(transcriptPath) && File.Exists(transcriptPath))
            {
                record.Steps = ParseTranscript(File.ReadAllLines(transcriptPath), warnings);
                record.Operators = record.Steps.Where(x => !string.IsNullOrWhiteSpace(x.Operator)).Select(x => x.Operator).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            else
            {
                warnings.Add("未提供字幕/人工步骤文本；攻略已作为仅索引记录导入。");
            }

            record.Confidence = CalculateConfidence(record);
            var fileName = SafeName($"{record.Stage}_{record.Bvid}_{DateTimeOffset.Now:yyyyMMddHHmmss}.json");
            var output = Path.Combine(AiDataPaths.BilibiliStrategyDirectory, fileName);
            AtomicWrite(output, JsonConvert.SerializeObject(record, Formatting.Indented));
            return new(true, output, record, warnings, $"已导入攻略索引，提取 {record.Steps.Count} 个候选步骤。");
        }
        catch (Exception ex)
        {
            return new(false, string.Empty, null, warnings, $"导入失败：{ex.Message}");
        }
    }

    public string CreateMetadataExample()
    {
        AiDataPaths.EnsureDirectories();
        var path = Path.Combine(AiDataPaths.BilibiliStrategyDirectory, "bilibili-metadata-example.json");
        if (!File.Exists(path))
        {
            var sample = new JObject
            {
                ["bvid"] = "BVxxxxxxxxxx",
                ["title"] = "关卡低配攻略",
                ["uploader"] = "UP主名称",
                ["published_at"] = DateTimeOffset.Now,
                ["source_url"] = "https://www.bilibili.com/video/BVxxxxxxxxxx",
                ["stage"] = "关卡编号",
                ["game_version"] = "当前游戏版本",
                ["strategy_type"] = "低配",
                ["difficulty"] = "普通",
            };
            AtomicWrite(path, sample.ToString(Formatting.Indented));
        }
        return path;
    }

    public string CreateTranscriptExample()
    {
        AiDataPaths.EnsureDirectories();
        var path = Path.Combine(AiDataPaths.BilibiliStrategyDirectory, "bilibili-transcript-example.txt");
        if (!File.Exists(path))
        {
            AtomicWrite(path, "[00:03] 费用到10，在 3,6 部署桃金娘，朝上\n[00:08] 桃金娘开技能\n[00:15] 击杀到12，在 5,4 部署山，朝左\n[00:38] 撤退桃金娘\n");
        }
        return path;
    }

    private static BilibiliStrategyRecord BuildRecord(JObject metadata, string stageOverride, string versionOverride, List<string> warnings)
    {
        var url = Value(metadata, "source_url", "url", "link");
        var bvid = Value(metadata, "bvid", "bv");
        if (string.IsNullOrWhiteSpace(bvid))
        {
            bvid = BvidRegex.Match(url).Value;
        }
        if (string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(bvid))
        {
            url = $"https://www.bilibili.com/video/{bvid}";
        }
        if (string.IsNullOrWhiteSpace(bvid))
        {
            warnings.Add("未识别到BV号，仍会保存本地索引。");
        }

        return new BilibiliStrategyRecord
        {
            Bvid = bvid,
            Title = Value(metadata, "title", "name"),
            Uploader = Value(metadata, "uploader", "author", "up_name"),
            SourceUrl = url,
            PublishedAt = ParseDate(metadata["published_at"] ?? metadata["pubdate"]),
            IndexedAt = DateTimeOffset.Now,
            Stage = string.IsNullOrWhiteSpace(stageOverride) ? Value(metadata, "stage", "stage_name") : stageOverride.Trim(),
            GameVersion = string.IsNullOrWhiteSpace(versionOverride) ? Value(metadata, "game_version", "version") : versionOverride.Trim(),
            StrategyType = Value(metadata, "strategy_type", "type"),
            Difficulty = Value(metadata, "difficulty"),
            IsCurrentVersion = metadata.Value<bool?>("is_current_version") ?? true,
        };
    }

    private static List<BilibiliStrategyStep> ParseTranscript(IEnumerable<string> lines, List<string> warnings)
    {
        var result = new List<BilibiliStrategyStep>();
        var order = 1;
        foreach (var original in lines)
        {
            var line = original.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;

            var time = ParseTime(ref line);
            var trigger = BuildTrigger(line, time);
            BilibiliStrategyStep? step = null;

            var retreat = RetreatRegex.Match(line);
            if (retreat.Success)
            {
                step = NewStep(order, trigger, "retreat", retreat.Groups["op"].Value, "", "", original);
            }
            else
            {
                var skill = SkillRegex.Match(line);
                if (skill.Success)
                {
                    step = NewStep(order, trigger, "use_skill", skill.Groups["op"].Value, "", "", original);
                }
                else
                {
                    var deploy = DeployRegex.Match(line);
                    if (deploy.Success)
                    {
                        step = NewStep(order, trigger, "deploy", deploy.Groups["op"].Value, NormalizeTile(deploy.Groups["tile"].Value), deploy.Groups["dir"].Value, original);
                    }
                }
            }

            if (step is null)
            {
                warnings.Add($"未能结构化：{original}");
                continue;
            }
            result.Add(step);
            order++;
        }
        return result;
    }

    private static BilibiliStrategyStep NewStep(int order, string trigger, string action, string op, string tile, string direction, string notes) => new()
    {
        Order = order,
        Trigger = trigger,
        Action = action,
        Operator = op.Trim(),
        Tile = tile,
        Direction = direction.Trim(),
        Notes = notes.Trim(),
    };

    private static string BuildTrigger(string line, double? seconds)
    {
        var triggers = new List<string>();
        var cost = CostRegex.Match(line);
        if (cost.Success) triggers.Add($"cost>={cost.Groups["v"].Value}");
        var kills = KillsRegex.Match(line);
        if (kills.Success) triggers.Add($"kills>={kills.Groups["v"].Value}");
        if (triggers.Count == 0 && seconds.HasValue) triggers.Add($"elapsed_seconds>={seconds.Value.ToString("0.###", CultureInfo.InvariantCulture)}");
        return triggers.Count == 0 ? "manual_or_runtime_state" : string.Join(" && ", triggers);
    }

    private static double? ParseTime(ref string line)
    {
        var match = TimePrefixRegex.Match(line);
        if (!match.Success) return null;
        line = line[match.Length..].Trim();
        if (match.Groups["sec"].Success && double.TryParse(match.Groups["sec"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)) return seconds;
        var m = int.TryParse(match.Groups["m"].Value, out var mm) ? mm : 0;
        var s = int.TryParse(match.Groups["s"].Value, out var ss) ? ss : 0;
        var msText = match.Groups["ms"].Value;
        var fraction = string.IsNullOrWhiteSpace(msText) ? 0 : double.Parse("0." + msText, CultureInfo.InvariantCulture);
        return (m * 60) + s + fraction;
    }

    private static string NormalizeTile(string tile)
    {
        var parts = tile.Split([',', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2 ? $"{parts[0]},{parts[1]}" : tile.Trim();
    }

    private static double CalculateConfidence(BilibiliStrategyRecord record)
    {
        var score = 0.2;
        if (!string.IsNullOrWhiteSpace(record.Bvid)) score += 0.1;
        if (!string.IsNullOrWhiteSpace(record.Stage)) score += 0.15;
        if (!string.IsNullOrWhiteSpace(record.GameVersion)) score += 0.15;
        if (!string.IsNullOrWhiteSpace(record.Uploader)) score += 0.05;
        if (record.Steps.Count > 0) score += Math.Min(0.35, record.Steps.Count * 0.025);
        return Math.Clamp(score, 0, 0.95);
    }

    private static string Value(JObject obj, params string[] names)
    {
        foreach (var name in names)
        {
            var value = obj[name]?.ToString();
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }
        return string.Empty;
    }

    private static DateTimeOffset ParseDate(JToken? token)
    {
        if (token is null) return default;
        if (token.Type == JTokenType.Integer && long.TryParse(token.ToString(), out var unix))
        {
            return unix > 10_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(unix) : DateTimeOffset.FromUnixTimeSeconds(unix);
        }
        return DateTimeOffset.TryParse(token.ToString(), out var value) ? value : default;
    }

    private static string SafeName(string value) => string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    private static void AtomicWrite(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, true);
    }
}
