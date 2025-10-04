using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Utils;
using ZombieModPlugin.Abilities;
using ZombieModPlugin.Abilities.Executors;
using ZombieModPlugin.Configs;
using ZombieModPlugin.Extensions;
using ZombieModPlugin.Handlers;
using ZombieModPlugin.Humans.Handlers;
using ZombieModPlugin.States;
using ZombieModPlugin.Zombies.Handlers;


namespace ZombieModPlugin;

public class ZombieModPlugin : BasePlugin, IPluginConfig<BaseConfig>
{
    public override string ModuleName => "ZombieModPlugin";
    public override string ModuleAuthor => "Seen";
    public override string ModuleVersion => "1.0.0";
    private GeneralHandlers? _generalHandlers;

    public BaseConfig Config { get; set; } = null!;

    public void OnConfigParsed(BaseConfig config)
    {
        Config = config;
    }


    private readonly Dictionary<ulong, PlayerState> _playerStates = [];

    public override void Load(bool hotReload)
    {
        var zombieHandler = new ZombieHandler(_playerStates, Config);
        var humanHandler = new HumanHandler();

        _generalHandlers = new GeneralHandlers(_playerStates, Config, zombieHandler, humanHandler);

        RegisterEventHandler<EventPlayerConnectFull>(_generalHandlers.OnPlayerConnectFullInitState);
        RegisterEventHandler<EventRoundStart>(zombieHandler.OnRoundStartInfectPlayer);
        RegisterEventHandler<EventRoundEnd>((@event, gameEventInfo) =>
        {
            Console.WriteLine("[ZombieMod] Round ended. Resetting player states.");

            foreach (var state in _playerStates.Values)
            {
                state.IsZombie = false;
                state.GlobalCooldowns.Clear();
            }

            return HookResult.Continue;
        });
    }

}
