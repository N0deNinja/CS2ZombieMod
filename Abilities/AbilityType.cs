namespace ZombieModPlugin.Models;

public enum AbilityType
{
    Pounce,               // Jump forward rapidly (leap attack)
    HealthRegen,          // Gradual healing over time
    DamageResistance,     // Reduced damage for a duration
    Roar,                 // Screen shake + fear effect (stun or slow nearby enemies)
    Invisibility,         // Makes player invisible (render off)
    SpeedBoost,           // Temporary movement speed increase
    ToxicAura,            // Damages nearby players over time (AOE)
    Berserk,              // +Damage, -Defense boost for short time
    BlindSpit,            // Spits goo to blind (flash effect) in front cone
    SelfDestruct,         // Explode on death, damaging nearby enemies
    WallClimb,            // Temporarily lets zombie jump higher or climb surfaces
    SummonMinions,        // Spawns weak zombie bots to assist
    Silence,              // Prevents enemies from using weapons for X seconds (simulate with weapon strip + restore)
    EMPPulse,             // Disables enemy HUD/crosshair (simulate with overlays)
    VenomBite,            // Poison effect over time (DOT)
    ShadowStep,           // Short teleport forward (based on aim direction)
}
