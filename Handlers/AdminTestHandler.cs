using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using ReclaimCS.Shared.Administration;
using ZombieModPlugin.Abilities;
using ZombieModPlugin.Configs;
using ZombieModPlugin.Extensions;
using ZombieModPlugin.Humans.Handlers;
using ZombieModPlugin.Humans.Models;
using ZombieModPlugin.Progression.Models;
using ZombieModPlugin.Progression.Services;
using ZombieModPlugin.Rounds;
using ZombieModPlugin.States;
using ZombieModPlugin.Zombies.Handlers;
using ZombieModPlugin.Zombies.Models;

namespace ZombieModPlugin.Handlers;

public class AdminTestHandler
{
    private readonly Dictionary<ulong, PlayerState> _playerStates;
    private readonly BaseConfig _config;
    private readonly BasePlugin _plugin;
    private readonly ZombieHandler _zombieHandler;
    private readonly HumanHandler _humanHandler;
    private readonly ZombieRoundManager _roundManager;
    private readonly ProgressionService _progressionService;

    public AdminTestHandler(
        Dictionary<ulong, PlayerState> playerStates,
        BaseConfig config,
        BasePlugin plugin,
        ZombieHandler zombieHandler,
        HumanHandler humanHandler,
        ZombieRoundManager roundManager,
        ProgressionService progressionService)
    {
        _playerStates = playerStates;
        _config = config;
        _plugin = plugin;
        _zombieHandler = zombieHandler;
        _humanHandler = humanHandler;
        _roundManager = roundManager;
        _progressionService = progressionService;
    }

    public void RegisterCommands()
    {
        var adminConfig = _config.AdminTestConfig;

        _plugin.AddCommand($"css_{NormalizeCommandName(adminConfig.MenuCommand, "zadmin")}", "Show Zombie Mod admin test menu.", OnAdminMenuCommand);
        _plugin.AddCommand($"css_{NormalizeCommandName(adminConfig.ClassCommand, "zclass")}", "Force yourself into a zombie class for testing.", OnZombieClassCommand);
        _plugin.AddCommand($"css_{NormalizeCommandName(adminConfig.HumanCommand, "zhuman")}", "Force yourself back to human for testing.", OnHumanCommand);
        _plugin.AddCommand($"css_{NormalizeCommandName(adminConfig.HumanClassCommand, "hclass")}", "Force yourself into a human class for testing.", OnHumanCommand);
        _plugin.AddCommand($"css_{NormalizeCommandName(adminConfig.BotsCommand, "zbots")}", "Manage Zombie Mod test bots.", OnBotsCommand);
        _plugin.AddCommand($"css_{NormalizeCommandName(adminConfig.RoundCommand, "zround")}", "Manage Zombie Mod test round flow.", OnRoundCommand);
        _plugin.AddCommand("css_map", "Change the current map.", OnMapCommand);
        _plugin.AddCommand("css_givexp", "Give global or class XP to a player.", OnGiveXpCommand);
        _plugin.AddCommand("css_setlevel", "Set global or class level for a player.", OnSetLevelCommand);
        _plugin.AddCommand("css_unlockall", "Unlock all progression items for a player.", OnUnlockAllCommand);
        _plugin.AddCommand("css_resetprogress", "Reset progression for a player.", OnResetProgressCommand);
        _plugin.AddCommand("css_giveclassxp", "Give class XP to a player.", OnGiveClassXpCommand);
        _plugin.AddCommand("css_unlockability", "Unlock an ability for a player.", OnUnlockAbilityCommand);
        _plugin.AddCommand("css_unlockclass", "Unlock a class for a player.", OnUnlockClassCommand);
    }

    private void OnAdminMenuCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!CanUse(player, command))
            return;

        if (command.ArgCount < 2 || IsHelp(command.GetArg(1)))
        {
            PrintMenu(player, command);
            return;
        }

        ExecuteMenuAction(player, command, command.GetArg(1));
    }

    private void OnZombieClassCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!CanUse(player, command) || !TryGetPlayer(player, command, out var controller))
            return;

        if (command.ArgCount < 2 || IsHelp(command.GetArg(1)))
        {
            PrintZombieClassList(command);
            return;
        }

        if (!TryResolveZombie(command.GetArg(1), out var zombie))
        {
            command.ReplyToCommand($"Unknown zombie class: {command.GetArg(1)}");
            PrintZombieClassList(command);
            return;
        }

        ForceZombieClass(controller, zombie, command);
    }

    private void OnHumanCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!CanUse(player, command) || !TryGetPlayer(player, command, out var controller))
            return;

        if (command.ArgCount >= 2 && IsHelp(command.GetArg(1)))
        {
            PrintHumanClassList(command);
            return;
        }

        if (command.ArgCount >= 2)
        {
            if (!TryResolveHuman(command.GetArg(1), out var humanClass))
            {
                command.ReplyToCommand($"Unknown human class: {command.GetArg(1)}");
                PrintHumanClassList(command);
                return;
            }

            ForceHuman(controller, command, humanClass);
            return;
        }

        ForceHuman(controller, command, null);
    }

    private void OnBotsCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!CanUse(player, command))
            return;

        if (command.ArgCount < 2 || IsHelp(command.GetArg(1)))
        {
            command.ReplyToCommand("Usage: css_zbots <on|off|add|ct|t|kick|round> [count] [ct|t]");
            command.ReplyToCommand("Examples: css_zbots add 3 ct | css_zbots round | css_zbots kick");
            return;
        }

        var action = Normalize(command.GetArg(1));
        switch (action)
        {
            case "on":
            case "enable":
                _roundManager.SetBotsInRoundForTesting(true);
                command.ReplyToCommand("[ZM ADMIN] Bots are enabled for zombie round testing.");
                break;

            case "off":
            case "disable":
                _roundManager.SetBotsInRoundForTesting(false);
                command.ReplyToCommand("[ZM ADMIN] Bots are disabled and kicked.");
                break;

            case "kick":
            case "clear":
                KickBots();
                _roundManager.SetBotsInRoundForTesting(false);
                command.ReplyToCommand("[ZM ADMIN] Kicked bots and disabled bot participation.");
                break;

            case "round":
            case "botround":
                StartBotRound(command);
                break;

            case "add":
            case "spawn":
                AddBotsFromCommand(command, defaultTeam: "ct");
                break;

            case "ct":
            case "human":
            case "humans":
                AddBots(ParseCount(command, 2), "ct");
                command.ReplyToCommand("[ZM ADMIN] Added CT bot(s) and enabled bot participation.");
                break;

            case "t":
            case "zombie":
            case "zombies":
                AddBots(ParseCount(command, 2), "t");
                command.ReplyToCommand("[ZM ADMIN] Added T bot(s) and enabled bot participation.");
                break;

            default:
                command.ReplyToCommand($"Unknown bot action: {command.GetArg(1)}");
                break;
        }
    }

    private void OnRoundCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!CanUse(player, command))
            return;

        if (command.ArgCount < 2 || IsHelp(command.GetArg(1)))
        {
            command.ReplyToCommand("Usage: css_zround <status|restart|test|botround>");
            return;
        }

        var action = Normalize(command.GetArg(1));
        switch (action)
        {
            case "status":
                command.ReplyToCommand($"[ZM ADMIN] {_roundManager.GetDebugStatus()}");
                break;

            case "restart":
            case "start":
                _roundManager.SetBotsInRoundForTesting(false);
                _roundManager.RestartRoundForTesting();
                command.ReplyToCommand("[ZM ADMIN] Zombie round loop restarted.");
                break;

            case "test":
            case "practice":
            case "stop":
                _roundManager.EnterAdminTestMode();
                command.ReplyToCommand("[ZM ADMIN] Admin test mode enabled. Round loop paused until css_zround restart.");
                break;

            case "botround":
            case "bots":
                StartBotRound(command);
                break;

            default:
                command.ReplyToCommand($"Unknown round action: {command.GetArg(1)}");
                break;
        }
    }

    private void OnMapCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!CanUse(player, command))
            return;

        if (command.ArgCount < 2 || IsHelp(command.GetArg(1)))
        {
            command.ReplyToCommand("[ZM ADMIN] Usage: css_map <mapname|workshop_id>");
            var configuredMaps = GetConfiguredMapHelpText();
            if (!string.IsNullOrWhiteSpace(configuredMaps))
                command.ReplyToCommand($"[ZM ADMIN] Configured maps: {configuredMaps}");

            return;
        }

        var requestedMap = command.GetArg(1).Trim();
        if (!IsSafeMapToken(requestedMap))
        {
            command.ReplyToCommand("[ZM ADMIN] Invalid map name. Use only letters, numbers, _, -, /, and .");
            return;
        }

        if (TryResolveConfiguredWorkshopMap(requestedMap, out var workshopMapName, out var workshopMapId))
        {
            command.ReplyToCommand($"[ZM ADMIN] Changing to workshop map {workshopMapName} ({workshopMapId}).");
            Console.WriteLine($"[ZombieMod][Admin] {DescribeAdminActor(player)} changing map via css_map to workshop map {workshopMapName} ({workshopMapId}).");
            Server.ExecuteCommand($"host_workshop_map {workshopMapId}");
            return;
        }

        if (requestedMap.All(char.IsDigit))
        {
            command.ReplyToCommand($"[ZM ADMIN] Changing to workshop map id {requestedMap}.");
            Console.WriteLine($"[ZombieMod][Admin] {DescribeAdminActor(player)} changing map via css_map to workshop id {requestedMap}.");
            Server.ExecuteCommand($"host_workshop_map {requestedMap}");
            return;
        }

        command.ReplyToCommand($"[ZM ADMIN] Changing map to {requestedMap}.");
        Console.WriteLine($"[ZombieMod][Admin] {DescribeAdminActor(player)} changing map via css_map to {requestedMap}.");
        Server.ExecuteCommand($"changelevel {requestedMap}");
    }

    private void OnGiveXpCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!CanUse(player, command))
            return;

        if (command.ArgCount < 3 || !int.TryParse(command.GetArg(2), out var amount))
        {
            command.ReplyToCommand("[ZM ADMIN] Usage: css_givexp <global|zombie|human> <amount> [target]");
            return;
        }

        if (!TryResolveTarget(player, command, 3, out var target))
            return;

        var state = target.GetState(_playerStates);
        var scope = Normalize(command.GetArg(1));
        if (scope == "global")
        {
            _progressionService.AwardXp(target, state, amount, 0, "admin grant");
            command.ReplyToCommand($"[ZM ADMIN] Gave {amount} global XP to {target.PlayerName}.");
            return;
        }

        if (!TryParseRole(scope, out var role))
        {
            command.ReplyToCommand("[ZM ADMIN] Scope must be global, zombie, or human.");
            return;
        }

        var classId = role == ProgressionClassRole.Zombie
            ? _progressionService.GetPreferredZombie(state).Id
            : _progressionService.GetPreferredHuman(state).Id;

        _progressionService.AwardClassXp(target, state, role, classId, amount, "admin class grant");
        command.ReplyToCommand($"[ZM ADMIN] Gave {amount} {role} class XP to {target.PlayerName}.");
    }

    private void OnGiveClassXpCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!CanUse(player, command))
            return;

        if (command.ArgCount < 4
            || !TryParseRole(command.GetArg(1), out var role)
            || !int.TryParse(command.GetArg(3), out var amount))
        {
            command.ReplyToCommand("[ZM ADMIN] Usage: css_giveclassxp <zombie|human> <classId> <amount> [target]");
            return;
        }

        if (!TryResolveTarget(player, command, 4, out var target))
            return;

        if (!ClassExists(role, command.GetArg(2)))
        {
            command.ReplyToCommand($"[ZM ADMIN] Unknown {role} class: {command.GetArg(2)}");
            return;
        }

        var state = target.GetState(_playerStates);
        _progressionService.AwardClassXp(target, state, role, command.GetArg(2), amount, "admin class grant");
        command.ReplyToCommand($"[ZM ADMIN] Gave {amount} XP to {target.PlayerName}'s {_progressionService.GetClassName(role, command.GetArg(2))}.");
    }

    private void OnSetLevelCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!CanUse(player, command))
            return;

        if (command.ArgCount < 3)
        {
            command.ReplyToCommand("[ZM ADMIN] Usage: css_setlevel global <level> [target] OR css_setlevel <zombie|human> <classId> <level> [target]");
            return;
        }

        var scope = Normalize(command.GetArg(1));
        if (scope == "global")
        {
            if (!int.TryParse(command.GetArg(2), out var globalLevel) || !TryResolveTarget(player, command, 3, out var target))
            {
                command.ReplyToCommand("[ZM ADMIN] Usage: css_setlevel global <level> [target]");
                return;
            }

            _progressionService.SetGlobalLevel(target, target.GetState(_playerStates), globalLevel);
            command.ReplyToCommand($"[ZM ADMIN] Set {target.PlayerName}'s global level to {globalLevel}.");
            return;
        }

        if (command.ArgCount < 4
            || !TryParseRole(scope, out var role)
            || !int.TryParse(command.GetArg(3), out var classLevel)
            || !TryResolveTarget(player, command, 4, out var classTarget))
        {
            command.ReplyToCommand("[ZM ADMIN] Usage: css_setlevel <zombie|human> <classId> <level> [target]");
            return;
        }

        if (!ClassExists(role, command.GetArg(2)))
        {
            command.ReplyToCommand($"[ZM ADMIN] Unknown {role} class: {command.GetArg(2)}");
            return;
        }

        _progressionService.SetClassLevel(classTarget, classTarget.GetState(_playerStates), role, command.GetArg(2), classLevel);
        command.ReplyToCommand($"[ZM ADMIN] Set {classTarget.PlayerName}'s {_progressionService.GetClassName(role, command.GetArg(2))} level to {classLevel}.");
    }

    private void OnUnlockAllCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!CanUse(player, command) || !TryResolveTarget(player, command, 1, out var target))
            return;

        _progressionService.UnlockAll(target, target.GetState(_playerStates));
        command.ReplyToCommand($"[ZM ADMIN] Unlocked all classes and abilities for {target.PlayerName}.");
    }

    private void OnResetProgressCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!CanUse(player, command) || !TryResolveTarget(player, command, 1, out var target))
            return;

        _progressionService.ResetProgress(target, target.GetState(_playerStates));
        command.ReplyToCommand($"[ZM ADMIN] Reset progression for {target.PlayerName}.");
    }

    private void OnUnlockAbilityCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!CanUse(player, command))
            return;

        if (command.ArgCount < 4
            || !TryParseRole(command.GetArg(1), out var role)
            || !TryResolveAbility(command.GetArg(3), out var ability))
        {
            command.ReplyToCommand("[ZM ADMIN] Usage: css_unlockability <zombie|human> <classId> <abilityId> [target]");
            return;
        }

        if (!TryResolveTarget(player, command, 4, out var target))
            return;

        var result = _progressionService.ForceUnlockAbility(target, target.GetState(_playerStates), role, command.GetArg(2), ability);
        command.ReplyToCommand($"[ZM ADMIN] {result.Message}");
    }

    private void OnUnlockClassCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!CanUse(player, command))
            return;

        if (command.ArgCount < 3 || !TryParseRole(command.GetArg(1), out var role))
        {
            command.ReplyToCommand("[ZM ADMIN] Usage: css_unlockclass <zombie|human> <classId> [target]");
            return;
        }

        if (!TryResolveTarget(player, command, 3, out var target))
            return;

        var result = _progressionService.ForceUnlockClass(target, target.GetState(_playerStates), role, command.GetArg(2));
        command.ReplyToCommand($"[ZM ADMIN] {result.Message}");
    }

    private void PrintMenu(CCSPlayerController? player, CommandInfo command)
    {
        command.ReplyToCommand("=== Zombie Mod Admin Test Menu ===");

        var index = 1;
        foreach (var zombie in _config.ZombieConfig.ZombieTypes)
        {
            command.ReplyToCommand($"{index}. Become {zombie.Name}: css_zadmin {index} or css_zclass {zombie.Id}");
            index++;
        }

        var humanIndex = index++;
        var botRoundIndex = index++;
        var addBotsIndex = index++;
        var kickBotsIndex = index++;
        var restartIndex = index++;
        var statusIndex = index;

        command.ReplyToCommand($"{humanIndex}. Become Human: css_zadmin human or css_zhuman [classId]");
        command.ReplyToCommand($"Human classes: {string.Join(", ", _config.HumanConfig.HumanClasses.Select(human => human.Id))}");
        command.ReplyToCommand($"{botRoundIndex}. Start bot test round: css_zadmin botround");
        command.ReplyToCommand($"{addBotsIndex}. Add CT bots: css_zadmin addbots");
        command.ReplyToCommand($"{kickBotsIndex}. Kick bots: css_zadmin kickbots");
        command.ReplyToCommand($"{restartIndex}. Restart zombie loop: css_zadmin restart");
        command.ReplyToCommand($"{statusIndex}. Status: css_zadmin status");
        command.ReplyToCommand("Maps: css_map zm_map_name or css_map <workshop_id>");
        command.ReplyToCommand("Progression: css_givexp global 500 | css_setlevel zombie brute 5 | css_unlockall");
        command.ReplyToCommand("Unlocks: css_unlockclass zombie brute | css_unlockability zombie brute selfdestruct");
        command.ReplyToCommand("Bind example: bind kp_1 \"css_zclass brute\"");

        player?.PrintToCenterHtml("<font color='#ff3d3d'>Zombie Admin Test</font><br><font color='#ffffff'>Menu printed in chat/console.</font>", 5);
    }

    private void ExecuteMenuAction(CCSPlayerController? player, CommandInfo command, string rawAction)
    {
        var zombieCount = _config.ZombieConfig.ZombieTypes.Length;
        var action = Normalize(rawAction);

        if (int.TryParse(rawAction, out var actionNumber))
        {
            if (actionNumber >= 1 && actionNumber <= zombieCount)
            {
                ExecuteClassAction(player, command, _config.ZombieConfig.ZombieTypes[actionNumber - 1]);
                return;
            }

            action = actionNumber switch
            {
                var value when value == zombieCount + 1 => "human",
                var value when value == zombieCount + 2 => "botround",
                var value when value == zombieCount + 3 => "addbots",
                var value when value == zombieCount + 4 => "kickbots",
                var value when value == zombieCount + 5 => "restart",
                var value when value == zombieCount + 6 => "status",
                _ => action
            };
        }

        switch (action)
        {
            case "human":
            case "ct":
                if (!TryGetPlayer(player, command, out var humanPlayer))
                    return;

                ForceHuman(humanPlayer, command, null);
                break;

            case "botround":
            case "bots":
                StartBotRound(command);
                break;

            case "addbots":
            case "addbot":
                AddBots(_config.AdminTestConfig.DefaultBotCount, "ct");
                command.ReplyToCommand("[ZM ADMIN] Added CT bot(s) and enabled bot participation.");
                break;

            case "kickbots":
            case "kickbot":
                KickBots();
                _roundManager.SetBotsInRoundForTesting(false);
                command.ReplyToCommand("[ZM ADMIN] Kicked bots and disabled bot participation.");
                break;

            case "restart":
            case "start":
                _roundManager.SetBotsInRoundForTesting(false);
                _roundManager.RestartRoundForTesting();
                command.ReplyToCommand("[ZM ADMIN] Zombie round loop restarted.");
                break;

            case "status":
                command.ReplyToCommand($"[ZM ADMIN] {_roundManager.GetDebugStatus()}");
                break;

            default:
                if (TryResolveZombie(rawAction, out var zombie))
                    ExecuteClassAction(player, command, zombie);
                else
                    command.ReplyToCommand($"Unknown admin menu action: {rawAction}");

                break;
        }
    }

    private void ExecuteClassAction(CCSPlayerController? player, CommandInfo command, Zombie zombie)
    {
        if (!TryGetPlayer(player, command, out var controller))
            return;

        ForceZombieClass(controller, zombie, command);
    }

    private void ForceZombieClass(CCSPlayerController player, Zombie zombie, CommandInfo command)
    {
        _roundManager.EnterAdminTestMode();

        var state = player.GetState(_playerStates);
        EnsureProgression(state);

        state.SelectedZombieType = zombie;
        state.SelectedHumanClass = null;
        state.IsZombie = true;
        _zombieHandler.OnBecomeZombie(player, state);

        command.ReplyToCommand($"[ZM ADMIN] Forced {player.PlayerName} to zombie class: {zombie.Name}.");
        player.PrintToCenterHtml($"<font color='#ff3d3d'>{zombie.Name}</font><br><font color='#ffffff'>Admin test mode</font>", 5);
    }

    private void ForceHuman(CCSPlayerController player, CommandInfo command, HumanClass? humanClass)
    {
        _roundManager.EnterAdminTestMode();

        var state = player.GetState(_playerStates);
        state.SelectedZombieType = null;
        state.SelectedHumanClass = humanClass ?? _humanHandler.GetDefaultHumanClass();
        state.GlobalCooldowns.Clear();
        state.ActiveAbilities.Clear();

        state.IsZombie = false;
        _humanHandler.OnBecomeHuman(player, state);

        command.ReplyToCommand($"[ZM ADMIN] Forced {player.PlayerName} to human class: {state.SelectedHumanClass.Name}.");
        player.PrintToCenterHtml($"<font color='#7fd7ff'>{state.SelectedHumanClass.Name}</font><br><font color='#ffffff'>Admin test mode</font>", 5);
    }

    private void StartBotRound(CommandInfo command)
    {
        var count = ParseCount(command, 2);
        _roundManager.EnterAdminTestMode();
        KickBots();
        AddBots(count, "ct");
        _roundManager.RestartRoundForTesting();
        command.ReplyToCommand($"[ZM ADMIN] Started a bot test round with {count} CT bot(s).");
    }

    private void AddBotsFromCommand(CommandInfo command, string defaultTeam)
    {
        var count = ParseCount(command, 2);
        var team = command.ArgCount >= 4 ? Normalize(command.GetArg(3)) : defaultTeam;

        if (team is "terrorist" or "zombie" or "zombies")
            team = "t";
        else if (team is "counterterrorist" or "human" or "humans")
            team = "ct";

        AddBots(count, team);
        command.ReplyToCommand($"[ZM ADMIN] Added {count} {team.ToUpperInvariant()} bot(s) and enabled bot participation.");
    }

    private void AddBots(int count, string team)
    {
        count = Math.Clamp(count, 1, 16);
        _roundManager.SetBotsInRoundForTesting(true, count);

        var command = Normalize(team) switch
        {
            "t" or "terrorist" or "zombie" or "zombies" => "bot_add_t",
            "ct" or "counterterrorist" or "human" or "humans" => "bot_add_ct",
            _ => "bot_add"
        };

        for (var i = 0; i < count; i++)
            Server.ExecuteCommand(command);
    }

    private void KickBots()
    {
        Server.ExecuteCommand("bot_kick");
    }

    private bool TryResolveConfiguredWorkshopMap(string requestedMap, out string mapName, out string workshopMapId)
    {
        var mapNames = _config.GeneralConfig.WorkshopMapNames ?? [];
        var mapIds = _config.GeneralConfig.WorkshopMapIds ?? [];
        var normalizedRequest = NormalizeMapToken(requestedMap);

        for (var i = 0; i < mapNames.Length; i++)
        {
            var candidateName = mapNames[i]?.Trim() ?? string.Empty;
            var candidateMapId = i < mapIds.Length
                ? mapIds[i]?.Trim() ?? string.Empty
                : string.Empty;

            if (string.IsNullOrWhiteSpace(candidateName)
                || NormalizeMapToken(candidateName) != normalizedRequest
                || string.IsNullOrWhiteSpace(candidateMapId)
                || !candidateMapId.All(char.IsDigit))
            {
                continue;
            }

            mapName = candidateName;
            workshopMapId = candidateMapId;
            return true;
        }

        mapName = string.Empty;
        workshopMapId = string.Empty;
        return false;
    }

    private string GetConfiguredMapHelpText()
    {
        return string.Join(", ", (_config.GeneralConfig.WorkshopMapNames ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim()));
    }

    private static bool IsSafeMapToken(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.All(character => char.IsLetterOrDigit(character)
                || character is '_' or '-' or '/' or '.');
    }

    private static string NormalizeMapToken(string value)
    {
        return value.Trim().Replace('\\', '/').ToLowerInvariant();
    }

    private static string DescribeAdminActor(CCSPlayerController? player)
    {
        return player is { IsValid: true }
            ? $"{player.PlayerName} ({player.SteamID})"
            : "console";
    }

    private int ParseCount(CommandInfo command, int argIndex)
    {
        if (command.ArgCount > argIndex && int.TryParse(command.GetArg(argIndex), out var count))
            return Math.Clamp(count, 1, 16);

        return Math.Clamp(_config.AdminTestConfig.DefaultBotCount, 1, 16);
    }

    private void PrintZombieClassList(CommandInfo command)
    {
        command.ReplyToCommand("Available zombie classes:");
        for (var i = 0; i < _config.ZombieConfig.ZombieTypes.Length; i++)
        {
            var zombie = _config.ZombieConfig.ZombieTypes[i];
            command.ReplyToCommand($"{i + 1}. {zombie.Name} [{zombie.Id}]");
        }
    }

    private void PrintHumanClassList(CommandInfo command)
    {
        command.ReplyToCommand("Available human classes:");
        for (var i = 0; i < _config.HumanConfig.HumanClasses.Length; i++)
        {
            var human = _config.HumanConfig.HumanClasses[i];
            command.ReplyToCommand($"{i + 1}. {human.Name} [{human.Id}]");
        }
    }

    private bool TryResolveZombie(string value, out Zombie zombie)
    {
        if (int.TryParse(value, out var index)
            && index >= 1
            && index <= _config.ZombieConfig.ZombieTypes.Length)
        {
            zombie = _config.ZombieConfig.ZombieTypes[index - 1];
            return true;
        }

        var normalized = Normalize(value);
        foreach (var candidate in _config.ZombieConfig.ZombieTypes)
        {
            if (Normalize(candidate.Id) == normalized || Normalize(candidate.Name) == normalized)
            {
                zombie = candidate;
                return true;
            }
        }

        zombie = null!;
        return false;
    }

    private bool TryResolveHuman(string value, out HumanClass humanClass)
    {
        if (int.TryParse(value, out var index)
            && index >= 1
            && index <= _config.HumanConfig.HumanClasses.Length)
        {
            humanClass = _config.HumanConfig.HumanClasses[index - 1];
            return true;
        }

        var normalized = Normalize(value);
        foreach (var candidate in _config.HumanConfig.HumanClasses)
        {
            if (Normalize(candidate.Id) == normalized || Normalize(candidate.Name) == normalized)
            {
                humanClass = candidate;
                return true;
            }
        }

        humanClass = null!;
        return false;
    }

    private void EnsureProgression(PlayerState state)
    {
        foreach (var zombieType in _config.ZombieConfig.ZombieTypes)
        {
            if (!state.ZombieProgression.ContainsKey(zombieType.Id))
                state.ZombieProgression[zombieType.Id] = new ZombieProgression();
        }
    }

    private bool TryResolveTarget(
        CCSPlayerController? player,
        CommandInfo command,
        int targetArgIndex,
        out CCSPlayerController target)
    {
        target = null!;

        if (command.ArgCount > targetArgIndex)
        {
            var targetText = command.GetArg(targetArgIndex);
            var matches = Utilities.GetPlayers()
                .Where(candidate => candidate is { IsValid: true }
                    && !candidate.IsBot
                    && candidate.Connected == PlayerConnectedState.Connected
                    && (candidate.PlayerName.Contains(targetText, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(candidate.SteamID.ToString(), targetText, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (matches.Count == 1)
            {
                target = matches[0];
                return true;
            }

            command.ReplyToCommand(matches.Count == 0
                ? $"[ZM ADMIN] No connected player matched '{targetText}'."
                : $"[ZM ADMIN] Multiple players matched '{targetText}'. Use more of the name or SteamID.");
            return false;
        }

        if (player is { IsValid: true })
        {
            target = player;
            return true;
        }

        command.ReplyToCommand("[ZM ADMIN] Console usage requires a target player name or SteamID.");
        return false;
    }

    private static bool TryParseRole(string value, out ProgressionClassRole role)
    {
        switch (Normalize(value))
        {
            case "z":
            case "zombie":
            case "zombies":
                role = ProgressionClassRole.Zombie;
                return true;

            case "h":
            case "human":
            case "humans":
                role = ProgressionClassRole.Human;
                return true;

            default:
                role = default;
                return false;
        }
    }

    private static bool TryResolveAbility(string value, out AbilityType ability)
    {
        foreach (var candidate in Enum.GetValues<AbilityType>())
        {
            var registered = AbilityRegistry.Get(candidate);
            if (Normalize(candidate.ToString()) == Normalize(value)
                || (registered != null
                    && (Normalize(registered.Id) == Normalize(value) || Normalize(registered.Name) == Normalize(value))))
            {
                ability = candidate;
                return true;
            }
        }

        ability = default;
        return false;
    }

    private bool ClassExists(ProgressionClassRole role, string classId)
    {
        return role == ProgressionClassRole.Zombie
            ? _progressionService.FindZombie(classId) != null
            : _progressionService.FindHuman(classId) != null;
    }

    private bool CanUse(CCSPlayerController? player, CommandInfo command)
    {
        var adminConfig = _config.AdminTestConfig;
        if (!adminConfig.Enabled)
        {
            command.ReplyToCommand("[ZM ADMIN] Admin test commands are disabled.");
            return false;
        }

        if (ReclaimAdminService.CanUseAdminFeature(player, _config.Admin))
            return true;

        command.ReplyToCommand("[ZM ADMIN] You do not have permission to use Zombie Mod admin test commands.");
        return false;
    }

    private static bool TryGetPlayer(CCSPlayerController? player, CommandInfo command, out CCSPlayerController controller)
    {
        controller = null!;
        if (player is { IsValid: true })
        {
            controller = player;
            return true;
        }

        command.ReplyToCommand("[ZM ADMIN] This action must be used by a connected player.");
        return false;
    }

    private static bool IsHelp(string value)
    {
        return Normalize(value) is "help" or "list" or "menu";
    }

    private static string Normalize(string value)
    {
        return value
            .Trim()
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty)
            .ToLowerInvariant();
    }

    private static string NormalizeCommandName(string configuredCommand, string fallback)
    {
        var command = string.IsNullOrWhiteSpace(configuredCommand)
            ? fallback
            : configuredCommand.Trim();

        command = command.TrimStart('!', '/');
        if (command.StartsWith("css_", StringComparison.OrdinalIgnoreCase))
            command = command[4..];

        return command.ToLowerInvariant();
    }
}
