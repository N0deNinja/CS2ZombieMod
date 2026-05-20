using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using ZombieModPlugin.Configs;
using ZombieModPlugin.Extensions;
using ZombieModPlugin.Humans.Handlers;
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

    public AdminTestHandler(
        Dictionary<ulong, PlayerState> playerStates,
        BaseConfig config,
        BasePlugin plugin,
        ZombieHandler zombieHandler,
        HumanHandler humanHandler,
        ZombieRoundManager roundManager)
    {
        _playerStates = playerStates;
        _config = config;
        _plugin = plugin;
        _zombieHandler = zombieHandler;
        _humanHandler = humanHandler;
        _roundManager = roundManager;
    }

    public void RegisterCommands()
    {
        var adminConfig = _config.AdminTestConfig;

        _plugin.AddCommand($"css_{NormalizeCommandName(adminConfig.MenuCommand, "zadmin")}", "Show Zombie Mod admin test menu.", OnAdminMenuCommand);
        _plugin.AddCommand($"css_{NormalizeCommandName(adminConfig.ClassCommand, "zclass")}", "Force yourself into a zombie class for testing.", OnZombieClassCommand);
        _plugin.AddCommand($"css_{NormalizeCommandName(adminConfig.HumanCommand, "zhuman")}", "Force yourself back to human for testing.", OnHumanCommand);
        _plugin.AddCommand($"css_{NormalizeCommandName(adminConfig.BotsCommand, "zbots")}", "Manage Zombie Mod test bots.", OnBotsCommand);
        _plugin.AddCommand($"css_{NormalizeCommandName(adminConfig.RoundCommand, "zround")}", "Manage Zombie Mod test round flow.", OnRoundCommand);
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

        ForceHuman(controller, command);
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

        command.ReplyToCommand($"{humanIndex}. Become Human: css_zadmin human or css_zhuman");
        command.ReplyToCommand($"{botRoundIndex}. Start bot test round: css_zadmin botround");
        command.ReplyToCommand($"{addBotsIndex}. Add CT bots: css_zadmin addbots");
        command.ReplyToCommand($"{kickBotsIndex}. Kick bots: css_zadmin kickbots");
        command.ReplyToCommand($"{restartIndex}. Restart zombie loop: css_zadmin restart");
        command.ReplyToCommand($"{statusIndex}. Status: css_zadmin status");
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

                ForceHuman(humanPlayer, command);
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
        state.IsZombie = true;
        _zombieHandler.OnBecomeZombie(player, state);

        command.ReplyToCommand($"[ZM ADMIN] Forced {player.PlayerName} to zombie class: {zombie.Name}.");
        player.PrintToCenterHtml($"<font color='#ff3d3d'>{zombie.Name}</font><br><font color='#ffffff'>Admin test mode</font>", 5);
    }

    private void ForceHuman(CCSPlayerController player, CommandInfo command)
    {
        _roundManager.EnterAdminTestMode();

        var state = player.GetState(_playerStates);
        state.SelectedZombieType = null;
        state.GlobalCooldowns.Clear();
        state.ActiveAbilities.Clear();

        state.IsZombie = false;
        _humanHandler.OnBecomeHuman(player, state);

        command.ReplyToCommand($"[ZM ADMIN] Forced {player.PlayerName} back to human.");
        player.PrintToCenterHtml("<font color='#7fd7ff'>Human</font><br><font color='#ffffff'>Admin test mode</font>", 5);
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

    private void EnsureProgression(PlayerState state)
    {
        foreach (var zombieType in _config.ZombieConfig.ZombieTypes)
        {
            if (!state.ZombieProgression.ContainsKey(zombieType.Id))
                state.ZombieProgression[zombieType.Id] = new ZombieProgression();
        }
    }

    private bool CanUse(CCSPlayerController? player, CommandInfo command)
    {
        var adminConfig = _config.AdminTestConfig;
        if (!adminConfig.Enabled)
        {
            command.ReplyToCommand("[ZM ADMIN] Admin test commands are disabled.");
            return false;
        }

        if (!adminConfig.RequireAdminPermissions || player == null)
            return true;

        if (adminConfig.RequiredPermissions.Length == 0)
            return true;

        if (AdminManager.PlayerHasPermissions(player, adminConfig.RequiredPermissions))
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
