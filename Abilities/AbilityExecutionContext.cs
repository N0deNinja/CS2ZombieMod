using CounterStrikeSharp.API.Core;
using ZombieModPlugin.Configs;

namespace ZombieModPlugin.Abilities;

public class AbilityExecutionContext
{
    public CCSPlayerController Player { get; init; }
    public Zombie ZombieType { get; init; }
    public int PlayerLevel { get; init; }
    public BasePlugin Plugin { get; init; }
    public BaseConfig Config { get; init; }
    public DateTime ServerTime { get; init; }

    public List<CCSPlayerController> AllPlayers { get; init; } = [];
    public Action<AbilityType, float> SetCooldown { get; init; }
}


