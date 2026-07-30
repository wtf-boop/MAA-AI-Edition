// Part of MaaAssistantArknights, licensed under AGPL-3.0.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace MaaWpfGui.Services.AI;

public sealed class ArknightsKnowledgeService
{
    private sealed class KnowledgeEntry
    {
        public KnowledgeEntry(string source, string text)
        {
            Source = source;
            Text = text;
        }

        public string Source { get; set; }

        public string Text { get; set; }
    }
    private readonly List<KnowledgeEntry> _entries = [];
    private static string IndexPath => Path.Combine(AiDataPaths.CacheDirectory, "ai-knowledge.json");

    public int Count => _entries.Count;

    public ArknightsKnowledgeService() => LoadIndex();

    public async Task<int> RebuildAsync()
    {
        var roots = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resource"),
            AiDataPaths.KnowledgeDirectory,
            AiDataPaths.BilibiliStrategyDirectory,
        };
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".json", ".txt", ".md" };
        var rebuilt = new List<KnowledgeEntry>();

        await Task.Run(() => {
            foreach (var root in roots.Where(Directory.Exists))
            {
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        if (!allowed.Contains(info.Extension) || info.Length > 4 * 1024 * 1024)
                        {
                            continue;
                        }

                        var text = File.ReadAllText(file);
                        foreach (var chunk in Chunk(text, 1200))
                        {
                            if (chunk.Length >= 40)
                            {
                                rebuilt.Add(new KnowledgeEntry(Path.GetRelativePath(AppDomain.CurrentDomain.BaseDirectory, file), chunk));
                            }
                        }
                    }
                    catch
                    {
                        // Skip malformed or locked files.
                    }
                }
            }
        });

        _entries.Clear();
        _entries.AddRange(rebuilt);
        Directory.CreateDirectory(Path.GetDirectoryName(IndexPath)!);
        File.WriteAllText(IndexPath, JsonConvert.SerializeObject(_entries));
        return _entries.Count;
    }

    public string Search(string query, int maxResults)
    {
        if (_entries.Count == 0 || string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var terms = Tokenize(query).ToArray();
        var hits = _entries
            .Select(entry => new
            {
                Entry = entry,
                Score = terms.Sum(term => CountOccurrences(entry.Text, term) * Math.Max(1, term.Length)),
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(Math.Clamp(maxResults, 1, 12))
            .ToArray();

        var builder = new StringBuilder();
        foreach (var hit in hits)
        {
            builder.AppendLine($"[来源: {hit.Entry.Source}]");
            builder.AppendLine(hit.Entry.Text);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private void LoadIndex()
    {
        try
        {
            if (File.Exists(IndexPath))
            {
                _entries.AddRange(JsonConvert.DeserializeObject<List<KnowledgeEntry>>(File.ReadAllText(IndexPath)) ?? []);
            }
        }
        catch
        {
            _entries.Clear();
        }
    }

    private static IEnumerable<string> Chunk(string text, int size)
    {
        text = text.Replace("\0", string.Empty).Trim();
        for (var i = 0; i < text.Length; i += size)
        {
            yield return text.Substring(i, Math.Min(size, text.Length - i));
        }
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var normalized = new string(text.Where(c => char.IsLetterOrDigit(c) || c >= 0x4E00).ToArray()).ToLowerInvariant();
        foreach (var word in normalized.Split([' ', '\r', '\n', '\t', ',', '.', '?', '!', '，', '。', '？', '！'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.Length >= 2)
            {
                yield return word;
            }
        }

        for (var i = 0; i + 1 < normalized.Length; i++)
        {
            if (normalized[i] >= 0x4E00 && normalized[i + 1] >= 0x4E00)
            {
                yield return normalized.Substring(i, 2);
            }
        }
    }

    private static int CountOccurrences(string text, string term)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(term, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += term.Length;
        }

        return count;
    }
}
