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
using ZombieModPlugin.Sounds;
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
    private PlayerCommandHandler? _playerCommandHandler;
    private ZombieRoundManager? _roundManager;
    private string _currentMapName = string.Empty;

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
        _playerCommandHandler = new PlayerCommandHandler(_playerStates, Config, this);
        _roundManager = new ZombieRoundManager(_playerStates, Config, zombieHandler, humanHandler);
        _adminTestHandler = new AdminTestHandler(_playerStates, Config, this, zombieHandler, humanHandler, _roundManager);
        _abilityHandler.RegisterCommands();
        _playerCommandHandler.RegisterCommands();
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
            _currentMapName = mapName;
            Console.WriteLine($"[ZombieMod] Map started: {mapName}. Applying Zombie Mod server rules.");
            _roundManager?.OnMapStarted(mapName);
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

        foreach (var resource in Config.SoundConfig.Resources ?? [])
            PrecacheConfiguredResource(manifest, resource);

        var frostBolt = Config.AbilityConfig.FrostBolt;
        PrecacheConfiguredResource(manifest, frostBolt.CastParticle);
        PrecacheConfiguredResource(manifest, frostBolt.ProjectileParticle);
        PrecacheConfiguredResource(manifest, frostBolt.HitParticle);
        PrecacheConfiguredResource(manifest, frostBolt.BeamMaterial);
    }

    private static void PrecacheConfiguredModel(ResourceManifest manifest, string modelPath)
    {
        if (!string.IsNullOrWhiteSpace(modelPath))
            manifest.AddResource(modelPath);
    }

    private static void PrecacheConfiguredResource(ResourceManifest manifest, string resourcePath)
    {
        if (!string.IsNullOrWhiteSpace(resourcePath))
            manifest.AddResource(resourcePath);
    }

    private void ScheduleWorkshopAddonDownloadRetry()
    {
        if (!Config.GeneralConfig.AutoDownloadWorkshopAddons)
            return;

        var addonIds = GetWorkshopAddonIdsForMap(_currentMapName);

        if (addonIds.Length == 0)
            return;

        var addonList = string.Join(",", addonIds);
        Server.ExecuteCommand($"mm_extra_addons \"{addonList}\"");
        Server.ExecuteCommand($"mm_client_extra_addons \"{addonList}\"");
        Server.ExecuteCommand("mm_addon_mount_download 1");
        Server.ExecuteCommand("mm_cache_clients_with_addons 0");

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

    private string[] GetWorkshopAddonIdsForMap(string mapName)
    {
        var orderedIds = new List<string>();
        var mapIds = Config.GeneralConfig.WorkshopMapIds ?? [];
        var mapNames = Config.GeneralConfig.WorkshopMapNames ?? [];
        var mapIndex = Array.FindIndex(
            mapNames,
            name => string.Equals(name?.Trim(), mapName, StringComparison.OrdinalIgnoreCase));

        if (mapIndex >= 0 && mapIndex < mapIds.Length)
            AddWorkshopAddonId(orderedIds, mapIds[mapIndex]);

        foreach (var addonId in Config.GeneralConfig.WorkshopAddonIds ?? [])
            AddWorkshopAddonId(orderedIds, addonId);

        foreach (var addonId in mapIds)
            AddWorkshopAddonId(orderedIds, addonId);

        return orderedIds.ToArray();
    }

    private static void AddWorkshopAddonId(List<string> addonIds, string? addonId)
    {
        if (string.IsNullOrWhiteSpace(addonId))
            return;

        var trimmedAddonId = addonId.Trim();
        if (!addonIds.Contains(trimmedAddonId, StringComparer.Ordinal))
            addonIds.Add(trimmedAddonId);
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
