using ZombieModPlugin.Abilities.Utils;
using ZombieModPlugin.Extensions;

namespace ZombieModPlugin.Abilities.Executors;

public class PounceExecutor : Ability
{
    private const float pounceForceMultiplier = 700f;

    public PounceExecutor()
        : base(
            id: "pounce",
            name: "Pounce",
            description: "Jumps forward quickly to close the distance to your target.",
            cooldown: 15f,
            unlockCost: 400,
            duration: 0.1f)
    {
    }

    public override void Execute(AbilityExecutionContext context)
    {
        var player = context.Player;
        var playerPawn = player.PlayerPawn.Value;
        if (playerPawn == null) return;

        var state = context.PlayerState;
        var forward = playerPawn.EyeAngles.ToForwardVector();
        var pounceForce = forward * pounceForceMultiplier;
        pounceForce.Z += 300f;

        playerPawn.Teleport(velocity: pounceForce);

        AbilityUtils.TrackActiveAbilityDuration(player, AbilityType.Pounce, Duration, state);
        context.PlayerState.SetCooldown(AbilityType.Pounce, Cooldown);
    }
}
