using ZombieModPlugin.Abilities;
using ZombieModPlugin.Zombies.Models;

namespace ZombieModPlugin.States;

public class PlayerState
{
    public bool IsZombie { get; set; } = false;

    public int Level { get; set; } = 1;
    public int XP { get; set; } = 0;

    public Zombie? SelectedZombieType { get; set; }

    public List<AbilityType> UnlockedAbilities { get; set; } = [];
    public Dictionary<AbilityType, DateTime> Cooldowns { get; set; } = [];
    public HashSet<AbilityType> ActiveAbilities { get; set; } = [];
}
