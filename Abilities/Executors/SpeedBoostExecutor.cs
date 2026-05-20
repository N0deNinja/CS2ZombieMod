using ZombieModPlugin.Abilities.Utils;

namespace ZombieModPlugin.Abilities.Executors;

public class SpeedBoostExecutor : Ability
{
    public SpeedBoostExecutor()
        : base(
            id: "speed_boost",
            name: "Speed Boost",
            description: "Temporarily increases your movement speed.",
            cooldown: 10f,
            unlockCost: 100,
            duration: 5f)
    {
    }

    public override void Execute(AbilityExecutionContext context)
    {
        var player = context.Player;
        var playerPawn = player.PlayerPawn.Value;
        if (playerPawn == null) return;

        var config = context.Config.AbilityConfig.SpeedBoost;
        var speedMultiplier = Math.Clamp(config.SpeedMultiplier, 0.1f, 4.0f);

        AbilityUtils.TrackActiveAbilityDuration(player, AbilityType.SpeedBoost, config.DurationSeconds, context.PlayerState);
        context.PlayerState.SetCooldown(AbilityType.SpeedBoost, config.CooldownSeconds);

        AbilityUtils.ApplySpeedBoost(player, speedMultiplier, config.DurationSeconds);
    }
}
