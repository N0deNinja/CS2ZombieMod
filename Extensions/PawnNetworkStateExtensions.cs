using CounterStrikeSharp.API.Core;

namespace ZombieModPlugin.Extensions;

public static class PawnNetworkStateExtensions
{
    public static void MarkHealthStateChanged(this CBaseEntity entity)
    {
        ReclaimCS.Shared.CounterStrike.PawnNetworkStateExtensions.MarkHealthStateChanged(entity);
    }

    public static void MarkMovementStateChanged(this CBaseEntity entity)
    {
        ReclaimCS.Shared.CounterStrike.PawnNetworkStateExtensions.MarkMovementStateChanged(entity);
    }

    public static void MarkRenderStateChanged(this CBaseEntity entity)
    {
        ReclaimCS.Shared.CounterStrike.PawnNetworkStateExtensions.MarkRenderStateChanged(entity);
    }

    public static void MarkEffectsStateChanged(this CBaseEntity entity)
    {
        ReclaimCS.Shared.CounterStrike.PawnNetworkStateExtensions.MarkEffectsStateChanged(entity);
    }

    public static void MarkEconStateChanged(this CBaseEntity entity)
    {
        ReclaimCS.Shared.CounterStrike.PawnNetworkStateExtensions.MarkEconStateChanged(entity);
    }

    public static void MarkInventoryStateChanged(this CBaseEntity entity)
    {
        ReclaimCS.Shared.CounterStrike.PawnNetworkStateExtensions.MarkInventoryStateChanged(entity);
    }

    public static void MarkMoneyStateChanged(this CBaseEntity entity)
    {
        ReclaimCS.Shared.CounterStrike.PawnNetworkStateExtensions.MarkMoneyStateChanged(entity);
    }

    public static void MarkTeamStateChanged(this CBaseEntity entity)
    {
        ReclaimCS.Shared.CounterStrike.PawnNetworkStateExtensions.MarkTeamStateChanged(entity);
    }

    public static void MarkPlayerStatsStateChanged(this CBaseEntity entity)
    {
        ReclaimCS.Shared.CounterStrike.PawnNetworkStateExtensions.MarkPlayerStatsStateChanged(
            entity,
            includeArmor: false,
            includeRender: true);
    }
}
