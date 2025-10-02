using ZombieModPlugin.Abilities;

public class SpeedBoostExecutor : Ability
{
    public SpeedBoostExecutor()
        : base(
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

        AbilityUtils.RunTimedEffect(
            player,
            Duration,
            apply: pawn => pawn.VelocityModifier *= 1.5f,
            revert: pawn => pawn.VelocityModifier /= 1.5f
        );

        context.SetCooldown(AbilityType.SpeedBoost, Cooldown);
    }
}
