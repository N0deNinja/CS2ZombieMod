using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System.Drawing;
using ZombieModPlugin.Extensions;

namespace ZombieModPlugin.Abilities.Utils;

public static class AbilityVisualUtils
{
    private const float LandingGraceSeconds = 0.18f;
    private static readonly Color TrailColor = Color.FromArgb(210, 255, 0, 0);

    public static void StartPounceTrail(
        CCSPlayerController player,
        float maxDurationSeconds,
        float tickIntervalSeconds,
        float segmentLifetimeSeconds,
        float fadeAfterLandingSeconds,
        float minSegmentDistance,
        float heightOffset,
        float beamWidth,
        string beamMaterial,
        string markerParticle,
        float markerRadiusScale)
    {
        if (string.IsNullOrWhiteSpace(beamMaterial) && string.IsNullOrWhiteSpace(markerParticle))
            return;

        if (!TryGetPawn(player, out var pawn) || !TryGetTrailPosition(pawn, heightOffset, out var startPosition))
            return;

        var trail = new PounceTrail
        {
            LastPosition = startPosition,
            StartedAtUtc = DateTime.UtcNow,
            EndsAtUtc = DateTime.UtcNow.AddSeconds(Math.Clamp(maxDurationSeconds, 0.1f, 10.0f)),
            TickIntervalSeconds = Math.Clamp(tickIntervalSeconds, 0.01f, 0.2f),
            SegmentLifetimeSeconds = Math.Clamp(segmentLifetimeSeconds, 0.1f, 15.0f),
            FadeAfterLandingSeconds = Math.Clamp(fadeAfterLandingSeconds, 0.0f, 5.0f),
            MinSegmentDistance = Math.Clamp(minSegmentDistance, 1.0f, 128.0f),
            HeightOffset = Math.Clamp(heightOffset, 0.0f, 96.0f),
            BeamWidth = Math.Clamp(beamWidth, 0.2f, 8.0f),
            BeamMaterial = beamMaterial,
            MarkerParticle = markerParticle,
            MarkerRadiusScale = Math.Clamp(markerRadiusScale, 0.1f, 4.0f)
        };

        SpawnTrailMarker(startPosition, trail);
        SchedulePounceTrailTick(player, trail);
    }

    private static void SchedulePounceTrailTick(CCSPlayerController player, PounceTrail trail)
    {
        _ = Task.Delay(TimeSpan.FromSeconds(trail.TickIntervalSeconds)).ContinueWith(_ =>
        {
            Server.NextFrame(() =>
            {
                if (StepPounceTrail(player, trail))
                    return;

                SchedulePounceTrailTick(player, trail);
            });
        });
    }

    private static bool StepPounceTrail(CCSPlayerController player, PounceTrail trail)
    {
        if (trail.Finished)
            return true;

        if (!TryGetPawn(player, out var pawn) || !TryGetTrailPosition(pawn, trail.HeightOffset, out var position))
        {
            FinishTrail(trail, 0.0f);
            return true;
        }

        var elapsedSeconds = (DateTime.UtcNow - trail.StartedAtUtc).TotalSeconds;
        var isOnGround = IsOnGround(pawn);
        if (!isOnGround)
            trail.HasLeftGround = true;

        var shouldStopForLanding = isOnGround
            && elapsedSeconds >= LandingGraceSeconds
            && (trail.HasLeftGround || elapsedSeconds >= 0.35);

        var delta = Subtract(position, trail.LastPosition);
        if (LengthSquared(delta) >= trail.MinSegmentDistance * trail.MinSegmentDistance)
        {
            SpawnTrailSegment(trail.LastPosition, position, trail);
            trail.LastPosition = position;
        }

        if (shouldStopForLanding)
        {
            FinishTrail(trail, trail.FadeAfterLandingSeconds);
            return true;
        }

        if (DateTime.UtcNow >= trail.EndsAtUtc)
        {
            FinishTrail(trail, trail.FadeAfterLandingSeconds);
            return true;
        }

        return false;
    }

    private static void SpawnTrailSegment(Vector startPosition, Vector endPosition, PounceTrail trail)
    {
        SpawnTrailMarker(endPosition, trail);

        if (string.IsNullOrWhiteSpace(trail.BeamMaterial))
            return;

        var beam = Utilities.CreateEntityByName<CBeam>("env_beam");
        if (beam == null || !beam.IsValid)
            return;

        beam.BeamType = BeamType_t.BEAM_POINTS;
        beam.NumBeamEnts = 2;
        beam.ClipStyle = BeamClipStyle_t.kNOCLIP;
        beam.TurnedOff = false;
        beam.Amplitude = 0.0f;
        beam.Speed = 0.0f;
        beam.FrameRate = 0.0f;
        beam.HDRColorScale = 1.0f;
        beam.FadeLength = 0.0f;
        beam.Width = trail.BeamWidth;
        beam.EndWidth = Math.Max(0.15f, trail.BeamWidth * 0.65f);
        beam.RenderMode = RenderMode_t.kRenderTransAlpha;
        beam.RenderFX = RenderFx_t.kRenderFxNone;
        beam.Render = Color.FromArgb(170, 255, 24, 24);
        beam.SetModel(trail.BeamMaterial);
        beam.Teleport(startPosition, null, null);
        beam.DispatchSpawn();
        beam.EndPos.X = endPosition.X;
        beam.EndPos.Y = endPosition.Y;
        beam.EndPos.Z = endPosition.Z;
        MarkBeamStateChanged(beam);
        beam.MarkRenderStateChanged();

        trail.Entities.Add(beam);
        ScheduleEntityRemoval(beam, trail.SegmentLifetimeSeconds);
    }

    private static void MarkBeamStateChanged(CBeam beam)
    {
        Utilities.SetStateChanged(beam, "CBeam", "m_nBeamType");
        Utilities.SetStateChanged(beam, "CBeam", "m_nNumBeamEnts");
        Utilities.SetStateChanged(beam, "CBeam", "m_nClipStyle");
        Utilities.SetStateChanged(beam, "CBeam", "m_bTurnedOff");
        Utilities.SetStateChanged(beam, "CBeam", "m_fAmplitude");
        Utilities.SetStateChanged(beam, "CBeam", "m_fSpeed");
        Utilities.SetStateChanged(beam, "CBeam", "m_flFrameRate");
        Utilities.SetStateChanged(beam, "CBeam", "m_flHDRColorScale");
        Utilities.SetStateChanged(beam, "CBeam", "m_fFadeLength");
        Utilities.SetStateChanged(beam, "CBeam", "m_fWidth");
        Utilities.SetStateChanged(beam, "CBeam", "m_fEndWidth");
        Utilities.SetStateChanged(beam, "CBeam", "m_vecEndPos");
    }

    private static void SpawnTrailMarker(Vector position, PounceTrail trail)
    {
        if (string.IsNullOrWhiteSpace(trail.MarkerParticle))
            return;

        var particle = CreateTrailMarker(trail.MarkerParticle, position, trail.MarkerRadiusScale);
        if (particle == null)
            return;

        trail.Entities.Add(particle);
        ScheduleEntityRemoval(particle, trail.SegmentLifetimeSeconds);
    }

    private static CParticleSystem? CreateTrailMarker(string particleName, Vector position, float radiusScale)
    {
        var particle = Utilities.CreateEntityByName<CEnvParticleGlow>("env_particle_glow");
        if (particle == null || !particle.IsValid)
            return null;

        particle.EffectName = particleName;
        particle.StartActive = true;
        particle.Active = true;
        particle.NoSave = true;
        particle.Tint = TrailColor;
        particle.ColorTint = TrailColor;
        particle.AlphaScale = 0.85f;
        particle.RadiusScale = radiusScale;
        particle.SelfIllumScale = 4.0f;
        particle.Teleport(position, null, null);
        particle.DispatchSpawn();
        particle.AcceptInput("Start");

        return particle;
    }

    private static void FinishTrail(PounceTrail trail, float fadeSeconds)
    {
        if (trail.Finished)
            return;

        trail.Finished = true;
        foreach (var entity in trail.Entities)
            ScheduleEntityRemoval(entity, fadeSeconds);

        trail.Entities.Clear();
    }

    private static void ScheduleEntityRemoval(CEntityInstance? entity, float delaySeconds)
    {
        _ = Task.Delay(TimeSpan.FromSeconds(Math.Max(0.0f, delaySeconds))).ContinueWith(_ =>
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

    private static bool TryGetPawn(CCSPlayerController player, out CCSPlayerPawn pawn)
    {
        pawn = null!;
        if (!player.IsValid || !player.PawnIsAlive)
            return false;

        var currentPawn = player.PlayerPawn.Value;
        if (currentPawn == null || !currentPawn.IsValid)
            return false;

        pawn = currentPawn;
        return true;
    }

    private static bool TryGetTrailPosition(CCSPlayerPawn pawn, float heightOffset, out Vector position)
    {
        position = null!;
        if (pawn.AbsOrigin == null)
            return false;

        var origin = pawn.AbsOrigin;
        position = new Vector(origin.X, origin.Y, origin.Z + heightOffset);
        return true;
    }

    private static bool IsOnGround(CCSPlayerPawn pawn)
    {
        const uint onGroundFlag = 1u;
        return pawn.OnGroundLastTick
            || (pawn.Flags & onGroundFlag) == onGroundFlag
            || pawn.GroundEntity.Value != null;
    }

    private static Vector Subtract(Vector a, Vector b)
    {
        return new Vector(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    }

    private static float LengthSquared(Vector vector)
    {
        return vector.X * vector.X + vector.Y * vector.Y + vector.Z * vector.Z;
    }

    private sealed class PounceTrail
    {
        public required Vector LastPosition { get; set; }
        public required DateTime StartedAtUtc { get; init; }
        public required DateTime EndsAtUtc { get; init; }
        public required float TickIntervalSeconds { get; init; }
        public required float SegmentLifetimeSeconds { get; init; }
        public required float FadeAfterLandingSeconds { get; init; }
        public required float MinSegmentDistance { get; init; }
        public required float HeightOffset { get; init; }
        public required float BeamWidth { get; init; }
        public required string BeamMaterial { get; init; }
        public required string MarkerParticle { get; init; }
        public required float MarkerRadiusScale { get; init; }
        public bool HasLeftGround { get; set; }
        public bool Finished { get; set; }
        public List<CEntityInstance> Entities { get; } = [];
    }
}
