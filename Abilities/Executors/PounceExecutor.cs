using ZombieModPlugin.Abilities.Utils;
using ZombieModPlugin.Extensions;
using ZombieModPlugin.Sounds;

namespace ZombieModPlugin.Abilities.Executors;

public class PounceExecutor : Ability
{
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
        var config = context.Config.AbilityConfig.Pounce;
        var forward = playerPawn.EyeAngles.ToForwardVector();
        var pounceForce = forward * Math.Clamp(config.Force, 0f, 2000f);
        pounceForce.Z += Math.Clamp(config.UpForce, 0f, 1000f);

        playerPawn.Teleport(velocity: pounceForce);
        ZombieSounds.EmitWithExtras(playerPawn, context.Config, config.ActivationSound, config.ExtraActivationSounds);

        AbilityUtils.TrackActiveAbilityDuration(player, AbilityType.Pounce, config.DurationSeconds, state);
        context.PlayerState.SetCooldown(AbilityType.Pounce, config.CooldownSeconds);
    }
}
