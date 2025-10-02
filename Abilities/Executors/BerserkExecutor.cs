using ZombieModPlugin.Abilities;


public class BerserkExecutor : Ability
{
    private const float speedMultiplier = 1.5f;

    public BerserkExecutor()
        : base(
            id: "berserk",
            name: "Berserk",
            description: "Temporarily increases your speed and damage.",
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

        var state = context.PlayerState;

        AbilityUtils.TrackActiveAbilityDuration(player, AbilityType.Berserk, Duration, state);
        AbilityUtils.ApplySpeedBoost(player, speedMultiplier, Duration);

        context.SetCooldown(AbilityType.Berserk, Cooldown);
    }
}
