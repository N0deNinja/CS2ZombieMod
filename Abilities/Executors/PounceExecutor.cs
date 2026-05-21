using ZombieModPlugin.Abilities.Utils;
using ZombieModPlugin.Extensions;
using ZombieModPlugin.Sounds;
using CounterStrikeSharp.API.Modules.Utils;

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
        if (state.IsWallClinging)
            state.ResetWallClingState();

        var forward = GetHorizontalForward(playerPawn.EyeAngles.ToForwardVector());
        var pounceForce = forward * Math.Clamp(config.Force, 0f, 2000f);
        pounceForce.Z += Math.Clamp(config.UpForce, 0f, 1000f);

        playerPawn.Teleport(velocity: pounceForce);
        AbilityVisualUtils.StartPounceTrail(
            player,
            config.TrailMaxDurationSeconds,
            config.TrailTickIntervalSeconds,
            config.TrailSegmentLifetimeSeconds,
            config.TrailFadeAfterLandingSeconds,
            config.TrailMinSegmentDistance,
            config.TrailHeightOffset,
            config.TrailBeamWidth,
            config.TrailBeamMaterial,
            config.TrailMarkerParticle,
            config.TrailMarkerRadiusScale);
        ZombieSounds.EmitAbilityActivation(player, context.Config, config);

        AbilityUtils.TrackActiveAbilityDuration(player, AbilityType.Pounce, config.DurationSeconds, state);
        context.PlayerState.SetCooldown(AbilityType.Pounce, config.CooldownSeconds);
    }

    private static Vector GetHorizontalForward(Vector forward)
    {
        forward.Z = 0.0f;
        var length = MathF.Sqrt(forward.X * forward.X + forward.Y * forward.Y);
        if (length <= 0.001f)
            return new Vector(1.0f, 0.0f, 0.0f);

        return new Vector(forward.X / length, forward.Y / length, 0.0f);
    }
}
