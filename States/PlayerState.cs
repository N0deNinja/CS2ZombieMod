using System.Collections.Generic;
using ZombieModPlugin.Abilities;
using ZombieModPlugin.Configs;

namespace ZombieModPlugin.States;

public class PlayerState
{
    public bool IsZombie { get; set; } = false;

    public int Level { get; set; } = 1;
    public int XP { get; set; } = 0;

    public Zombie? SelectedZombieType { get; set; }

    public List<AbilityType> UnlockedAbilities { get; set; } = new();
    public Dictionary<AbilityType, DateTime> Cooldowns { get; set; } = new();
    public HashSet<AbilityType> ActiveAbilities { get; set; } = new();
}
