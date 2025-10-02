using ZombieModPlugin.Abilities;

public class HealthRegenExecutor : Ability
{
    private readonly int healPerTick = 20;

    public HealthRegenExecutor()
        : base(
            name: "Health Regeneration",
            description: "Periodically increases your health.",
            cooldown: 10f,
            unlockCost: 100,
            duration: 4f)
    {
    }

    public override void Execute(AbilityExecutionContext context)
    {
        var player = context.Player;
        var playerPawn = player.PlayerPawn.Value;
        if (playerPawn == null) return;

        context.SetCooldown(AbilityType.HealthRegen, Cooldown);

        AbilityUtils.RunPeriodicEffect(
            player,
            durationSeconds: Duration,
            intervalSeconds: 1f,
            onTick: pawn =>
            {
                var maxHealth = context.ZombieType.Health;
                var newHealth = Math.Min(pawn.Health + healPerTick, maxHealth);
                pawn.Health = newHealth;
            }
        );
    }

}
