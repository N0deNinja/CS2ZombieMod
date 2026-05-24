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
        MarkStateChanged(entity, "CBaseModelEntity", "m_nRenderMode");
        MarkStateChanged(entity, "CBaseModelEntity", "m_nRenderFX");
        MarkEffectsStateChanged(entity);
    }

    public static void MarkEffectsStateChanged(this CBaseEntity entity)
    {
        MarkStateChanged(entity, "CBaseEntity", "m_fEffects");
    }

    public static void MarkEconStateChanged(this CBaseEntity entity)
    {
        TryMarkStateChanged(entity, "CEconEntity", "m_AttributeManager");
        TryMarkStateChanged(entity, "CAttributeContainer", "m_Item");
        TryMarkStateChanged(entity, "CEconItemView", "m_iItemDefinitionIndex");
        TryMarkStateChanged(entity, "CEconItemView", "m_iItemIDHigh");
        TryMarkStateChanged(entity, "CEconItemView", "m_iItemIDLow");
        TryMarkStateChanged(entity, "CEconItemView", "m_bInitialized");
    }

    public static void MarkInventoryStateChanged(this CBaseEntity entity)
    {
        TryMarkStateChanged(entity, "CCSPlayerController", "m_pInventoryServices");
        TryMarkStateChanged(entity, "CCSPlayerController_InventoryServices", "m_vecServerAuthoritativeWeaponSlots");
    }

    public static void MarkMoneyStateChanged(this CBaseEntity entity)
    {
        TryMarkStateChanged(entity, "CCSPlayerController", "m_pInGameMoneyServices");
        TryMarkStateChanged(entity, "CCSPlayerController_InGameMoneyServices", "m_iAccount");
        TryMarkStateChanged(entity, "CCSPlayerController_InGameMoneyServices", "m_iStartAccount");
    }

    public static void MarkTeamStateChanged(this CBaseEntity entity)
    {
        MarkStateChanged(entity, "CBaseEntity", "m_iTeamNum");
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

    private static void TryMarkStateChanged(CBaseEntity entity, string className, string fieldName)
    {
        try
        {
            MarkStateChanged(entity, className, fieldName);
        }
        catch
        {
        }
    }
}
