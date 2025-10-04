using CounterStrikeSharp.API.Core;
using ZombieModPlugin.Configs;
using ZombieModPlugin.States;
using ZombieModPlugin.Zombies.Models;

namespace ZombieModPlugin.Abilities;

public class AbilityExecutionContext
{
    public required CCSPlayerController Player { get; init; }
    public required PlayerState PlayerState { get; init; }
    public required BasePlugin Plugin { get; init; }
    public required BaseConfig Config { get; init; }
    public required DateTime ServerTime { get; init; }
    public List<CCSPlayerController> AllPlayers { get; init; } = [];
}


