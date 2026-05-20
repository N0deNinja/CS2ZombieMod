using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using ZombieModPlugin.Extensions;
using ZombieModPlugin.States;

namespace ZombieModPlugin.Abilities.Utils;

public static class AbilityUtils
{
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


    public static void ApplySpeedBoost(CCSPlayerController player, float multiplier, float duration)
    {
        RunTimedEffect(
            player,
            duration,
            apply: pawn => pawn.VelocityModifier *= multiplier,
            revert: pawn => pawn.VelocityModifier /= multiplier
        );
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

    public static void TrackActiveAbilityDuration(
    CCSPlayerController player,
    AbilityType ability,
    float durationSeconds,
    PlayerState state)
    {
        Task.Delay(TimeSpan.FromSeconds(durationSeconds)).ContinueWith(_ =>
        {
            if (player.IsValid)
            {
                state.ActiveAbilities.Remove(ability);
            }
        });
    }


}
