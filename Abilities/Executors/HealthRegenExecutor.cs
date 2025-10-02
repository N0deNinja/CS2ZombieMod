using ZombieModPlugin.Abilities.Utils;

namespace ZombieModPlugin.Abilities.Executors;

public class HealthRegenExecutor : Ability
{
    private readonly int healPerTick = 20;

    public HealthRegenExecutor()
        : base(
            id: "health_regen",
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

        AbilityUtils.TrackActiveAbilityDuration(player, AbilityType.HealthRegen, Duration, context.PlayerState);

        AbilityUtils.ApplyHealthRegen(
            player,
            healPerTick,
            duration: Duration,
            interval: 1f,
            maxHealth: context.ZombieType.Health
        );
    }
}