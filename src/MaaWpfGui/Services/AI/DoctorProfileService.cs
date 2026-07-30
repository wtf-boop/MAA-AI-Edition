// Part of MaaAssistantArknights, licensed under AGPL-3.0.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace MaaWpfGui.Services.AI;

public sealed class DoctorOperatorProfile
{
    public string Name { get; set; } = string.Empty;
    public int Elite { get; set; }
    public int Level { get; set; }
    public int Potential { get; set; }
    public int ModuleLevel { get; set; }
    public List<int> Mastery { get; set; } = [];
}

public sealed class DoctorProfile
{
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public string GameServer { get; set; } = string.Empty;
    public List<DoctorOperatorProfile> Operators { get; set; } = [];
}

public sealed class DoctorProfileService
{
    private static string StorePath => Path.Combine(AiDataPaths.ConfigDirectory, "doctor-profile.json");
    public DoctorProfile Profile { get; private set; } = new();

    public DoctorProfileService()
    {
        AiDataPaths.EnsureDirectories();
        Load();
    }

    public void Load()
    {
        try
        {
            Profile = File.Exists(StorePath)
                ? JsonConvert.DeserializeObject<DoctorProfile>(File.ReadAllText(StorePath)) ?? new DoctorProfile()
                : new DoctorProfile();
        }
        catch
        {
            Profile = new DoctorProfile();
        }
    }

    public string CreateExampleFile()
    {
        if (Profile.Operators.Count == 0)
        {
            Profile = new DoctorProfile
            {
                GameServer = "官服",
                Operators =
                [
                    new DoctorOperatorProfile { Name = "桃金娘", Elite = 2, Level = 40, Potential = 6, Mastery = [3, 0, 0] },
                    new DoctorOperatorProfile { Name = "山", Elite = 2, Level = 60, Potential = 1, ModuleLevel = 0, Mastery = [0, 3, 0] },
                ],
            };
            Save();
        }

        return StorePath;
    }

    public void Save()
    {
        Profile.UpdatedAt = DateTimeOffset.Now;
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, JsonConvert.SerializeObject(Profile, Formatting.Indented));
    }

    public double CompatibilityScore(IEnumerable<string> requiredOperators)
    {
        var required = requiredOperators.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (required.Length == 0)
        {
            return 1;
        }

        var owned = Profile.Operators.Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return required.Count(owned.Contains) / (double)required.Length;
    }

    public string GetSummary()
    {
        if (Profile.Operators.Count == 0)
        {
            return "尚未导入账号画像；攻略评分暂不考虑你的干员池。";
        }

        var eliteTwo = Profile.Operators.Count(item => item.Elite >= 2);
        return $"已记录 {Profile.Operators.Count} 名干员，其中精二 {eliteTwo} 名；更新时间 {Profile.UpdatedAt:yyyy-MM-dd HH:mm}。";
    }
}
