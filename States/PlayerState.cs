using ZombieModPlugin.Zombies.Models;
using ZombieModPlugin.Abilities;
using ZombieModPlugin.Humans.Models;

namespace ZombieModPlugin.States;

public class PlayerState
{
    public bool _isZombie;

    public bool IsZombie
    {
        get => _isZombie;
        set
        {
            if (_isZombie == value)
                return;

            _isZombie = value;
            ResetRoleRuntimeState();
            OnZombieStateChanged?.Invoke(this, value);
        }
    }

    // Event triggered whenever IsZombie changes
    public event Action<PlayerState, bool>? OnZombieStateChanged;

    public Zombie? SelectedZombieType { get; set; }
    public HumanClass? SelectedHumanClass { get; set; }
    public Zombie? PreferredZombieType { get; set; }
    public HumanClass? PreferredHumanClass { get; set; }
    public int InfectionHitsTaken { get; set; }
    public float ZombieKnockbackMultiplier { get; set; } = 1.0f;
    public DateTime KnockbackMultiplierExpiresAtUtc { get; set; } = DateTime.MinValue;
    public int AirJumpsUsed { get; set; }
    public bool AirJumpReady { get; set; }
    public float? LastCloakX { get; set; }
    public float? LastCloakY { get; set; }
    public float? LastCloakZ { get; set; }
    public DateTime? LurkerStationarySinceUtc { get; set; }
    public DateTime LastLurkerCloakCheckUtc { get; set; } = DateTime.MinValue;
    public bool IsLurkerCloaked { get; set; }
    public int LurkerCurrentAlpha { get; set; } = 255;
    public DateTime NextZombiePainSoundAtUtc { get; set; } = DateTime.MinValue;

    public Dictionary<string, ZombieProgression> ZombieProgression { get; set; } = new();
    public Dictionary<string, HumanProgression> HumanProgression { get; set; } = new();
    public Dictionary<AbilityType, DateTime> GlobalCooldowns { get; set; } = [];
    public HashSet<AbilityType> ActiveAbilities { get; set; } = [];

    public bool IsOnCooldown(AbilityType type, out double remainingSeconds)
    {
        if (GlobalCooldowns.TryGetValue(type, out var endTime))
        {
            remainingSeconds = (endTime - DateTime.Now).TotalSeconds;
            if (remainingSeconds > 0)
                return true;
        }

        remainingSeconds = 0;
        return false;
    }

    public void SetCooldown(AbilityType type, float durationSeconds)
    {
        var end = DateTime.Now.AddSeconds(durationSeconds);
        GlobalCooldowns[type] = end;

        Task.Delay(TimeSpan.FromSeconds(durationSeconds)).ContinueWith(_ =>
        {
            GlobalCooldowns.Remove(type);
        });
    }

    public void ResetRoleRuntimeState()
    {
        InfectionHitsTaken = 0;
        ZombieKnockbackMultiplier = 1.0f;
        KnockbackMultiplierExpiresAtUtc = DateTime.MinValue;
        NextZombiePainSoundAtUtc = DateTime.MinValue;
        AirJumpsUsed = 0;
        AirJumpReady = false;
        ResetLurkerCloakTracking();
    }

    public void ResetLurkerCloakTracking()
    {
        LastCloakX = null;
        LastCloakY = null;
        LastCloakZ = null;
        LurkerStationarySinceUtc = null;
        LastLurkerCloakCheckUtc = DateTime.MinValue;
        IsLurkerCloaked = false;
        LurkerCurrentAlpha = 255;
    }
}

public class ZombieProgression
{
    public int Level { get; set; } = 1;
    public int XP { get; set; } = 0;

    public List<AbilityType> UnlockedAbilities { get; set; } = [];
    public HashSet<AbilityType> ActiveAbilities { get; set; } = [];
}

public class HumanProgression
{
    public int Level { get; set; } = 1;
    public int XP { get; set; } = 0;
}
