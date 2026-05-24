using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using ZombieModPlugin.Extensions;
using ZombieModPlugin.States;

namespace ZombieModPlugin.Abilities.Utils;

public static class AbilityUtils
{
    private const float SpeedModifierRefreshIntervalSeconds = 0.1f;
    private const float MinVelocityModifier = 0.05f;
    private const float MaxVelocityModifier = 4.0f;

    /// <summary>
    /// Safely runs a temporary effect on a player with automatic revert after duration.
    /// </summary>
    public static void RunTimedEffect(CCSPlayerController player, float durationSeconds, Action<CCSPlayerPawn> apply, Action<CCSPlayerPawn> revert)
    {
        var pawn = player.PlayerPawn?.Value;
        if (pawn is null || !pawn.IsValid) return;

        apply(pawn);
        pawn.MarkPlayerStatsStateChanged();

        Task.Delay(TimeSpan.FromSeconds(durationSeconds)).ContinueWith(_ =>
        {
            Server.NextFrame(() =>
            {
                if (player.IsValid && player.PlayerPawn?.Value is { IsValid: true } currentPawn)
                {
                    revert(currentPawn);
                    currentPawn.MarkPlayerStatsStateChanged();
                }
            });
        });
    }

    /// <summary>
    /// Runs a periodic effect on a player every interval for a total duration.
    /// </summary>
    public static void RunPeriodicEffect(
        CCSPlayerController player,
        float durationSeconds,
        float intervalSeconds,
        Action<CCSPlayerPawn> onTick)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid) return;

        Task.Run(async () =>
        {
            int ticks = (int)Math.Floor(durationSeconds / intervalSeconds);
            for (int i = 0; i < ticks; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds));

                Server.NextFrame(() =>
                {
                    if (!player.IsValid || player.PlayerPawn.Value is not { IsValid: true } currentPawn)
                        return;

                    onTick(currentPawn);
                    currentPawn.MarkPlayerStatsStateChanged();
                });
            }
        });
    }


    public static void ApplySpeedBoost(CCSPlayerController player, PlayerState state, float multiplier, float duration)
    {
        ApplySpeedModifier(player, state, multiplier, duration);
    }

    public static void ApplySpeedModifier(CCSPlayerController player, PlayerState state, float multiplier, float duration)
    {
        if (!player.IsValid)
            return;

        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid)
            return;

        duration = Math.Max(0.0f, duration);
        if (duration <= 0.0f)
            return;

        multiplier = Math.Clamp(multiplier, MinVelocityModifier, MaxVelocityModifier);
        var effectId = Guid.NewGuid();
        var version = state.TimedSpeedModifierVersion;

        if (state.TimedSpeedModifierMultipliers.Count == 0 || state.TimedSpeedModifierBase == null)
            state.TimedSpeedModifierBase = Math.Clamp(pawn.VelocityModifier, MinVelocityModifier, MaxVelocityModifier);

        state.TimedSpeedModifierMultipliers[effectId] = multiplier;
        ApplyManagedSpeedModifier(player, state, version);
        ScheduleSpeedModifierRefresh(player, state, effectId, DateTime.UtcNow.AddSeconds(duration), version);
        ScheduleSpeedModifierExpiration(player, state, effectId, duration, version);
    }

    public static void ApplySpeedModifier(CCSPlayerController player, float multiplier, float duration)
    {
        var originalVelocityModifier = 1.0f;
        RunTimedEffect(
            player,
            duration,
            apply: pawn =>
            {
                originalVelocityModifier = pawn.VelocityModifier;
                pawn.VelocityModifier = Math.Clamp(
                    originalVelocityModifier * multiplier,
                    MinVelocityModifier,
                    MaxVelocityModifier);
            },
            revert: pawn => pawn.VelocityModifier = originalVelocityModifier
        );
    }

    private static void ScheduleSpeedModifierRefresh(
        CCSPlayerController player,
        PlayerState state,
        Guid effectId,
        DateTime expiresAtUtc,
        int version)
    {
        _ = Task.Delay(TimeSpan.FromSeconds(SpeedModifierRefreshIntervalSeconds)).ContinueWith(_ =>
        {
            Server.NextFrame(() =>
            {
                if (state.TimedSpeedModifierVersion != version
                    || !state.TimedSpeedModifierMultipliers.ContainsKey(effectId)
                    || DateTime.UtcNow >= expiresAtUtc)
                {
                    return;
                }

                ApplyManagedSpeedModifier(player, state, version);
                ScheduleSpeedModifierRefresh(player, state, effectId, expiresAtUtc, version);
            });
        });
    }

    private static void ScheduleSpeedModifierExpiration(
        CCSPlayerController player,
        PlayerState state,
        Guid effectId,
        float duration,
        int version)
    {
        _ = Task.Delay(TimeSpan.FromSeconds(duration)).ContinueWith(_ =>
        {
            Server.NextFrame(() =>
            {
                if (state.TimedSpeedModifierVersion != version)
                    return;

                if (!state.TimedSpeedModifierMultipliers.Remove(effectId))
                    return;

                ApplyManagedSpeedModifier(player, state, version);
            });
        });
    }

    private static void ApplyManagedSpeedModifier(CCSPlayerController player, PlayerState state, int version)
    {
        if (state.TimedSpeedModifierVersion != version)
            return;

        if (!player.IsValid)
            return;

        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid)
            return;

        var baseModifier = Math.Clamp(
            state.TimedSpeedModifierBase ?? pawn.VelocityModifier,
            MinVelocityModifier,
            MaxVelocityModifier);

        if (state.TimedSpeedModifierMultipliers.Count == 0)
        {
            pawn.VelocityModifier = baseModifier;
            state.TimedSpeedModifierBase = null;
            pawn.MarkMovementStateChanged();
            return;
        }

        var combinedMultiplier = 1.0f;
        foreach (var activeMultiplier in state.TimedSpeedModifierMultipliers.Values)
            combinedMultiplier *= activeMultiplier;

        pawn.VelocityModifier = Math.Clamp(
            baseModifier * combinedMultiplier,
            MinVelocityModifier,
            MaxVelocityModifier);
        pawn.MarkMovementStateChanged();
    }

    public static void ApplyHealthRegen(CCSPlayerController player, int healPerTick, float duration, float interval, int maxHealth)
    {
        RunPeriodicEffect(
            player,
            duration,
            interval,
            onTick: pawn =>
            {
                if (pawn.Health < maxHealth)
                {
                    pawn.Health = Math.Min(pawn.Health + healPerTick, maxHealth);
                    pawn.MarkHealthStateChanged();
                }
            }
        );
    }

    public static void ApplyKnockbackDebuff(PlayerState state, float multiplier, float durationSeconds)
    {
        var expiresAt = DateTime.UtcNow.AddSeconds(durationSeconds);
        state.ZombieKnockbackMultiplier = Math.Min(state.ZombieKnockbackMultiplier, multiplier);
        state.KnockbackMultiplierExpiresAtUtc = expiresAt > state.KnockbackMultiplierExpiresAtUtc
            ? expiresAt
            : state.KnockbackMultiplierExpiresAtUtc;

        Task.Delay(TimeSpan.FromSeconds(durationSeconds)).ContinueWith(_ =>
        {
            if (DateTime.UtcNow >= state.KnockbackMultiplierExpiresAtUtc)
            {
                state.ZombieKnockbackMultiplier = 1.0f;
                state.KnockbackMultiplierExpiresAtUtc = DateTime.MinValue;
            }
        });
    }

    public static void TrackActiveAbilityDuration(
    CCSPlayerController player,
    AbilityType ability,
    float durationSeconds,
    PlayerState state)
    {
        state.ActiveAbilities.Add(ability);

        if (durationSeconds <= 0.0f)
        {
            state.ActiveAbilities.Remove(ability);
            return;
        }

        Task.Delay(TimeSpan.FromSeconds(durationSeconds)).ContinueWith(_ =>
        {
            Server.NextFrame(() =>
            {
                if (player.IsValid)
                    state.ActiveAbilities.Remove(ability);
            });
        });
    }


}
