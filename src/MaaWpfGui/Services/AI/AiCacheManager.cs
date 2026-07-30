// Part of MaaAssistantArknights, licensed under AGPL-3.0.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MaaWpfGui.Services.AI;

public sealed record AiCacheCleanupResult(long BytesFreed, int FilesDeleted, int FilesSkipped)
{
    public string DisplayText => $"释放 {FormatBytes(BytesFreed)}，删除 {FilesDeleted} 个文件，跳过 {FilesSkipped} 个文件";

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

public sealed class AiCacheManager
{
    private static readonly HashSet<string> ProtectedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ai-knowledge.json",
        "reclamation-experience.json",
    };

    public long CalculateCacheSize()
    {
        AiDataPaths.EnsureDirectories();
        return EnumerateCacheFiles().Sum(SafeLength);
    }

    public AiCacheCleanupResult Cleanup(TimeSpan maxAge, long targetMaximumBytes)
    {
        AiDataPaths.EnsureDirectories();
        var files = EnumerateCacheFiles()
            .Select(path => new FileInfo(path))
            .OrderBy(info => info.LastWriteTimeUtc)
            .ToList();
        var total = files.Sum(info => SafeLength(info.FullName));
        long freed = 0;
        var deleted = 0;
        var skipped = 0;

        foreach (var info in files)
        {
            if (ProtectedFileNames.Contains(info.Name))
            {
                skipped++;
                continue;
            }

            var expired = info.LastWriteTimeUtc < DateTime.UtcNow.Subtract(maxAge);
            var oversized = total - freed > targetMaximumBytes;
            if (!expired && !oversized)
            {
                continue;
            }

            try
            {
                var length = info.Length;
                info.Delete();
                freed += length;
                deleted++;
            }
            catch
            {
                skipped++;
            }
        }

        DeleteEmptyDirectories(AiDataPaths.CacheDirectory);
        return new AiCacheCleanupResult(freed, deleted, skipped);
    }

    public AiCacheCleanupResult PruneExecutionSessions(TimeSpan maxAge, int keepLatest = 50)
    {
        AiDataPaths.EnsureDirectories();
        var files = Directory.EnumerateFiles(AiDataPaths.SessionDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .ToList();
        long freed = 0;
        var deleted = 0;
        var skipped = 0;
        var cutoff = DateTime.UtcNow.Subtract(maxAge);

        for (var index = 0; index < files.Count; index++)
        {
            var info = files[index];
            if (index < Math.Clamp(keepLatest, 1, 500) || info.LastWriteTimeUtc >= cutoff)
            {
                skipped++;
                continue;
            }

            try
            {
                var length = info.Length;
                info.Delete();
                freed += length;
                deleted++;
            }
            catch
            {
                skipped++;
            }
        }

        return new AiCacheCleanupResult(freed, deleted, skipped);
    }

    private static IEnumerable<string> EnumerateCacheFiles()
    {
        if (!Directory.Exists(AiDataPaths.CacheDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(AiDataPaths.CacheDirectory, "*", SearchOption.AllDirectories);
    }

    private static long SafeLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }

    private static void DeleteEmptyDirectories(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(path => path.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }
}
