using CounterStrikeSharp.API.Core;
using ZombieModPlugin.Abilities.Utils;
using ZombieModPlugin.Extensions;

namespace ZombieModPlugin.Abilities.Executors;

public class FrostBoltExecutor : Ability
{
    public FrostBoltExecutor()
        : base(
            id: "frost_bolt",
            name: "Frost Bolt",
            description: "Chills the human you are aiming at and slows them briefly.",
            cooldown: 12f,
            unlockCost: 350,
            duration: 3f)
    {
    }

    public override void Execute(AbilityExecutionContext context)
    {
        var player = context.Player;
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null)
            return;

        var config = context.Config.AbilityConfig.FrostBolt;
        var range = Math.Clamp(config.Range, 64f, 4096f);
        var coneDot = Math.Clamp(config.AimConeDot, -1.0f, 1.0f);
        var slowMultiplier = Math.Clamp(config.SlowMultiplier, 0.1f, 1.0f);

        var target = FindTargetInAimCone(context, range, coneDot);
        if (target == null)
        {
            player.PrintToChat($"{context.Config.ChatConfig.ZombiePrefix} Frost Bolt found no human target.");
            context.PlayerState.SetCooldown(AbilityType.FrostBolt, config.CooldownSeconds);
            return;
        }

        AbilityUtils.ApplySpeedModifier(target, slowMultiplier, config.DurationSeconds);
        target.PrintToChat($"{context.Config.ChatConfig.ZombiePrefix} {config.HitMessage}");
        player.PrintToChat($"{context.Config.ChatConfig.ZombiePrefix} Frost Bolt chilled {target.PlayerName}.");

        context.PlayerState.SetCooldown(AbilityType.FrostBolt, config.CooldownSeconds);
        AbilityUtils.TrackActiveAbilityDuration(player, AbilityType.FrostBolt, config.DurationSeconds, context.PlayerState);
    }

    private static CCSPlayerController? FindTargetInAimCone(AbilityExecutionContext context, float range, float coneDot)
    {
        var casterPawn = context.Player.PlayerPawn.Value;
        if (casterPawn == null || !casterPawn.IsValid || casterPawn.AbsOrigin == null)
            return null;

        var origin = casterPawn.AbsOrigin;
        var forward = casterPawn.EyeAngles.ToForwardVector();
        CCSPlayerController? bestTarget = null;
        var bestScore = float.MinValue;

        foreach (var candidate in context.AllPlayers)
        {
            if (!candidate.IsValid || !candidate.PawnIsAlive || candidate == context.Player)
                continue;

            var candidateState = candidate.GetState(context.PlayerStates);
            if (candidateState.IsZombie)
                continue;

            var targetPawn = candidate.PlayerPawn.Value;
            if (targetPawn == null || !targetPawn.IsValid || targetPawn.AbsOrigin == null)
                continue;

            var targetOrigin = targetPawn.AbsOrigin;
            var dx = targetOrigin.X - origin.X;
            var dy = targetOrigin.Y - origin.Y;
            var dz = targetOrigin.Z + 32.0f - origin.Z;
            var distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            if (distance <= 0.001f || distance > range)
                continue;

            var dot = (dx / distance * forward.X) + (dy / distance * forward.Y) + (dz / distance * forward.Z);
            if (dot < coneDot)
                continue;

            var score = dot * 10000.0f - distance;
            if (score > bestScore)
            {
                bestScore = score;
                bestTarget = candidate;
            }
        }

        return bestTarget;
    }
}
