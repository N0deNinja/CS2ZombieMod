using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace ZombieModPlugin.Extensions;

public static class PawnNetworkStateExtensions
{
    public static void MarkHealthStateChanged(this CBaseEntity entity)
    {
        MarkStateChanged(entity, "CBaseEntity", "m_iMaxHealth");
        MarkStateChanged(entity, "CBaseEntity", "m_iHealth");
    }

    public static void MarkMovementStateChanged(this CBaseEntity entity)
    {
        MarkStateChanged(entity, "CCSPlayerPawn", "m_flVelocityModifier");
        MarkStateChanged(entity, "CBaseEntity", "m_flGravityScale");
    }

    public static void MarkRenderStateChanged(this CBaseEntity entity)
    {
        MarkStateChanged(entity, "CBaseModelEntity", "m_clrRender");
    }

    public static void MarkPlayerStatsStateChanged(this CBaseEntity entity)
    {
        entity.MarkHealthStateChanged();
        entity.MarkMovementStateChanged();
        entity.MarkRenderStateChanged();
    }

    private static void MarkStateChanged(CBaseEntity entity, string className, string fieldName)
    {
        if (!entity.IsValid)
            return;

        Utilities.SetStateChanged(entity, className, fieldName, 0);
    }
}
