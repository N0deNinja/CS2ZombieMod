using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Utils;
using ZombieModPlugin.Abilities;
using ZombieModPlugin.Abilities.Executors;
using ZombieModPlugin.Extensions;
using ZombieModPlugin.States;
using ZombieModPlugin.Zombies.Models;

namespace ZombieModPlugin;

public class ZombieModPlugin : BasePlugin
{
    public override string ModuleName => "ZombieModPlugin";
    public override string ModuleAuthor => "Seen";
    public override string ModuleVersion => "1.0.0";



    private readonly Dictionary<ulong, PlayerState> _playerStates = [];

    public override void Load(bool hotReload)
    {

    }

}
