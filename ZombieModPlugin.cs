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
        var humanHandler = new HumanHandler(Config);
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
        RegisterEventHandler<EventItemPickup>(_roundManager.OnItemPickup, HookMode.Pre);
        RegisterEventHandler<EventPlayerDeath>(_roundManager.OnPlayerDeath);
        RegisterEventHandler<EventRoundEnd>(_roundManager.OnRoundEnd);
        RegisterListener<Listeners.OnServerPrecacheResources>(OnServerPrecacheResources);
        RegisterListener<Listeners.OnPlayerTakeDamagePre>(_roundManager.OnPlayerTakeDamagePre);
        RegisterListener<Listeners.OnTick>(_roundManager.OnTick);
        RegisterListener<Listeners.OnPlayerButtonsChanged>(_roundManager.OnPlayerButtonsChanged);
        RegisterListener<Listeners.OnMapStart>(mapName =>
        {
            Console.WriteLine($"[ZombieMod] Map started: {mapName}. Applying Zombie Mod server rules.");
            ScheduleWorkshopAddonDownloadRetry();
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

    private void OnServerPrecacheResources(ResourceManifest manifest)
    {
        PrecacheConfiguredModel(manifest, Config.ZombieConfig.PlayerModel);
        foreach (var zombieType in Config.ZombieConfig.ZombieTypes)
            PrecacheConfiguredModel(manifest, zombieType.PlayerModel);

        PrecacheConfiguredModel(manifest, Config.HumanConfig.PlayerModel);
        foreach (var humanClass in Config.HumanConfig.HumanClasses)
            PrecacheConfiguredModel(manifest, humanClass.PlayerModel);
    }

    private static void PrecacheConfiguredModel(ResourceManifest manifest, string modelPath)
    {
        if (!string.IsNullOrWhiteSpace(modelPath))
            manifest.AddResource(modelPath);
    }

    private void ScheduleWorkshopAddonDownloadRetry()
    {
        if (!Config.GeneralConfig.AutoDownloadWorkshopAddons)
            return;

        var addonIds = Config.GeneralConfig.WorkshopAddonIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (addonIds.Length == 0)
            return;

        var addonList = string.Join(",", addonIds);
        Server.ExecuteCommand($"mm_extra_addons \"{addonList}\"");
        Server.ExecuteCommand($"mm_client_extra_addons \"{addonList}\"");
        Server.ExecuteCommand("mm_addon_mount_download 1");

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(12));
            foreach (var addonId in addonIds)
            {
                var capturedAddonId = addonId;
                Server.NextFrame(() =>
                {
                    Console.WriteLine($"[ZombieMod] Requesting workshop addon download via MultiAddonManager: {capturedAddonId}");
                    Server.ExecuteCommand($"mm_download_addon {capturedAddonId}");
                });
            }
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
