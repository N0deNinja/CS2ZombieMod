using ZombieModPlugin.Abilities;
using ZombieModPlugin.Progression.Models;

namespace ZombieModPlugin.Configs;

public class ProgressionConfig
{
    public bool Enabled { get; set; } = true;
    public DatabaseConfig Database { get; set; } = new();
    public LevelCurveConfig GlobalLevelCurve { get; set; } = new()
    {
        MaxLevel = 100,
        BaseXp = 100,
        LinearIncrement = 75,
        ExponentialMultiplier = 1.12
    };
    public LevelCurveConfig ClassLevelCurve { get; set; } = new()
    {
        MaxLevel = 20,
        BaseXp = 80,
        LinearIncrement = 55,
        ExponentialMultiplier = 1.10
    };
    public XpRewardsConfig XpRewards { get; set; } = new();
    public int MaxEquippedZombieAbilities { get; set; } = 3;
    public int MaxEquippedHumanAbilities { get; set; } = 2;
    public UnlockDefinition[] ZombieClassUnlocks { get; set; } = DefaultZombieUnlocks();
    public UnlockDefinition[] HumanClassUnlocks { get; set; } = DefaultHumanUnlocks();
    public UnlockDefinition[] AbilityUnlocks { get; set; } = DefaultAbilityUnlocks();

    private static UnlockDefinition[] DefaultZombieUnlocks()
    {
        return
        [
            ClassUnlock(ProgressionClassRole.Zombie, "runner", 3),
            ClassUnlock(ProgressionClassRole.Zombie, "cultist", 6),
            ClassUnlock(ProgressionClassRole.Zombie, "frozen", 8),
            ClassUnlock(ProgressionClassRole.Zombie, "brute", 10),
            ClassUnlock(ProgressionClassRole.Zombie, "lurker", 12),
            ClassUnlock(ProgressionClassRole.Zombie, "molong", 15)
        ];
    }

    private static UnlockDefinition[] DefaultHumanUnlocks()
    {
        return
        [
            ClassUnlock(ProgressionClassRole.Human, "hunter", 2),
            ClassUnlock(ProgressionClassRole.Human, "tac", 4),
            ClassUnlock(ProgressionClassRole.Human, "vip_heavy", 8),
            ClassUnlock(ProgressionClassRole.Human, "vip_tactical", 10)
        ];
    }

    private static UnlockDefinition[] DefaultAbilityUnlocks()
    {
        return
        [
            AbilityUnlock(ProgressionClassRole.Zombie, "classic", AbilityType.Berserk, 2),
            AbilityUnlock(ProgressionClassRole.Zombie, "classic", AbilityType.HealthRegen, 3),
            AbilityUnlock(ProgressionClassRole.Zombie, "runner", AbilityType.Pounce, 2),
            AbilityUnlock(ProgressionClassRole.Zombie, "brute", AbilityType.HealthRegen, 3),
            AbilityUnlock(ProgressionClassRole.Zombie, "brute", AbilityType.SelfDestruct, 5),
            AbilityUnlock(ProgressionClassRole.Zombie, "cultist", AbilityType.Invisibility, 3),
            AbilityUnlock(ProgressionClassRole.Zombie, "cultist", AbilityType.FrostBolt, 5),
            AbilityUnlock(ProgressionClassRole.Zombie, "frozen", AbilityType.HealthRegen, 3),
            AbilityUnlock(ProgressionClassRole.Zombie, "frozen", AbilityType.CultistHex, 5),
            AbilityUnlock(ProgressionClassRole.Zombie, "lurker", AbilityType.Invisibility, 4),
            AbilityUnlock(ProgressionClassRole.Zombie, "lurker", AbilityType.SpeedBoost, 6),
            AbilityUnlock(ProgressionClassRole.Zombie, "molong", AbilityType.Berserk, 3),
            AbilityUnlock(ProgressionClassRole.Zombie, "molong", AbilityType.Invisibility, 5)
        ];
    }

    private static UnlockDefinition ClassUnlock(ProgressionClassRole role, string classId, int globalLevel)
    {
        return new UnlockDefinition
        {
            Kind = role == ProgressionClassRole.Zombie ? UnlockKind.ZombieClass : UnlockKind.HumanClass,
            Role = role,
            ClassId = classId,
            Requirements =
            [
                new RequirementDefinition
                {
                    Type = RequirementType.GlobalLevel,
                    Value = globalLevel
                }
            ]
        };
    }

    private static UnlockDefinition AbilityUnlock(
        ProgressionClassRole role,
        string classId,
        AbilityType ability,
        int classLevel)
    {
        return new UnlockDefinition
        {
            Kind = UnlockKind.Ability,
            Role = role,
            ClassId = classId,
            Ability = ability,
            Requirements =
            [
                new RequirementDefinition
                {
                    Type = RequirementType.ClassLevel,
                    Role = role,
                    ClassId = classId,
                    Value = classLevel
                }
            ]
        };
    }
}

public class DatabaseConfig
{
    public string Provider { get; set; } = "sqlite";
    public string FilePath { get; set; } = "data/zombiemod_progression.db";
}

public class LevelCurveConfig
{
    public LevelCurveMode Mode { get; set; } = LevelCurveMode.Exponential;
    public int MaxLevel { get; set; } = 100;
    public int BaseXp { get; set; } = 100;
    public int LinearIncrement { get; set; } = 50;
    public double ExponentialMultiplier { get; set; } = 1.12;
    public int[] XpTable { get; set; } = [];
}

public class XpRewardsConfig
{
    public XpRewardDefinition Infection { get; set; } = new()
    {
        GlobalXp = 35,
        ClassXp = 45,
        Message = "infection"
    };
    public XpRewardDefinition ZombieKill { get; set; } = new()
    {
        GlobalXp = 30,
        ClassXp = 40,
        Message = "zombie kill"
    };
    public XpRewardDefinition HumanKill { get; set; } = new()
    {
        GlobalXp = 35,
        ClassXp = 45,
        Message = "human infected"
    };
    public XpRewardDefinition RoundWin { get; set; } = new()
    {
        GlobalXp = 60,
        ClassXp = 70,
        Message = "round win"
    };
    public XpRewardDefinition HumanSurvival { get; set; } = new()
    {
        GlobalXp = 45,
        ClassXp = 55,
        Message = "surviving"
    };
    public XpRewardDefinition Assist { get; set; } = new()
    {
        GlobalXp = 12,
        ClassXp = 18,
        Message = "assist"
    };

    public XpRewardDefinition GetReward(ProgressionRewardType type)
    {
        return type switch
        {
            ProgressionRewardType.Infection => Infection,
            ProgressionRewardType.ZombieKill => ZombieKill,
            ProgressionRewardType.HumanKill => HumanKill,
            ProgressionRewardType.RoundWin => RoundWin,
            ProgressionRewardType.HumanSurvival => HumanSurvival,
            ProgressionRewardType.Assist => Assist,
            _ => new XpRewardDefinition()
        };
    }
}

public class XpRewardDefinition
{
    public int GlobalXp { get; set; }
    public int ClassXp { get; set; }
    public string Message { get; set; } = "";
}

public class UnlockDefinition
{
    public UnlockKind Kind { get; set; }
    public ProgressionClassRole Role { get; set; }
    public string ClassId { get; set; } = "";
    public AbilityType? Ability { get; set; }
    public RequirementDefinition[] Requirements { get; set; } = [];
}

public class RequirementDefinition
{
    public RequirementType Type { get; set; }
    public ProgressionClassRole? Role { get; set; }
    public string ClassId { get; set; } = "";
    public int Value { get; set; }
    public string StatKey { get; set; } = "";
    public string AchievementId { get; set; } = "";
}
