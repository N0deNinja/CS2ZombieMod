using ZombieModPlugin.Abilities.Utils;
using ZombieModPlugin.Sounds;

namespace ZombieModPlugin.Abilities.Executors;

public class HealthRegenExecutor : Ability
{
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

        var config = context.Config.AbilityConfig.HealthRegen;

        context.PlayerState.SetCooldown(AbilityType.HealthRegen, config.CooldownSeconds);
        ZombieSounds.Emit(playerPawn, context.Config, config.ActivationSound);

        AbilityUtils.TrackActiveAbilityDuration(player, AbilityType.HealthRegen, config.DurationSeconds, context.PlayerState);

        AbilityUtils.ApplyHealthRegen(
            player,
            Math.Max(1, config.HealPerTick),
            duration: config.DurationSeconds,
            interval: Math.Max(0.1f, config.TickIntervalSeconds),
            maxHealth: context.PlayerState!.SelectedZombieType!.Health
        );
    }
}
