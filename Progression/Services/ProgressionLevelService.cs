using ZombieModPlugin.Configs;
using ZombieModPlugin.Progression.Models;

namespace ZombieModPlugin.Progression.Services;

public sealed class ProgressionLevelService
{
    public int GetRequiredXpForNextLevel(int currentLevel, LevelCurveConfig curve)
    {
        currentLevel = Math.Max(1, currentLevel);

        if (currentLevel >= Math.Max(1, curve.MaxLevel))
            return 0;

        return curve.Mode switch
        {
            LevelCurveMode.Linear => Math.Max(1, curve.BaseXp + ((currentLevel - 1) * Math.Max(0, curve.LinearIncrement))),
            LevelCurveMode.Table => GetTableXp(currentLevel, curve),
            _ => GetExponentialXp(currentLevel, curve)
        };
    }

    public int ApplyXp(
        int currentLevel,
        int currentXp,
        int xpToAdd,
        LevelCurveConfig curve,
        out int remainingXp,
        out int levelsGained)
    {
        var level = Math.Clamp(currentLevel, 1, Math.Max(1, curve.MaxLevel));
        var xp = Math.Max(0, currentXp) + Math.Max(0, xpToAdd);
        levelsGained = 0;

        while (level < Math.Max(1, curve.MaxLevel))
        {
            var requiredXp = GetRequiredXpForNextLevel(level, curve);
            if (requiredXp <= 0 || xp < requiredXp)
                break;

            xp -= requiredXp;
            level++;
            levelsGained++;
        }

        remainingXp = xp;
        return level;
    }

    private static int GetTableXp(int currentLevel, LevelCurveConfig curve)
    {
        if (curve.XpTable.Length >= currentLevel)
            return Math.Max(1, curve.XpTable[currentLevel - 1]);

        return Math.Max(1, curve.BaseXp + ((currentLevel - 1) * Math.Max(0, curve.LinearIncrement)));
    }

    private static int GetExponentialXp(int currentLevel, LevelCurveConfig curve)
    {
        var multiplier = Math.Max(1.0, curve.ExponentialMultiplier);
        var xp = curve.BaseXp * Math.Pow(multiplier, currentLevel - 1);
        xp += (currentLevel - 1) * Math.Max(0, curve.LinearIncrement);
        return Math.Max(1, (int)Math.Round(xp));
    }
}
