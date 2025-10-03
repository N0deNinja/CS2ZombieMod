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
            { AbilityType.Invisibility, new Executors.InvisibilityExecutor() }
        };

        public static Ability? Get(AbilityType type) =>
            Abilities.TryGetValue(type, out var ability) ? ability : null;
    }
}
