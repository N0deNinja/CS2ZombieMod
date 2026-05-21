using System.Text.Json.Serialization;
using ZombieModPlugin.Abilities;

namespace ZombieModPlugin.Progression.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProgressionClassRole
{
    Zombie,
    Human
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProgressionRewardType
{
    Infection,
    ZombieKill,
    HumanKill,
    RoundWin,
    HumanSurvival,
    Assist,
    Bonus
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LevelCurveMode
{
    Linear,
    Exponential,
    Table
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UnlockKind
{
    ZombieClass,
    HumanClass,
    Ability
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RequirementType
{
    GlobalLevel,
    ClassLevel,
    Currency,
    Achievement,
    Statistic
}

public readonly record struct ProgressionClassKey(ProgressionClassRole Role, string ClassId);

public readonly record struct EquippedAbilityRecord(
    ProgressionClassRole Role,
    string ClassId,
    AbilityType Ability,
    int Slot);

public sealed class PlayerProgressionData
{
    public ulong SteamId { get; set; }
    public string PlayerName { get; set; } = "";
    public int GlobalLevel { get; set; } = 1;
    public int GlobalXp { get; set; }
    public int Money { get; set; }
    public string SelectedZombieClassId { get; set; } = "";
    public string SelectedHumanClassId { get; set; } = "";
    public Dictionary<ProgressionClassKey, ClassProgressionData> ClassProgression { get; } = [];
    public HashSet<string> UnlockedZombieClassIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> UnlockedHumanClassIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<EquippedAbilityRecord> EquippedAbilities { get; } = [];
    public Dictionary<int, string> AbilitySlotBinds { get; } = [];
    public Dictionary<string, long> Statistics { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ClassProgressionData
{
    public int Level { get; set; } = 1;
    public int Xp { get; set; }
    public HashSet<AbilityType> UnlockedAbilities { get; } = [];
}

public sealed class ProgressionAwardResult
{
    public int GlobalXpAwarded { get; set; }
    public int ClassXpAwarded { get; set; }
    public int GlobalLevelsGained { get; set; }
    public int ClassLevelsGained { get; set; }
    public int NewGlobalLevel { get; set; }
    public int NewClassLevel { get; set; }
    public string ClassName { get; set; } = "";
}

public sealed class RequirementCheck
{
    public bool IsMet { get; set; }
    public string Description { get; set; } = "";
}

public sealed class UnlockAttemptResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}
