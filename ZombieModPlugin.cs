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
using ZombieModPlugin.Diagnostics;
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
    private DateTime _loadedAtUtc;
    private DateTime _detailedTickLogUntilUtc;
    private DateTime _nextStartupTickBreadcrumbAtUtc;
    private int _tickCount;
    private int _startupTickBreadcrumbs;

    public BaseConfig Config { get; set; } = null!;

    public void OnConfigParsed(BaseConfig config)
    {
        Config = config;
    }


    private readonly Dictionary<ulong, PlayerState> _playerStates = [];

    public override void Load(bool hotReload)
    {
        CrashBreadcrumbs.Configure(ModulePath);
        CrashBreadcrumbs.SessionStart(ModuleVersion, hotReload);
        _loadedAtUtc = DateTime.UtcNow;
        _detailedTickLogUntilUtc = _loadedAtUtc.AddSeconds(20);
        CrashBreadcrumbs.Log("Load start");

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
        CrashBreadcrumbs.Log("services constructed");

        _abilityHandler.RegisterCommands();
        CrashBreadcrumbs.Log("commands registered: ability");
        _progressionCommandHandler.RegisterCommands();
        CrashBreadcrumbs.Log("commands registered: progression");
        _humanShopCommandHandler.RegisterCommands();
        CrashBreadcrumbs.Log("commands registered: human shop");
        blockadeService.RegisterCommands();
        CrashBreadcrumbs.Log("commands registered: blockade");
        _adminTestHandler.RegisterCommands();

        CrashBreadcrumbs.Log("commands registered: admin");

        RegisterEventHandler<EventPlayerConnectFull>((@event, gameEventInfo) =>
            RunEvent("EventPlayerConnectFull.init-state", @event.Userid, () =>
                _generalHandlers.OnPlayerConnectFullInitState(@event, gameEventInfo)));
        RegisterEventHandler<EventPlayerConnectFull>((@event, gameEventInfo) =>
        {
            var player = @event.Userid;
            return RunEvent("EventPlayerConnectFull.round-and-tags", player, () =>
            {
                _detailedTickLogUntilUtc = DateTime.UtcNow.AddSeconds(12);

                if (player != null)
                {
                    CrashBreadcrumbs.Log($"connect-full round manager start {CrashBreadcrumbs.DescribePlayer(player)}");
                    _roundManager.OnPlayablePlayerConnected(player);
                    CrashBreadcrumbs.Log($"connect-full round manager end {CrashBreadcrumbs.DescribePlayer(player)}");

                    CrashBreadcrumbs.SafeNextFrame("connect-full name tag apply", () => ApplyPlayerNameTag(player, "connect-full", detailed: true));
                }

                return HookResult.Continue;
            });
        });
        RegisterEventHandler<EventRoundStart>((@event, gameEventInfo) =>
            RunEvent("EventRoundStart", null, () => _roundManager.OnRoundStart(@event, gameEventInfo)));
        RegisterEventHandler<EventPlayerSpawned>((@event, gameEventInfo) =>
            RunEvent("EventPlayerSpawned", @event.Userid, () => _roundManager.OnPlayerSpawned(@event, gameEventInfo)));
        RegisterEventHandler<EventItemPickup>((@event, gameEventInfo) =>
            RunEvent("EventItemPickup", @event.Userid, () => _roundManager.OnItemPickup(@event, gameEventInfo)), HookMode.Pre);
        RegisterEventHandler<EventWeaponFire>((@event, gameEventInfo) =>
            RunEvent("EventWeaponFire", @event.Userid, () => _roundManager.OnWeaponFire(@event, gameEventInfo)));
        RegisterEventHandler<EventPlayerDeath>((@event, gameEventInfo) =>
            RunEvent("EventPlayerDeath", @event.Userid, () => _roundManager.OnPlayerDeath(@event, gameEventInfo)));
        RegisterEventHandler<EventRoundEnd>((@event, gameEventInfo) =>
            RunEvent("EventRoundEnd", null, () => _roundManager.OnRoundEnd(@event, gameEventInfo)));
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
            RunAction("OnServerPrecacheResources", () => OnServerPrecacheResources(manifest)));
        RegisterListener<Listeners.OnPlayerTakeDamagePre>((victimPawn, damageInfo) =>
            RunEvent("OnPlayerTakeDamagePre", null, () => _roundManager.OnPlayerTakeDamagePre(victimPawn, damageInfo)));
        RegisterListener<Listeners.OnTick>(() =>
            RunAction("RoundManager.OnTick", _roundManager.OnTick, logStartAndEnd: ShouldLogTickBreadcrumb()));
        RegisterListener<Listeners.OnTick>(OnTick);
        RegisterListener<Listeners.OnPlayerButtonsChanged>((player, pressed, released) =>
            RunAction(
                $"OnPlayerButtonsChanged pressed={pressed} released={released} {CrashBreadcrumbs.DescribePlayer(player)}",
                () => _roundManager.OnPlayerButtonsChanged(player, pressed, released),
                logStartAndEnd: true));
        RegisterListener<Listeners.OnMapStart>(mapName =>
        {
            RunAction($"OnMapStart map={mapName}", () =>
            {
                _currentMapName = mapName;
                _detailedTickLogUntilUtc = DateTime.UtcNow.AddSeconds(20);
                Console.WriteLine($"[ZombieMod] Map started: {mapName}. Applying Zombie Mod server rules.");
                CrashBreadcrumbs.Log($"center hint OnMapStart start map={mapName}");
                _centerHtmlHintFlashFixService?.OnMapStart();
                CrashBreadcrumbs.Log($"center hint OnMapStart end map={mapName}");
                _roundManager?.OnMapStarted(mapName);
                ScheduleWorkshopAddonDownloadRetry();
                CrashBreadcrumbs.SafeNextFrame("map-start apply player name tags", () => ApplyPlayerNameTags("map-start", detailed: true));
                CrashBreadcrumbs.SafeNextFrame("map-start server rules", () =>
                {
                    _roundManager?.ApplyZombieServerRules();
                    _roundManager?.EnsureRoundLifecycleRunning();
                });
            });
        });

        CrashBreadcrumbs.SafeNextFrame("load startup server rules", () =>
        {
            _roundManager.ApplyZombieServerRules();
            _roundManager.EnsureRoundLifecycleRunning();

            foreach (var player in Utilities.GetPlayers().Where(player => player is { IsValid: true }))
            {
                var state = player.GetState(_playerStates);
                if (!state.ProgressionLoaded)
                    progressionService.BeginLoadPlayer(player, state);
            }

            ApplyPlayerNameTags("load-startup", detailed: true);
        });

        RegisterTaggedChatCommandListeners();
        CrashBreadcrumbs.Log("tagged chat command listeners registered");
        CrashBreadcrumbs.Log("Load end");
    }

    public override void Unload(bool hotReload)
    {
        CrashBreadcrumbs.Log($"Unload start hotReload={hotReload}");
        _blockadeService?.ClearAll();
        _progressionService?.SaveAllConnectedPlayers();
        CrashBreadcrumbs.Log($"Unload end hotReload={hotReload}");
    }

    private void OnTick()
    {
        var logTick = ShouldLogTickBreadcrumb();
        if (logTick)
            CrashBreadcrumbs.Log($"OnTick start tick={_tickCount} map={_currentMapName}");

        try
        {
            if (logTick)
                CrashBreadcrumbs.Log("center hint Update start");

            _centerHtmlHintFlashFixService?.Update();

            if (logTick)
                CrashBreadcrumbs.Log("center hint Update end");
        }
        catch (Exception ex)
        {
            CrashBreadcrumbs.LogException("center hint Update", ex);
        }

        ApplyPlayerNameTags("tick", logTick);

        if (logTick)
            CrashBreadcrumbs.Log($"OnTick end tick={_tickCount} map={_currentMapName}");
    }

    private void ApplyPlayerNameTags(string reason, bool detailed)
    {
        if (_playerNameTagService == null)
            return;

        if (detailed)
            CrashBreadcrumbs.Log($"name tag batch start reason={reason}");

        foreach (var player in Utilities.GetPlayers().Where(player => player.IsRealConnectedPlayer(Config.GeneralConfig.IncludeBotsInRound)))
            ApplyPlayerNameTag(player, reason, detailed);

        if (detailed)
            CrashBreadcrumbs.Log($"name tag batch end reason={reason}");
    }

    private void ApplyPlayerNameTag(CCSPlayerController player, string reason, bool detailed)
    {
        if (_playerNameTagService == null)
            return;

        if (detailed)
            CrashBreadcrumbs.Log($"name tag apply start reason={reason} {CrashBreadcrumbs.DescribePlayer(player)}");

        try
        {
            _playerNameTagService.Apply(player);
            if (detailed)
                CrashBreadcrumbs.Log($"name tag apply end reason={reason} {CrashBreadcrumbs.DescribePlayer(player)}");
        }
        catch (Exception ex)
        {
            CrashBreadcrumbs.LogException($"name tag apply reason={reason} {CrashBreadcrumbs.DescribePlayer(player)}", ex);
        }
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
        CrashBreadcrumbs.Log($"MultiAddonManager schedule start enabled={Config.GeneralConfig.AutoDownloadWorkshopAddons} map={_currentMapName}");
        if (!Config.GeneralConfig.AutoDownloadWorkshopAddons)
            return;

        var addonIds = GetWorkshopAddonIdsForMap(_currentMapName);

        if (addonIds.Length == 0)
        {
            CrashBreadcrumbs.Log($"MultiAddonManager schedule skipped no addons map={_currentMapName}");
            return;
        }

        var addonList = string.Join(",", addonIds);
        CrashBreadcrumbs.Log($"MultiAddonManager command start mm_extra_addons addons={addonList}");
        Server.ExecuteCommand($"mm_extra_addons \"{addonList}\"");
        CrashBreadcrumbs.Log($"MultiAddonManager command end mm_extra_addons addons={addonList}");
        CrashBreadcrumbs.Log($"MultiAddonManager command start mm_client_extra_addons addons={addonList}");
        Server.ExecuteCommand($"mm_client_extra_addons \"{addonList}\"");
        CrashBreadcrumbs.Log($"MultiAddonManager command end mm_client_extra_addons addons={addonList}");
        CrashBreadcrumbs.Log("MultiAddonManager command start mm_addon_mount_download 1");
        Server.ExecuteCommand("mm_addon_mount_download 1");
        CrashBreadcrumbs.Log("MultiAddonManager command end mm_addon_mount_download 1");
        CrashBreadcrumbs.Log("MultiAddonManager command start mm_cache_clients_with_addons 0");
        Server.ExecuteCommand("mm_cache_clients_with_addons 0");
        CrashBreadcrumbs.Log("MultiAddonManager command end mm_cache_clients_with_addons 0");

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(12));
                foreach (var addonId in addonIds)
                {
                    var capturedAddonId = addonId;
                    CrashBreadcrumbs.SafeNextFrame($"MultiAddonManager mm_download_addon addon={capturedAddonId}", () =>
                    {
                        Console.WriteLine($"[ZombieMod] Requesting workshop addon download via MultiAddonManager: {capturedAddonId}");
                        Server.ExecuteCommand($"mm_download_addon {capturedAddonId}");
                    });
                }
            }
            catch (Exception ex)
            {
                CrashBreadcrumbs.LogException("MultiAddonManager retry task", ex);
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
        CrashBreadcrumbs.Log($"OnAllPluginsLoaded hotReload={hotReload}");
        CrashBreadcrumbs.SafeNextFrame("OnAllPluginsLoaded server rules", () =>
        {
            _roundManager?.ApplyZombieServerRules();
            _roundManager?.EnsureRoundLifecycleRunning();
        });
    }

    private HookResult RunEvent(string label, CCSPlayerController? player, Func<HookResult> handler)
    {
        CrashBreadcrumbs.Log($"{label} start {CrashBreadcrumbs.DescribePlayer(player)}");
        try
        {
            var result = handler();
            CrashBreadcrumbs.Log($"{label} end result={result} {CrashBreadcrumbs.DescribePlayer(player)}");
            return result;
        }
        catch (Exception ex)
        {
            CrashBreadcrumbs.LogException($"{label} {CrashBreadcrumbs.DescribePlayer(player)}", ex);
            return HookResult.Continue;
        }
    }

    private void RunAction(string label, Action action, bool logStartAndEnd = true)
    {
        if (logStartAndEnd)
            CrashBreadcrumbs.Log($"{label} start");

        try
        {
            action();
            if (logStartAndEnd)
                CrashBreadcrumbs.Log($"{label} end");
        }
        catch (Exception ex)
        {
            CrashBreadcrumbs.LogException(label, ex);
        }
    }

    private bool ShouldLogTickBreadcrumb()
    {
        _tickCount++;

        var now = DateTime.UtcNow;
        if (now <= _detailedTickLogUntilUtc)
            return true;

        if (now - _loadedAtUtc > TimeSpan.FromMinutes(2))
            return false;

        if (_startupTickBreadcrumbs >= 20 || now < _nextStartupTickBreadcrumbAtUtc)
            return false;

        _startupTickBreadcrumbs++;
        _nextStartupTickBreadcrumbAtUtc = now.AddSeconds(3);
        return true;
    }
}
