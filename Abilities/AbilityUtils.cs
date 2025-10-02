using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities;

namespace ZombieModPlugin.Abilities;

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

        Task.Delay(TimeSpan.FromSeconds(durationSeconds)).ContinueWith(_ =>
        {
            if (player.IsValid && player.PlayerPawn?.Value is { IsValid: true } currentPawn)
            {
                revert(currentPawn);
            }
        });
    }
}
