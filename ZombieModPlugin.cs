using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Utils;
using ZombieModPlugin.Abilities;
using ZombieModPlugin.Abilities.Managers;
using ZombieModPlugin.Configs;
using ZombieModPlugin.Extensions;
using ZombieModPlugin.Handlers;
using ZombieModPlugin.Humans.Handlers;
using ZombieModPlugin.Rounds;
using ZombieModPlugin.States;
using ZombieModPlugin.Zombies.Handlers;


namespace ZombieModPlugin;

public class ZombieModPlugin : BasePlugin, IPluginConfig<BaseConfig>
{
    public override string ModuleName => "ZombieModPlugin";
    public override string ModuleAuthor => "Seen";
    public override string ModuleVersion => "1.0.0";
    private GeneralHandlers? _generalHandlers;
    private AbilityHandler? _abilityHandler;
    private AdminTestHandler? _adminTestHandler;
    private ZombieRoundManager? _roundManager;

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
        var abilityManager = new AbilityManager();

        _generalHandlers = new GeneralHandlers(_playerStates, Config);
        _abilityHandler = new AbilityHandler(_playerStates, Config, this, abilityManager);
        _roundManager = new ZombieRoundManager(_playerStates, Config, zombieHandler, humanHandler);
        _adminTestHandler = new AdminTestHandler(_playerStates, Config, this, zombieHandler, humanHandler, _roundManager);
        _abilityHandler.RegisterCommands();
        _adminTestHandler.RegisterCommands();

        RegisterEventHandler<EventPlayerConnectFull>(_generalHandlers.OnPlayerConnectFullInitState);
        RegisterEventHandler<EventPlayerConnectFull>((@event, gameEventInfo) =>
        {
            if (@event.Userid != null)
                _roundManager.OnPlayablePlayerConnected(@event.Userid);

            return HookResult.Continue;
        });
        RegisterEventHandler<EventRoundStart>(_roundManager.OnRoundStart);
        RegisterEventHandler<EventPlayerSpawned>(_roundManager.OnPlayerSpawned);
        RegisterEventHandler<EventPlayerDeath>(_roundManager.OnPlayerDeath);
        RegisterEventHandler<EventRoundEnd>(_roundManager.OnRoundEnd);
        RegisterListener<Listeners.OnPlayerTakeDamagePre>(_roundManager.OnPlayerTakeDamagePre);
        RegisterListener<Listeners.OnMapStart>(mapName =>
        {
            Console.WriteLine($"[ZombieMod] Map started: {mapName}. Applying Zombie Mod server rules.");
            Server.NextFrame(() =>
            {
                _roundManager?.ApplyZombieServerRules();
                _roundManager?.EnsureRoundLifecycleRunning();
            });
        });

        Server.NextFrame(() =>
        {
            _roundManager.ApplyZombieServerRules();
            _roundManager.EnsureRoundLifecycleRunning();
        });
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        Server.NextFrame(() =>
        {
            _roundManager?.ApplyZombieServerRules();
            _roundManager?.EnsureRoundLifecycleRunning();
        });
    }
}
