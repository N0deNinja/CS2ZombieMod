using ZombieModPlugin.Abilities;
using ZombieModPlugin.Configs;
using ZombieModPlugin.States;
using ZombieModPlugin.Progression.Models;

namespace ZombieModPlugin.Progression.Services;

public sealed class ProgressionUnlockService
{
    private readonly BaseConfig _config;

    public ProgressionUnlockService(BaseConfig config)
    {
        _config = config;
    }

    public UnlockDefinition? GetClassUnlockDefinition(ProgressionClassRole role, string classId)
    {
        var definitions = role == ProgressionClassRole.Zombie
            ? _config.ProgressionConfig.ZombieClassUnlocks
            : _config.ProgressionConfig.HumanClassUnlocks;

        return definitions.FirstOrDefault(definition =>
            string.Equals(definition.ClassId, classId, StringComparison.OrdinalIgnoreCase));
    }

    public UnlockDefinition? GetAbilityUnlockDefinition(ProgressionClassRole role, string classId, AbilityType ability)
    {
        return _config.ProgressionConfig.AbilityUnlocks.FirstOrDefault(definition =>
            definition.Kind == UnlockKind.Ability
            && definition.Role == role
            && definition.Ability == ability
            && string.Equals(definition.ClassId, classId, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<RequirementCheck> CheckRequirements(PlayerState state, UnlockDefinition? definition)
    {
        if (definition == null || definition.Requirements.Length == 0)
            return [];

        return definition.Requirements
            .Select(requirement => CheckRequirement(state, definition, requirement))
            .ToArray();
    }

    public bool RequirementsMet(PlayerState state, UnlockDefinition? definition)
    {
        return CheckRequirements(state, definition).All(check => check.IsMet);
    }

    public string FormatRequirements(PlayerState state, UnlockDefinition? definition)
    {
        var checks = CheckRequirements(state, definition);
        if (checks.Count == 0)
            return "free";

        return string.Join(", ", checks.Select(check => check.Description));
    }

    private RequirementCheck CheckRequirement(
        PlayerState state,
        UnlockDefinition definition,
        RequirementDefinition requirement)
    {
        return requirement.Type switch
        {
            RequirementType.GlobalLevel => CheckGlobalLevel(state, requirement.Value),
            RequirementType.ClassLevel => CheckClassLevel(state, definition, requirement),
            RequirementType.Currency => CheckCurrency(state, requirement.Value),
            RequirementType.Achievement => CheckAchievement(state, requirement),
            RequirementType.Statistic => CheckStatistic(state, requirement.StatKey, requirement.Value, requirement.StatKey),
            _ => new RequirementCheck { IsMet = true, Description = "free" }
        };
    }

    private static RequirementCheck CheckGlobalLevel(PlayerState state, int requiredLevel)
    {
        requiredLevel = Math.Max(1, requiredLevel);
        return new RequirementCheck
        {
            IsMet = state.GlobalLevel >= requiredLevel,
            Description = $"global level {requiredLevel} (you {Math.Max(1, state.GlobalLevel)}/{requiredLevel})"
        };
    }

    private static RequirementCheck CheckClassLevel(
        PlayerState state,
        UnlockDefinition definition,
        RequirementDefinition requirement)
    {
        var role = requirement.Role ?? definition.Role;
        var classId = string.IsNullOrWhiteSpace(requirement.ClassId)
            ? definition.ClassId
            : requirement.ClassId;
        var level = GetClassLevel(state, role, classId);
        var requiredLevel = Math.Max(1, requirement.Value);

        return new RequirementCheck
        {
            IsMet = level >= requiredLevel,
            Description = $"{classId} level {requiredLevel} (you {level}/{requiredLevel})"
        };
    }

    private static RequirementCheck CheckCurrency(PlayerState state, int value)
    {
        value = Math.Max(0, value);
        return new RequirementCheck
        {
            IsMet = state.Money >= value,
            Description = $"money ${value} (you ${Math.Max(0, state.Money)})"
        };
    }

    private static RequirementCheck CheckStatistic(PlayerState state, string statKey, int value, string label)
    {
        if (string.IsNullOrWhiteSpace(statKey))
            statKey = label;

        state.Statistics.TryGetValue(statKey, out var currentValue);
        return new RequirementCheck
        {
            IsMet = currentValue >= value,
            Description = $"{label} {value} (you {currentValue}/{value})"
        };
    }

    private static RequirementCheck CheckAchievement(PlayerState state, RequirementDefinition requirement)
    {
        var key = $"achievement:{requirement.AchievementId}";
        state.Statistics.TryGetValue(key, out var value);
        return new RequirementCheck
        {
            IsMet = value > 0,
            Description = $"achievement {requirement.AchievementId}"
        };
    }

    private static int GetClassLevel(PlayerState state, ProgressionClassRole role, string classId)
    {
        if (role == ProgressionClassRole.Zombie)
        {
            return state.ZombieProgression.TryGetValue(classId, out var progression)
                ? Math.Max(1, progression.Level)
                : 1;
        }

        return state.HumanProgression.TryGetValue(classId, out var humanProgression)
            ? Math.Max(1, humanProgression.Level)
            : 1;
    }
}
