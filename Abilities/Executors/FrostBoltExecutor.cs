using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System.Drawing;
using ZombieModPlugin.Abilities.Utils;
using ZombieModPlugin.Configs;
using ZombieModPlugin.Extensions;
using ZombieModPlugin.Sounds;

namespace ZombieModPlugin.Abilities.Executors;

public class FrostBoltExecutor : Ability
{
    private const float Epsilon = 0.0001f;

    public FrostBoltExecutor()
        : base(
            id: "frost_bolt",
            name: "Frost Bolt",
            description: "Launches an icy projectile that slows the first human it hits.",
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
        var forward = Normalize(pawn.EyeAngles.ToForwardVector());
        var startPosition = Add(
            Add(pawn.AbsOrigin, Scale(forward, Math.Clamp(config.SpawnForwardOffset, 0f, 128f))),
            new Vector(0f, 0f, Math.Clamp(config.SpawnUpOffset, 0f, 96f)));

        ZombieSounds.Emit(pawn, context.Config, config.CastSound);
        SpawnParticle(config.CastParticle, startPosition, 0.35f);
        LaunchProjectile(context, startPosition, forward, config);

        player.PrintToChat($"{context.Config.ChatConfig.ZombiePrefix} Frost Bolt launched.");
        context.PlayerState.SetCooldown(AbilityType.FrostBolt, config.CooldownSeconds);
        AbilityUtils.TrackActiveAbilityDuration(player, AbilityType.FrostBolt, config.DurationSeconds, context.PlayerState);
    }

    private static void LaunchProjectile(
        AbilityExecutionContext context,
        Vector startPosition,
        Vector direction,
        FrostBoltAbilityConfig config)
    {
        var projectile = new FrostBoltProjectile
        {
            Position = Copy(startPosition),
            Direction = direction,
            Speed = Math.Clamp(config.Speed, 300f, 4000f),
            Range = Math.Clamp(config.Range, 64f, 4096f),
            HitRadius = Math.Clamp(config.HitRadius, 8f, 96f),
            TickIntervalSeconds = Math.Clamp(config.TickIntervalSeconds, 0.01f, 0.1f),
            SlowMultiplier = Math.Clamp(config.SlowMultiplier, 0.1f, 1.0f),
            SlowDurationSeconds = Math.Clamp(config.DurationSeconds, 0.1f, 20f),
            HitParticle = config.HitParticle,
            HitParticleLifetimeSeconds = Math.Clamp(config.HitParticleLifetimeSeconds, 0.1f, 5f),
            BeamMaterial = config.BeamMaterial,
            BeamWidth = Math.Clamp(config.BeamWidth, 0.5f, 16f),
            BeamLifetimeSeconds = Math.Clamp(config.BeamLifetimeSeconds, 0.03f, 1f),
            HitSound = config.HitSound,
            HitMessage = config.HitMessage
        };

        projectile.TrailParticle = CreateParticle(config.ProjectileParticle, startPosition);
        ScheduleProjectileTick(context, projectile);
    }

    private static void ScheduleProjectileTick(AbilityExecutionContext context, FrostBoltProjectile projectile)
    {
        _ = Task.Delay(TimeSpan.FromSeconds(projectile.TickIntervalSeconds)).ContinueWith(_ =>
        {
            Server.NextFrame(() =>
            {
                if (StepProjectile(context, projectile))
                    return;

                ScheduleProjectileTick(context, projectile);
            });
        });
    }

    private static bool StepProjectile(AbilityExecutionContext context, FrostBoltProjectile projectile)
    {
        if (!context.Player.IsValid || !context.Player.PawnIsAlive)
        {
            RemoveEntity(projectile.TrailParticle);
            return true;
        }

        var remainingRange = projectile.Range - projectile.TraveledDistance;
        if (remainingRange <= 0f)
        {
            RemoveEntity(projectile.TrailParticle);
            return true;
        }

        var stepDistance = Math.Min(projectile.Speed * projectile.TickIntervalSeconds, remainingRange);
        var nextPosition = Add(projectile.Position, Scale(projectile.Direction, stepDistance));
        var hitTarget = FindProjectileHit(context, projectile.Position, nextPosition, projectile.HitRadius);

        SpawnBeamSegment(projectile.Position, nextPosition, projectile);
        MoveParticle(projectile.TrailParticle, nextPosition);
        projectile.TraveledDistance += stepDistance;

        if (hitTarget != null)
        {
            ApplyHit(context, projectile, hitTarget, nextPosition);
            return true;
        }

        projectile.Position = nextPosition;

        if (projectile.TraveledDistance >= projectile.Range)
        {
            RemoveEntity(projectile.TrailParticle);
            return true;
        }

        return false;
    }

    private static CCSPlayerController? FindProjectileHit(
        AbilityExecutionContext context,
        Vector segmentStart,
        Vector segmentEnd,
        float hitRadius)
    {
        var hitRadiusSquared = hitRadius * hitRadius;

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
            var capsuleBottom = Add(targetOrigin, new Vector(0f, 0f, 8f));
            var capsuleTop = Add(targetOrigin, new Vector(0f, 0f, 72f));
            var distanceSquared = DistanceSegmentToSegmentSquared(segmentStart, segmentEnd, capsuleBottom, capsuleTop);
            if (distanceSquared <= hitRadiusSquared)
                return candidate;
        }

        return null;
    }

    private static void ApplyHit(
        AbilityExecutionContext context,
        FrostBoltProjectile projectile,
        CCSPlayerController target,
        Vector impactPosition)
    {
        RemoveEntity(projectile.TrailParticle);
        SpawnParticle(projectile.HitParticle, impactPosition, projectile.HitParticleLifetimeSeconds);

        var targetPawn = target.PlayerPawn.Value;
        if (targetPawn != null && targetPawn.IsValid)
            ZombieSounds.Emit(targetPawn, context.Config, projectile.HitSound);

        AbilityUtils.ApplySpeedModifier(target, projectile.SlowMultiplier, projectile.SlowDurationSeconds);
        target.PrintToChat($"{context.Config.ChatConfig.ZombiePrefix} {projectile.HitMessage}");
        context.Player.PrintToChat($"{context.Config.ChatConfig.ZombiePrefix} Frost Bolt chilled {target.PlayerName}.");
    }

    private static CParticleSystem? CreateParticle(string particleName, Vector position)
    {
        if (string.IsNullOrWhiteSpace(particleName))
            return null;

        var particle = Utilities.CreateEntityByName<CEnvParticleGlow>("env_particle_glow");
        if (particle == null || !particle.IsValid)
            return null;

        particle.EffectName = particleName;
        particle.StartActive = true;
        particle.Active = true;
        particle.NoSave = true;
        particle.Tint = Color.FromArgb(255, 155, 225, 255);
        particle.ColorTint = Color.FromArgb(255, 155, 225, 255);
        particle.AlphaScale = 1.0f;
        particle.RadiusScale = 1.35f;
        particle.Teleport(position, null, null);
        particle.DispatchSpawn();
        particle.AcceptInput("Start");

        return particle;
    }

    private static void SpawnBeamSegment(Vector startPosition, Vector endPosition, FrostBoltProjectile projectile)
    {
        if (string.IsNullOrWhiteSpace(projectile.BeamMaterial))
            return;

        var beam = Utilities.CreateEntityByName<CBeam>("env_beam");
        if (beam == null || !beam.IsValid)
            return;

        beam.Width = projectile.BeamWidth;
        beam.EndWidth = 0.5f;
        beam.Render = Color.FromArgb(210, 115, 220, 255);
        beam.SetModel(projectile.BeamMaterial);
        beam.Teleport(startPosition, null, null);
        beam.DispatchSpawn();
        beam.EndPos.X = endPosition.X;
        beam.EndPos.Y = endPosition.Y;
        beam.EndPos.Z = endPosition.Z;
        Utilities.SetStateChanged(beam, "CBeam", "m_vecEndPos");
        ScheduleEntityRemoval(beam, projectile.BeamLifetimeSeconds);
    }

    private static void SpawnParticle(string particleName, Vector position, float lifetimeSeconds)
    {
        var particle = CreateParticle(particleName, position);
        if (particle != null)
            ScheduleEntityRemoval(particle, lifetimeSeconds);
    }

    private static void MoveParticle(CParticleSystem? particle, Vector position)
    {
        if (particle == null || !particle.IsValid)
            return;

        particle.Teleport(position, null, null);
    }

    private static void ScheduleEntityRemoval(CEntityInstance? entity, float delaySeconds)
    {
        _ = Task.Delay(TimeSpan.FromSeconds(delaySeconds)).ContinueWith(_ =>
        {
            Server.NextFrame(() => RemoveEntity(entity));
        });
    }

    private static void RemoveEntity(CEntityInstance? entity)
    {
        if (entity == null || !entity.IsValid)
            return;

        entity.Remove();
    }

    private static float DistanceSegmentToSegmentSquared(Vector p1, Vector q1, Vector p2, Vector q2)
    {
        var d1 = Subtract(q1, p1);
        var d2 = Subtract(q2, p2);
        var r = Subtract(p1, p2);
        var a = Dot(d1, d1);
        var e = Dot(d2, d2);
        var f = Dot(d2, r);
        float s;
        float t;

        if (a <= Epsilon && e <= Epsilon)
            return LengthSquared(Subtract(p1, p2));

        if (a <= Epsilon)
        {
            s = 0f;
            t = Math.Clamp(f / e, 0f, 1f);
        }
        else
        {
            var c = Dot(d1, r);
            if (e <= Epsilon)
            {
                t = 0f;
                s = Math.Clamp(-c / a, 0f, 1f);
            }
            else
            {
                var b = Dot(d1, d2);
                var denominator = a * e - b * b;
                s = denominator != 0f
                    ? Math.Clamp((b * f - c * e) / denominator, 0f, 1f)
                    : 0f;

                t = (b * s + f) / e;
                if (t < 0f)
                {
                    t = 0f;
                    s = Math.Clamp(-c / a, 0f, 1f);
                }
                else if (t > 1f)
                {
                    t = 1f;
                    s = Math.Clamp((b - c) / a, 0f, 1f);
                }
            }
        }

        var closestOnProjectile = Add(p1, Scale(d1, s));
        var closestOnTarget = Add(p2, Scale(d2, t));
        return LengthSquared(Subtract(closestOnProjectile, closestOnTarget));
    }

    private static Vector Normalize(Vector vector)
    {
        var length = MathF.Sqrt(LengthSquared(vector));
        if (length <= Epsilon)
            return new Vector(1f, 0f, 0f);

        return new Vector(vector.X / length, vector.Y / length, vector.Z / length);
    }

    private static Vector Copy(Vector vector)
    {
        return new Vector(vector.X, vector.Y, vector.Z);
    }

    private static Vector Add(Vector a, Vector b)
    {
        return new Vector(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    }

    private static Vector Subtract(Vector a, Vector b)
    {
        return new Vector(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    }

    private static Vector Scale(Vector vector, float multiplier)
    {
        return new Vector(vector.X * multiplier, vector.Y * multiplier, vector.Z * multiplier);
    }

    private static float Dot(Vector a, Vector b)
    {
        return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    }

    private static float LengthSquared(Vector vector)
    {
        return Dot(vector, vector);
    }

    private sealed class FrostBoltProjectile
    {
        public required Vector Position { get; set; }
        public required Vector Direction { get; init; }
        public required float Speed { get; init; }
        public required float Range { get; init; }
        public required float HitRadius { get; init; }
        public required float TickIntervalSeconds { get; init; }
        public required float SlowMultiplier { get; init; }
        public required float SlowDurationSeconds { get; init; }
        public required string HitParticle { get; init; }
        public required float HitParticleLifetimeSeconds { get; init; }
        public required string BeamMaterial { get; init; }
        public required float BeamWidth { get; init; }
        public required float BeamLifetimeSeconds { get; init; }
        public required string HitSound { get; init; }
        public required string HitMessage { get; init; }
        public float TraveledDistance { get; set; }
        public CParticleSystem? TrailParticle { get; set; }
    }
}
