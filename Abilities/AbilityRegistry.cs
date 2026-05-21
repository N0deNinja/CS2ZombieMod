namespace ZombieModPlugin.Abilities
{
    public static class AbilityRegistry
    {
        public static readonly Dictionary<AbilityType, Ability> Abilities = new()
        {
            { AbilityType.Pounce, new Executors.PounceExecutor() },
            { AbilityType.Berserk, new Executors.BerserkExecutor() },
            { AbilityType.HealthRegen, new Executors.HealthRegenExecutor() },
            { AbilityType.SpeedBoost, new Executors.SpeedBoostExecutor() },
            { AbilityType.Invisibility, new Executors.InvisibilityExecutor() },
            { AbilityType.SelfDestruct, new Executors.SelfDestructExecutor() },
            { AbilityType.FrostBolt, new Executors.FrostBoltExecutor() },
            { AbilityType.CultistHex, new Executors.CultistHexExecutor() },
            { AbilityType.WallClimb, new Executors.WallClimbExecutor() }
        };

        public static Ability? Get(AbilityType type) =>
            Abilities.TryGetValue(type, out var ability) ? ability : null;
    }
}
