using ZombieModPlugin.Zombies.Models;
using ZombieModPlugin.Abilities;

namespace ZombieModPlugin.States;

public class PlayerState
{
    public bool IsZombie { get; set; } = false;
    public Zombie? SelectedZombieType { get; set; }

    public Dictionary<string, ZombieProgression> ZombieProgression { get; set; } = new();
    public Dictionary<AbilityType, DateTime> GlobalCooldowns { get; set; } = [];

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
}

public class ZombieProgression
{
    public int Level { get; set; } = 1;
    public int XP { get; set; } = 0;

    public List<AbilityType> UnlockedAbilities { get; set; } = [];
    public HashSet<AbilityType> ActiveAbilities { get; set; } = [];
}
