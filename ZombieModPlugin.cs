using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Utils;
using ReclaimCS.Shared.Administration;
using ReclaimCS.Shared.CounterStrike;
using ReclaimCS.Shared.Menus;
using ReclaimCS.Shared.PlayerModels;
using ZombieModPlugin.Abilities;
using ZombieModPlugin.Abilities.Managers;
using ZombieModPlugin.Blockades;
using ZombieModPlugin.Configs;
using ZombieModPlugin.Extensions;
using ZombieModPlugin.Handlers;
using ZombieModPlugin.Humans.Handlers;
using ZombieModPlugin.Progression.Menus;
using ZombieModPlugin.Progression.Persistence;
using ZombieModPlugin.Progression.Services;
using ZombieModPlugin.Rounds;
using ZombieModPlugin.Shops;
using ZombieModPlugin.Sounds;
using ZombieModPlugin.States;
using ZombieModPlugin.Zombies.Handlers;
using ZombieModPlugin.Zombies.Services;


namespace ZombieModPlugin;

public class ZombieModPlugin : BasePlugin, IPluginConfig<BaseConfig>
{
    public override string ModuleName => "ZombieModPlugin";
    public override string ModuleAuthor => "Seen";
    public override string ModuleVersion => "1.0.0";
    private GeneralHandlers? _generalHandlers;
    private AbilityHandler? _abilityHandler;
    private AdminTestHandler? _adminTestHandler;
    private ProgressionCommandHandler? _progressionCommandHandler;
    private HumanShopCommandHandler? _humanShopCommandHandler;
    private BlockadeService? _blockadeService;
    private ZombieRoundManager? _roundManager;
    private ProgressionService? _progressionService;
    private PlayerNameTagService? _playerNameTagService;
    private CenterHtmlHintFlashFixService? _centerHtmlHintFlashFixService;
    private string _currentMapName = string.Empty;

    public BaseConfig Config { get; set; } = null!;

    public void OnConfigParsed(BaseConfig config)
    {
        Config = config;
    }


    private readonly Dictionary<ulong, PlayerState> _playerStates = [];

    public override void Load(bool hotReload)
    {
        var progressionRepository = new SqlitePlayerProgressionRepository(ResolveDatabasePath(Config.ProgressionConfig.Database.FilePath));
        var progressionLevelService = new ProgressionLevelService();
        var progressionUnlockService = new ProgressionUnlockService(Config);
        var progressionService = new ProgressionService(
            _playerStates,
            Config,
            progressionRepository,
            progressionLevelService,
            progressionUnlockService);
        _progressionService = progressionService;
        progressionService.InitializeAsync().GetAwaiter().GetResult();

        var zombieMeleeVisualService = new ZombieMeleeVisualService(Config);
        var zombieHandler = new ZombieHandler(_playerStates, Config, zombieMeleeVisualService, progressionService);
        var humanHandler = new HumanHandler(Config, progressionService);
        var abilityManager = new AbilityManager(progressionService);
        var progressionMenuRenderer = new ProgressionMenuRenderer(Config, progressionService);
        var humanWeaponShopService = new HumanWeaponShopService(Config, progressionService);
        var blockadeService = new BlockadeService(
            _playerStates,
            Config,
            this,
            progressionService,
            () => _roundManager?.IsBlockadePlacementAllowed == true);
        _blockadeService = blockadeService;

        _generalHandlers = new GeneralHandlers(_playerStates, Config, progressionService);
        _abilityHandler = new AbilityHandler(_playerStates, Config, this, abilityManager, progressionService);
        _progressionCommandHandler = new ProgressionCommandHandler(_playerStates, Config, this, progressionService, progressionMenuRenderer);
        _humanShopCommandHandler = new HumanShopCommandHandler(_playerStates, Config, this, progressionService, humanWeaponShopService);
        _roundManager = new ZombieRoundManager(_playerStates, Config, zombieHandler, humanHandler, zombieMeleeVisualService, progressionService, blockadeService);
        _adminTestHandler = new AdminTestHandler(_playerStates, Config, this, zombieHandler, humanHandler, _roundManager, progressionService);
        _playerNameTagService = new PlayerNameTagService(() => Config.Admin);
        _centerHtmlHintFlashFixService = new CenterHtmlHintFlashFixService();
        _abilityHandler.RegisterCommands();
        _progressionCommandHandler.RegisterCommands();
        _humanShopCommandHandler.RegisterCommands();
        blockadeService.RegisterCommands();
        _adminTestHandler.RegisterCommands();

        RegisterEventHandler<EventPlayerConnectFull>(_generalHandlers.OnPlayerConnectFullInitState);
        RegisterEventHandler<EventPlayerConnectFull>((@event, gameEventInfo) =>
        {
            if (@event.Userid != null)
            {
                _roundManager.OnPlayablePlayerConnected(@event.Userid);
                Server.NextFrame(() => _playerNameTagService?.Apply(@event.Userid));
            }

            return HookResult.Continue;
        });
        RegisterEventHandler<EventRoundStart>(_roundManager.OnRoundStart);
        RegisterEventHandler<EventPlayerSpawned>(_roundManager.OnPlayerSpawned);
        RegisterEventHandler<EventItemPickup>(_roundManager.OnItemPickup, HookMode.Pre);
        RegisterEventHandler<EventWeaponFire>(_roundManager.OnWeaponFire);
        RegisterEventHandler<EventPlayerDeath>(_roundManager.OnPlayerDeath);
        RegisterEventHandler<EventRoundEnd>(_roundManager.OnRoundEnd);
        RegisterListener<Listeners.OnServerPrecacheResources>(OnServerPrecacheResources);
        RegisterListener<Listeners.OnPlayerTakeDamagePre>(_roundManager.OnPlayerTakeDamagePre);
        RegisterListener<Listeners.OnTick>(_roundManager.OnTick);
        RegisterListener<Listeners.OnTick>(OnTick);
        RegisterListener<Listeners.OnPlayerButtonsChanged>(_roundManager.OnPlayerButtonsChanged);
        RegisterListener<Listeners.OnMapStart>(mapName =>
        {
            _currentMapName = mapName;
            Console.WriteLine($"[ZombieMod] Map started: {mapName}. Applying Zombie Mod server rules.");
            _centerHtmlHintFlashFixService?.OnMapStart();
            _roundManager?.OnMapStarted(mapName);
            ScheduleWorkshopAddonDownloadRetry();
            Server.NextFrame(ApplyPlayerNameTags);
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

            foreach (var player in Utilities.GetPlayers().Where(player => player is { IsValid: true }))
            {
                var state = player.GetState(_playerStates);
                if (!state.ProgressionLoaded)
                    progressionService.BeginLoadPlayer(player, state);
            }

            ApplyPlayerNameTags();
        });

        RegisterTaggedChatCommandListeners();
    }

    public override void Unload(bool hotReload)
    {
        _blockadeService?.ClearAll();
        _progressionService?.SaveAllConnectedPlayers();
    }

    private void OnTick()
    {
        _centerHtmlHintFlashFixService?.Update();
        ApplyPlayerNameTags();
    }

    private void ApplyPlayerNameTags()
    {
        if (_playerNameTagService == null)
            return;

        foreach (var player in Utilities.GetPlayers().Where(player => player.IsRealConnectedPlayer(Config.GeneralConfig.IncludeBotsInRound)))
            _playerNameTagService.Apply(player);
    }

    private void RegisterTaggedChatCommandListeners()
    {
        AddCommandListener("say", OnTaggedSayCommand, HookMode.Pre);
        AddCommandListener("say_team", OnTaggedSayTeamCommand, HookMode.Pre);
    }

    private HookResult OnTaggedSayCommand(CCSPlayerController? player, CommandInfo command)
    {
        return HandleTaggedChatCommand(player, command, teamChat: false);
    }

    private HookResult OnTaggedSayTeamCommand(CCSPlayerController? player, CommandInfo command)
    {
        return HandleTaggedChatCommand(player, command, teamChat: true);
    }

    private HookResult HandleTaggedChatCommand(CCSPlayerController? player, CommandInfo command, bool teamChat)
    {
        if (_playerNameTagService == null || !player.IsRealConnectedPlayer(Config.GeneralConfig.IncludeBotsInRound))
            return HookResult.Continue;

        var message = GetSayMessage(command);
        if (string.IsNullOrWhiteSpace(message) || IsChatCommand(message) || !_playerNameTagService.HasManagedTag(player!))
            return HookResult.Continue;

        _playerNameTagService.PrintTaggedChatMessage(player!, message, teamChat, Config.GeneralConfig.IncludeBotsInRound);
        return HookResult.Stop;
    }

    private static string GetSayMessage(CommandInfo command)
    {
        if (command.ArgCount < 2)
            return "";

        var message = command.GetArg(1).Trim();
        return message.Length >= 2 && message[0] == '"' && message[^1] == '"'
            ? message[1..^1].Trim()
            : message;
    }

    private static bool IsChatCommand(string message)
    {
        return message.StartsWith('!') || message.StartsWith('/');
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

        PrecacheConfiguredResource(manifest, "soundevents/soundevents_zr_extra.vsndevts");
        PrecacheConfiguredResource(manifest, "sounds/inf_begun.vsnd");
        PrecacheConfiguredResource(manifest, "sounds/inf_starts_14.vsnd");
        PrecacheConfiguredResource(manifest, "sounds/prepare_for_infection.vsnd");
        PrecacheConfiguredResource(manifest, "sounds/siren_14s.vsnd");

        if (Config.ZombieMeleeVisualConfig.EnableZombieKnifeReplacementModel)
            PrecacheConfiguredModel(manifest, Config.ZombieMeleeVisualConfig.ZombieKnifeReplacementModelPath);

        foreach (var resource in Config.ZombieMeleeVisualConfig.ZombieClawSoundResources ?? [])
            PrecacheConfiguredResource(manifest, resource);

        PrecacheConfiguredModel(manifest, Config.BlockadeConfig.MainModel);
        PrecacheConfiguredModel(manifest, Config.BlockadeConfig.SmallModel);

        var frostBolt = Config.AbilityConfig.FrostBolt;
        PrecacheConfiguredResource(manifest, frostBolt.CastParticle);
        PrecacheConfiguredResource(manifest, frostBolt.ProjectileParticle);
        PrecacheConfiguredResource(manifest, frostBolt.HitParticle);
        PrecacheConfiguredResource(manifest, frostBolt.BeamMaterial);

        var pounce = Config.AbilityConfig.Pounce;
        PrecacheConfiguredResource(manifest, pounce.TrailBeamMaterial);
        PrecacheConfiguredResource(manifest, pounce.TrailMarkerParticle);
    }

    private static void PrecacheConfiguredModel(ResourceManifest manifest, string modelPath)
    {
        if (ReclaimPlayerModels.TryFind(modelPath, out var model))
        {
            manifest.AddPlayerModelResources(model);
            return;
        }

        manifest.AddPlayerModelResource(modelPath);
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
        foreach (var addonId in Config.GeneralConfig.WorkshopAddonIds ?? [])
            AddWorkshopAddonId(orderedIds, addonId);

        AddWorkshopAddonId(orderedIds, ReclaimPlayerModels.ReclaimCharactersWorkshopAddonId);

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

    private string ResolveDatabasePath(string configuredPath)
    {
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? "data/zombiemod_progression.db"
            : configuredPath.Trim();

        if (Path.IsPathRooted(path))
            return path;

        var moduleDirectory = Path.GetDirectoryName(ModulePath);
        if (string.IsNullOrWhiteSpace(moduleDirectory))
            moduleDirectory = AppContext.BaseDirectory;

        return Path.Combine(moduleDirectory, path);
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
