using System.Globalization;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using ZombieModPlugin.Configs;
using ZombieModPlugin.Extensions;
using ZombieModPlugin.Humans.Models;
using ZombieModPlugin.States;
using ZombieModPlugin.Zombies.Models;

namespace ZombieModPlugin.Handlers;

public class PlayerCommandHandler
{
    private readonly Dictionary<ulong, PlayerState> _playerStates;
    private readonly BaseConfig _config;
    private readonly BasePlugin _plugin;

    public PlayerCommandHandler(
        Dictionary<ulong, PlayerState> playerStates,
        BaseConfig config,
        BasePlugin plugin)
    {
        _playerStates = playerStates;
        _config = config;
        _plugin = plugin;
    }

    public void RegisterCommands()
    {
        var commands = _config.CommandsConfig;
        var registered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddPlayerCommand(registered, commands.Help, "help", "Show Zombie Mod player commands.", OnHelpCommand);
        AddPlayerCommand(registered, commands.XP, "xp", "Show your current class XP.", OnXpCommand);
        AddPlayerCommand(registered, commands.Level, "level", "Show your current class XP and level.", OnXpCommand);
        AddPlayerCommand(registered, commands.Zombies, "zombies", "List zombie classes or choose your default zombie.", OnZombieCommand);
        AddPlayerCommand(registered, commands.SwitchZombie, "zombie", "Choose your default zombie.", OnZombieCommand);
        AddPlayerCommand(registered, commands.DefaultZombie, "zdefault", "Choose your default zombie.", OnZombieCommand);
        AddPlayerCommand(registered, commands.Humans, "humans", "List human classes or choose your default human.", OnHumanCommand);
        AddPlayerCommand(registered, commands.SwitchHuman, "human", "Choose your default human.", OnHumanCommand);
        AddPlayerCommand(registered, commands.DefaultHuman, "hdefault", "Choose your default human.", OnHumanCommand);
    }

    private void AddPlayerCommand(
        HashSet<string> registered,
        string configuredCommand,
        string fallback,
        string description,
        CommandInfo.CommandCallback handler)
    {
        var commandName = NormalizeCommandName(configuredCommand, fallback);
        if (!registered.Add(commandName))
            return;

        _plugin.AddCommand($"css_{commandName}", description, handler);
    }

    private void OnHelpCommand(CCSPlayerController? player, CommandInfo command)
    {
        var commands = _config.CommandsConfig;
        ReplyHeader(command, "Zombie Mod Commands");
        Reply(command, $"{ChatCommand(commands.Help, "help")} - show this command list.");
        Reply(command, $"{ChatCommand(commands.XP, "xp")} - show XP for your current zombie or human class.");
        Reply(command, $"{ChatCommand(commands.Zombies, "zombies")} - list zombie classes.");
        Reply(command, $"{ChatCommand(commands.SwitchZombie, "zombie")} {ArgToken("<id>")} - set your default zombie for your next zombie spawn.");
        Reply(command, $"{ChatCommand(commands.Humans, "humans")} - list human classes.");
        Reply(command, $"{ChatCommand(commands.SwitchHuman, "human")} {ArgToken("<id>")} - set your default human for next round.");
        Reply(command, $"{ChatCommand(commands.Abilities, "abilities")} - list zombie abilities and unlocks.");
        Reply(command, $"{ChatCommand("zability", "zability")} {ArgToken("<slot>")} - use a zombie ability. Example: {CommandToken("bind mouse4 \"css_zability 1\"")}");

        if (_config.AdminTestConfig.Enabled)
            Reply(command, $"{ChatCommand("zadmin", "zadmin")} - local admin test menu, if you have access.");
    }

    private void OnXpCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!TryGetPlayerState(player, command, out var state))
            return;

        if (state.IsZombie)
        {
            var zombie = GetEffectiveZombie(state);
            var progression = GetZombieProgression(state, zombie);
            ReplyZombie(command, $"{RoleToken(zombie.Name, isZombie: true)}: {FormatProgression(progression.Level, progression.XP, _config.ZombieConfig.MaxLevel, GetRequiredZombieXpForNextLevel)}");
            Reply(command, $"Default zombie: {RoleToken(GetEffectivePreferredZombie(state).Name, isZombie: true)}. Use {ChatCommand(_config.CommandsConfig.Zombies, "zombies")} to list classes.");
            return;
        }

        var humanClass = GetEffectiveHuman(state);
        var humanProgression = GetHumanProgression(state, humanClass);
        ReplyHuman(command, $"{RoleToken(humanClass.Name, isZombie: false)}: {FormatProgression(humanProgression.Level, humanProgression.XP, _config.HumanConfig.MaxLevel, GetRequiredHumanXpForNextLevel)}");
        Reply(command, $"Default human: {RoleToken(GetEffectivePreferredHuman(state).Name, isZombie: false)}. Use {ChatCommand(_config.CommandsConfig.Humans, "humans")} to list classes.");
    }

    private void OnZombieCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!TryGetPlayerState(player, command, out var state))
            return;

        if (command.ArgCount < 2 || IsListArgument(command.GetArg(1)))
        {
            PrintZombieList(command, state);
            return;
        }

        if (!TryResolveZombie(command.GetArg(1), out var zombie))
        {
            ReplyError(command, $"Unknown zombie class: {ArgToken(command.GetArg(1))}");
            PrintZombieList(command, state);
            return;
        }

        state.PreferredZombieType = zombie;
        GetZombieProgression(state, zombie);
        ReplyZombie(command, $"Default zombie set to {RoleToken(zombie.Name, isZombie: true)}. You will use it the next time you spawn as a zombie.");
        player?.PrintToCenterHtml($"<font color='#ff3d3d'>{zombie.Name}</font><br><font color='#ffffff'>Default zombie selected</font>", 4);
    }

    private void OnHumanCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!TryGetPlayerState(player, command, out var state))
            return;

        if (command.ArgCount < 2 || IsListArgument(command.GetArg(1)))
        {
            PrintHumanList(command, state);
            return;
        }

        if (!TryResolveHuman(command.GetArg(1), out var humanClass))
        {
            ReplyError(command, $"Unknown human class: {ArgToken(command.GetArg(1))}");
            PrintHumanList(command, state);
            return;
        }

        state.PreferredHumanClass = humanClass;
        GetHumanProgression(state, humanClass);
        ReplyHuman(command, $"Default human set to {RoleToken(humanClass.Name, isZombie: false)}. You will use it next round.");
        player?.PrintToCenterHtml($"<font color='#7fd7ff'>{humanClass.Name}</font><br><font color='#ffffff'>Default human selected</font>", 4);
    }

    private void PrintZombieList(CommandInfo command, PlayerState state)
    {
        ReplyHeader(command, "Available Zombies");
        Reply(command, $"Use {ChatCommand(_config.CommandsConfig.SwitchZombie, "zombie")} {ArgToken("<id>")} to set your default.");
        for (var i = 0; i < _config.ZombieConfig.ZombieTypes.Length; i++)
        {
            var zombie = _config.ZombieConfig.ZombieTypes[i];
            var progression = GetZombieProgression(state, zombie);
            var marker = GetZombieMarker(state, zombie);
            Reply(command,
                $"{IndexToken(i + 1)} {RoleToken(zombie.Name, isZombie: true)} {IdToken(zombie.Id)} HP {StatToken(zombie.Health)} SPD {StatToken(FormatStat(zombie.SpeedModifier))} DMG {StatToken(zombie.Damage)} - {FormatProgression(progression.Level, progression.XP, _config.ZombieConfig.MaxLevel, GetRequiredZombieXpForNextLevel)}{marker}");
        }
    }

    private void PrintHumanList(CommandInfo command, PlayerState state)
    {
        ReplyHeader(command, "Available Humans");
        Reply(command, $"Use {ChatCommand(_config.CommandsConfig.SwitchHuman, "human")} {ArgToken("<id>")} to set your default.");
        for (var i = 0; i < _config.HumanConfig.HumanClasses.Length; i++)
        {
            var humanClass = _config.HumanConfig.HumanClasses[i];
            var progression = GetHumanProgression(state, humanClass);
            var marker = GetHumanMarker(state, humanClass);
            var weaponCount = Math.Max(0, humanClass.DefaultWeapons?.Length ?? 0);
            Reply(command,
                $"{IndexToken(i + 1)} {RoleToken(humanClass.Name, isZombie: false)} {IdToken(humanClass.Id)} HP {StatToken(humanClass.Health)} SPD {StatToken(FormatStat(humanClass.SpeedModifier))} Weapons {StatToken(weaponCount)} - {FormatProgression(progression.Level, progression.XP, _config.HumanConfig.MaxLevel, GetRequiredHumanXpForNextLevel)}{marker}");
        }
    }

    private Zombie GetEffectiveZombie(PlayerState state)
    {
        return state.SelectedZombieType
            ?? state.PreferredZombieType
            ?? GetDefaultZombie();
    }

    private Zombie GetEffectivePreferredZombie(PlayerState state)
    {
        return state.PreferredZombieType ?? GetDefaultZombie();
    }

    private Zombie GetDefaultZombie()
    {
        return _config.ZombieConfig.ZombieTypes.FirstOrDefault(zombie =>
                string.Equals(zombie.Id, _config.ZombieConfig.DefaultZombieClassId, StringComparison.OrdinalIgnoreCase))
            ?? _config.ZombieConfig.ZombieTypes.First();
    }

    private HumanClass GetEffectiveHuman(PlayerState state)
    {
        return state.SelectedHumanClass
            ?? state.PreferredHumanClass
            ?? GetDefaultHuman();
    }

    private HumanClass GetEffectivePreferredHuman(PlayerState state)
    {
        return state.PreferredHumanClass ?? GetDefaultHuman();
    }

    private HumanClass GetDefaultHuman()
    {
        return _config.HumanConfig.HumanClasses.FirstOrDefault(human =>
                string.Equals(human.Id, _config.HumanConfig.DefaultHumanClassId, StringComparison.OrdinalIgnoreCase))
            ?? _config.HumanConfig.HumanClasses.First();
    }

    private ZombieProgression GetZombieProgression(PlayerState state, Zombie zombie)
    {
        if (!state.ZombieProgression.TryGetValue(zombie.Id, out var progression))
        {
            progression = new ZombieProgression
            {
                Level = _config.ZombieConfig.StartingLevel
            };
            state.ZombieProgression[zombie.Id] = progression;
        }

        progression.Level = Math.Max(1, progression.Level);
        progression.XP = Math.Max(0, progression.XP);
        return progression;
    }

    private HumanProgression GetHumanProgression(PlayerState state, HumanClass humanClass)
    {
        if (!state.HumanProgression.TryGetValue(humanClass.Id, out var progression))
        {
            progression = new HumanProgression
            {
                Level = _config.HumanConfig.StartingLevel
            };
            state.HumanProgression[humanClass.Id] = progression;
        }

        progression.Level = Math.Max(1, progression.Level);
        progression.XP = Math.Max(0, progression.XP);
        return progression;
    }

    private string GetZombieMarker(PlayerState state, Zombie zombie)
    {
        var markers = new List<string>();

        if (state.IsZombie && string.Equals(state.SelectedZombieType?.Id, zombie.Id, StringComparison.OrdinalIgnoreCase))
            markers.Add("current");

        if (string.Equals(GetEffectivePreferredZombie(state).Id, zombie.Id, StringComparison.OrdinalIgnoreCase))
            markers.Add("default");

        return markers.Count == 0
            ? string.Empty
            : $" {ChatColors.Gold}({string.Join(", ", markers)}){ChatColors.Default}";
    }

    private string GetHumanMarker(PlayerState state, HumanClass humanClass)
    {
        var markers = new List<string>();

        if (!state.IsZombie && string.Equals(state.SelectedHumanClass?.Id, humanClass.Id, StringComparison.OrdinalIgnoreCase))
            markers.Add("current");

        if (string.Equals(GetEffectivePreferredHuman(state).Id, humanClass.Id, StringComparison.OrdinalIgnoreCase))
            markers.Add("default");

        return markers.Count == 0
            ? string.Empty
            : $" {ChatColors.Gold}({string.Join(", ", markers)}){ChatColors.Default}";
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

    private bool TryGetPlayerState(
        CCSPlayerController? player,
        CommandInfo command,
        out PlayerState state)
    {
        state = null!;

        if (player == null || !player.IsValid)
        {
            ReplyError(command, "This command can only be used by a connected player.");
            return false;
        }

        state = player.GetState(_playerStates);
        return true;
    }

    private string FormatProgression(int level, int xp, int maxLevel, Func<int, int> getRequiredXp)
    {
        if (level >= maxLevel)
            return $"{ChatColors.Gold}Level {level} max{ChatColors.Default} ({ChatColors.Lime}{xp} XP saved{ChatColors.Default})";

        return $"{ChatColors.Gold}Level {level}{ChatColors.Default} {ChatColors.Lime}{xp}/{getRequiredXp(level)} XP{ChatColors.Default}";
    }

    private int GetRequiredZombieXpForNextLevel(int currentLevel)
    {
        return Math.Max(1, _config.ZombieConfig.XPPerLevel * Math.Max(1, currentLevel));
    }

    private int GetRequiredHumanXpForNextLevel(int currentLevel)
    {
        return Math.Max(1, _config.HumanConfig.XPPerLevel * Math.Max(1, currentLevel));
    }

    private static bool IsListArgument(string value)
    {
        return Normalize(value) is "help" or "list" or "show";
    }

    private static string ChatCommand(string configuredCommand, string fallback)
    {
        return CommandToken($"!{NormalizeCommandName(configuredCommand, fallback)}");
    }

    private void ReplyHeader(CommandInfo command, string text)
    {
        command.ReplyToCommand($"{ChatColors.Gold}=== {ChatColors.LightPurple}{text}{ChatColors.Gold} ==={ChatColors.Default}");
    }

    private void Reply(CommandInfo command, string message)
    {
        command.ReplyToCommand($"{ChatColors.LightPurple}[ZM]{ChatColors.Default} {message}");
    }

    private void ReplyZombie(CommandInfo command, string message)
    {
        command.ReplyToCommand($"{ChatColors.Red}{_config.ChatConfig.ZombiePrefix}{ChatColors.Default} {message}");
    }

    private void ReplyHuman(CommandInfo command, string message)
    {
        command.ReplyToCommand($"{ChatColors.LightBlue}{_config.ChatConfig.HumanPrefix}{ChatColors.Default} {message}");
    }

    private static void ReplyError(CommandInfo command, string message)
    {
        command.ReplyToCommand($"{ChatColors.Red}[ZM]{ChatColors.Default} {message}");
    }

    private static string CommandToken(string value)
    {
        return $"{ChatColors.Lime}{value}{ChatColors.Default}";
    }

    private static string ArgToken(string value)
    {
        return $"{ChatColors.Yellow}{value}{ChatColors.Default}";
    }

    private static string IdToken(string value)
    {
        return $"{ChatColors.Yellow}[{value}]{ChatColors.Default}";
    }

    private static string IndexToken(int value)
    {
        return $"{ChatColors.Gold}{value}.{ChatColors.Default}";
    }

    private static string RoleToken(string value, bool isZombie)
    {
        var color = isZombie ? ChatColors.Red : ChatColors.LightBlue;
        return $"{color}{value}{ChatColors.Default}";
    }

    private static string StatToken(object value)
    {
        return $"{ChatColors.Lime}{value}{ChatColors.Default}";
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

    private static string Normalize(string value)
    {
        return value
            .Trim()
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty)
            .ToLowerInvariant();
    }

    private static string FormatStat(float value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
