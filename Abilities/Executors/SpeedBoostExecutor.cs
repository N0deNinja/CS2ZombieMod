using ZombieModPlugin.Abilities.Utils;

namespace ZombieModPlugin.Abilities.Executors;

public class SpeedBoostExecutor : Ability
{
    private const float speedMultiplier = 1.5f;

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

        AbilityUtils.TrackActiveAbilityDuration(player, AbilityType.SpeedBoost, Duration, context.PlayerState);
        context.SetCooldown(AbilityType.SpeedBoost, Cooldown);


        AbilityUtils.ApplySpeedBoost(player, speedMultiplier, Duration);
    }
}
