using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using ZombieModPlugin.Abilities;
using ZombieModPlugin.Configs;
using ZombieModPlugin.Extensions;
using ZombieModPlugin.Progression.Menus;
using ZombieModPlugin.Progression.Models;
using ZombieModPlugin.Progression.Services;
using ZombieModPlugin.States;

namespace ZombieModPlugin.Handlers;

public sealed class ProgressionCommandHandler
{
    private readonly Dictionary<ulong, PlayerState> _playerStates;
    private readonly BaseConfig _config;
    private readonly BasePlugin _plugin;
    private readonly ProgressionService _progressionService;
    private readonly ProgressionMenuRenderer _menuRenderer;

    public ProgressionCommandHandler(
        Dictionary<ulong, PlayerState> playerStates,
        BaseConfig config,
        BasePlugin plugin,
        ProgressionService progressionService,
        ProgressionMenuRenderer menuRenderer)
    {
        _playerStates = playerStates;
        _config = config;
        _plugin = plugin;
        _progressionService = progressionService;
        _menuRenderer = menuRenderer;
    }

    public void RegisterCommands()
    {
        var commands = _config.CommandsConfig;
        var registered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddCommand(registered, commands.Help, "help", "Show Zombie Mod player commands.", OnHelpCommand);
        AddCommand(registered, commands.XP, "xp", "Show your progression overview.", OnXpCommand);
        AddCommand(registered, commands.Level, "level", "Show your progression overview.", OnXpCommand);
        AddCommand(registered, commands.Shop, "shop", "Open the progression shop menu.", OnShopCommand);
        AddCommand(registered, commands.Progression, "progression", "Open the progression menu.", OnShopCommand);
        AddCommand(registered, commands.Stats, "stats", "Show XP and stat overview.", OnXpCommand);
        AddCommand(registered, commands.Zombies, "zombies", "List, unlock, or select zombie classes.", OnZombieCommand);
        AddCommand(registered, commands.SwitchZombie, "zombie", "Select your default zombie class.", OnZombieCommand);
        AddCommand(registered, commands.DefaultZombie, "zdefault", "Select your default zombie class.", OnZombieCommand);
        AddCommand(registered, commands.Humans, "humans", "List, unlock, or select human classes.", OnHumanCommand);
        AddCommand(registered, commands.SwitchHuman, "human", "Select your default human class.", OnHumanCommand);
        AddCommand(registered, commands.DefaultHuman, "hdefault", "Select your default human class.", OnHumanCommand);
        AddCommand(registered, commands.Abilities, "abilities", "List, unlock, and equip abilities.", OnAbilitiesCommand);
        AddCommand(registered, commands.Bind, "bind", "Bind an ability slot to a key helper.", OnBindCommand);
    }

    private void AddCommand(
        HashSet<string> registered,
        string configuredCommand,
        string fallback,
        string description,
        CommandInfo.CommandCallback handler)
    {
        var commandName = NormalizeCommandName(configuredCommand, fallback);
        if (registered.Add(commandName))
            _plugin.AddCommand($"css_{commandName}", description, handler);
    }

    private void OnHelpCommand(CCSPlayerController? player, CommandInfo command)
    {
        Header(command, "Zombie Mod Commands");
        Reply(command, $"{Command("!shop")} main progression menu.");
        Reply(command, $"{Command("!xp")} XP, levels, and stats.");
        Reply(command, $"{Command("!zombies")} list zombie classes. {Command("!zombies unlock brute")} unlocks when ready.");
        Reply(command, $"{Command("!humans")} list human classes. {Command("!human hunter")} selects a default.");
        Reply(command, $"{Command("!abilities")} ability unlocks. {Command("!abilities equip pounce 1")} sets a loadout slot.");
        Reply(command, $"{Command("!bind 1 mouse4")} saves a slot bind helper and prints the console bind.");
        Reply(command, $"{Command("!weapons")} human weapon shop. {Command("!buy awp")} buys any human weapon.");
        Reply(command, $"{Command("!money")} refreshes your unlimited/persistent money.");
        Reply(command, $"{Command("!block")} place a blockade. {Command("!block small")} places a smaller one.");
        Reply(command, $"{Command("css_zability 1")} uses your first equipped zombie ability.");
    }

    private void OnShopCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!TryGetPlayerState(player, command, out var state))
            return;

        if (command.ArgCount < 2)
        {
            _menuRenderer.PrintMain(command, state);
            return;
        }

        var action = Normalize(command.GetArg(1));
        var page = ParsePage(command, 2);
        switch (action)
        {
            case "zombie":
            case "zombies":
                _menuRenderer.PrintZombieClasses(command, state, page);
                break;

            case "human":
            case "humans":
                _menuRenderer.PrintHumanClasses(command, state, page);
                break;

            case "ability":
            case "abilities":
                _menuRenderer.PrintAbilities(command, state, page);
                break;

            case "equip":
            case "loadout":
                _menuRenderer.PrintEquipMenu(command, state);
                break;

            case "weapons":
            case "guns":
            case "buy":
                Reply(command, $"Human weapons: {Command("!weapons")} for the shop or {Command("!buy awp")} for a quick buy.");
                break;

            case "stats":
            case "xp":
            case "overview":
                _menuRenderer.PrintOverview(command, state);
                break;

            default:
                _menuRenderer.PrintMain(command, state);
                break;
        }
    }

    private void OnBindCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!TryGetPlayerState(player, command, out var state))
            return;

        if (command.ArgCount < 3)
        {
            _menuRenderer.PrintEquipMenu(command, state);
            Reply(command, $"Usage: {Command("!bind <slot> <key>")} Example: {Command("!bind 1 mouse4")}");
            return;
        }

        if (!int.TryParse(command.GetArg(1), out var slot))
        {
            ReplyError(command, "Slot must be a number. Example: !bind 1 mouse4");
            return;
        }

        var maxSlots = _progressionService.GetCurrentRole(state) == ProgressionClassRole.Zombie
            ? Math.Max(1, _config.ProgressionConfig.MaxEquippedZombieAbilities)
            : Math.Max(1, _config.ProgressionConfig.MaxEquippedHumanAbilities);
        if (slot < 1 || slot > maxSlots)
        {
            ReplyError(command, $"Slot must be between 1 and {maxSlots} for your current class.");
            return;
        }

        var keyName = command.GetArg(2).Trim();
        if (!IsSafeBindKey(keyName))
        {
            ReplyError(command, "Key name can only use letters, numbers, underscore, dash, mouse, mwheel, alt, ctrl, and shift style names.");
            return;
        }

        var result = _progressionService.SaveAbilityBind(player!, state, slot, keyName);
        ReplyResult(command, result);

        if (result.Success)
        {
            var bindCommand = $"bind {keyName} \"css_zability {slot}\"";
            player!.ExecuteClientCommandFromServer(bindCommand);
            Reply(command, $"If CS2 blocks server-side binds, paste this in console: {Command(bindCommand)}");
            player.PrintToCenter($"Ability slot {slot}: {keyName}");
        }
    }

    private void OnXpCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (TryGetPlayerState(player, command, out var state))
            _menuRenderer.PrintOverview(command, state);
    }

    private void OnZombieCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!TryGetPlayerState(player, command, out var state))
            return;

        if (command.ArgCount < 2 || IsList(command.GetArg(1)))
        {
            _menuRenderer.PrintZombieClasses(command, state, ParsePage(command, 1));
            return;
        }

        if (int.TryParse(command.GetArg(1), out var requestedPage) && IsPluralCommand(command, "zombies"))
        {
            _menuRenderer.PrintZombieClasses(command, state, Math.Max(1, requestedPage));
            return;
        }

        var action = Normalize(command.GetArg(1));
        if (action is "unlock" or "buy")
        {
            if (command.ArgCount < 3)
            {
                ReplyError(command, "Usage: !zombies unlock <id>");
                return;
            }

            ReplyResult(command, _progressionService.TryUnlockClass(player!, state, ProgressionClassRole.Zombie, command.GetArg(2)));
            return;
        }

        var result = _progressionService.SetPreferredClass(player!, state, ProgressionClassRole.Zombie, command.GetArg(1));
        ReplyResult(command, result);
        if (result.Success)
            player?.PrintToCenterHtml($"<font color='#ff3d3d'>{_progressionService.GetPreferredZombie(state).Name}</font><br><font color='#ffffff'>Default zombie selected</font>", 4);
    }

    private void OnHumanCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!TryGetPlayerState(player, command, out var state))
            return;

        if (command.ArgCount < 2 || IsList(command.GetArg(1)))
        {
            _menuRenderer.PrintHumanClasses(command, state, ParsePage(command, 1));
            return;
        }

        if (int.TryParse(command.GetArg(1), out var requestedPage) && IsPluralCommand(command, "humans"))
        {
            _menuRenderer.PrintHumanClasses(command, state, Math.Max(1, requestedPage));
            return;
        }

        var action = Normalize(command.GetArg(1));
        if (action is "unlock" or "buy")
        {
            if (command.ArgCount < 3)
            {
                ReplyError(command, "Usage: !humans unlock <id>");
                return;
            }

            ReplyResult(command, _progressionService.TryUnlockClass(player!, state, ProgressionClassRole.Human, command.GetArg(2)));
            return;
        }

        var result = _progressionService.SetPreferredClass(player!, state, ProgressionClassRole.Human, command.GetArg(1));
        ReplyResult(command, result);
        if (result.Success)
            player?.PrintToCenterHtml($"<font color='#7fd7ff'>{_progressionService.GetPreferredHuman(state).Name}</font><br><font color='#ffffff'>Default human selected</font>", 4);
    }

    private void OnAbilitiesCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!TryGetPlayerState(player, command, out var state))
            return;

        if (command.ArgCount < 2 || IsList(command.GetArg(1)))
        {
            _menuRenderer.PrintAbilities(command, state, ParsePage(command, 1));
            return;
        }

        if (int.TryParse(command.GetArg(1), out var requestedPage))
        {
            _menuRenderer.PrintAbilities(command, state, Math.Max(1, requestedPage));
            return;
        }

        var action = Normalize(command.GetArg(1));
        var role = _progressionService.GetCurrentRole(state);
        var classId = _progressionService.GetCurrentClassId(state);

        switch (action)
        {
            case "equip":
            case "slot":
                EquipAbility(player!, state, command, role, classId);
                break;

            case "unequip":
            case "remove":
                UnequipAbility(player!, state, command, role, classId);
                break;

            case "unlock":
            case "buy":
                UnlockAbility(player!, state, command, role, classId, argIndex: 2);
                break;

            case "loadout":
            case "equipped":
                _menuRenderer.PrintEquipMenu(command, state);
                break;

            default:
                UnlockAbility(player!, state, command, role, classId, argIndex: 1);
                break;
        }
    }

    private void UnlockAbility(
        CCSPlayerController player,
        PlayerState state,
        CommandInfo command,
        ProgressionClassRole role,
        string classId,
        int argIndex)
    {
        if (command.ArgCount <= argIndex || !TryResolveAbility(command.GetArg(argIndex), out var ability))
        {
            ReplyError(command, "Usage: !abilities unlock <ability_id>");
            return;
        }

        ReplyResult(command, _progressionService.TryUnlockAbility(player, state, role, classId, ability));
    }

    private void EquipAbility(
        CCSPlayerController player,
        PlayerState state,
        CommandInfo command,
        ProgressionClassRole role,
        string classId)
    {
        if (command.ArgCount < 3 || !TryResolveAbility(command.GetArg(2), out var ability))
        {
            ReplyError(command, "Usage: !abilities equip <ability_id> [slot]");
            return;
        }

        var slot = 1;
        if (command.ArgCount >= 4 && int.TryParse(command.GetArg(3), out var parsedSlot))
            slot = parsedSlot;

        ReplyResult(command, _progressionService.TryEquipAbility(player, state, role, classId, ability, slot));
    }

    private void UnequipAbility(
        CCSPlayerController player,
        PlayerState state,
        CommandInfo command,
        ProgressionClassRole role,
        string classId)
    {
        if (command.ArgCount < 3 || !int.TryParse(command.GetArg(2), out var slot))
        {
            ReplyError(command, "Usage: !abilities unequip <slot>");
            return;
        }

        ReplyResult(command, _progressionService.UnequipAbility(player, state, role, classId, slot));
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

    private static void ReplyResult(CommandInfo command, UnlockAttemptResult result)
    {
        if (result.Success)
            Reply(command, result.Message);
        else
            ReplyError(command, result.Message);
    }

    private static int ParsePage(CommandInfo command, int argIndex)
    {
        if (command.ArgCount > argIndex && int.TryParse(command.GetArg(argIndex), out var page))
            return Math.Max(1, page);

        return 1;
    }

    private static bool IsList(string value)
    {
        return Normalize(value) is "help" or "list" or "show" or "menu";
    }

    private static bool IsPluralCommand(CommandInfo command, string pluralName)
    {
        return command.ArgCount > 0
            && Normalize(command.GetArg(0)).EndsWith(Normalize(pluralName), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSafeBindKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 32)
            return false;

        return value.All(character =>
            char.IsAsciiLetterOrDigit(character)
            || character is '_' or '-');
    }

    private static void Header(CommandInfo command, string text)
    {
        command.ReplyToCommand($"{ChatColors.Gold}======== {ChatColors.LightPurple}{text}{ChatColors.Gold} ========{ChatColors.Default}");
    }

    private static void Reply(CommandInfo command, string message)
    {
        command.ReplyToCommand($"{ChatColors.LightPurple}[ZM]{ChatColors.Default} {message}");
    }

    private static void ReplyError(CommandInfo command, string message)
    {
        command.ReplyToCommand($"{ChatColors.Red}[ZM]{ChatColors.Default} {message}");
    }

    private static string Command(string command)
    {
        return $"{ChatColors.Lime}{command}{ChatColors.Default}";
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
}
