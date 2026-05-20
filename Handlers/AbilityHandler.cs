using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using ZombieModPlugin.Abilities;
using ZombieModPlugin.Abilities.Managers;
using ZombieModPlugin.Configs;
using ZombieModPlugin.Extensions;
using ZombieModPlugin.States;
using ZombieModPlugin.Zombies.Models;

namespace ZombieModPlugin.Handlers;

public class AbilityHandler
{
    private readonly Dictionary<ulong, PlayerState> _playerStates;
    private readonly BaseConfig _config;
    private readonly BasePlugin _plugin;
    private readonly AbilityManager _abilityManager;

    public AbilityHandler(
        Dictionary<ulong, PlayerState> playerStates,
        BaseConfig config,
        BasePlugin plugin,
        AbilityManager abilityManager)
    {
        _playerStates = playerStates;
        _config = config;
        _plugin = plugin;
        _abilityManager = abilityManager;
    }

    public void RegisterCommands()
    {
        _plugin.AddCommand("css_zability", "Use a zombie ability by id.", OnUseAbilityCommand);
        _plugin.AddCommand("css_zability_slot", "Use a zombie ability by slot number.", OnUseAbilitySlotCommand);
        _plugin.AddCommand($"css_{NormalizeCommandName(_config.CommandsConfig.Abilities, "abilities")}", "List or unlock zombie abilities.", OnAbilitiesCommand);
    }

    private void OnUseAbilityCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!TryGetPlayerState(player, command, out var state))
            return;

        if (command.ArgCount < 2)
        {
            command.ReplyToCommand("Usage: css_zability <ability_id>. Example: bind mouse4 \"css_zability pounce\"");
            return;
        }

        var requestedAbility = command.GetArg(1);
        if (!TryResolveRegisteredAbility(requestedAbility, out var type, out _))
        {
            command.ReplyToCommand($"Unknown or unimplemented ability: {requestedAbility}");
            return;
        }

        ActivateAbility(type, player!, state);
    }

    private void OnUseAbilitySlotCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!TryGetPlayerState(player, command, out var state))
            return;

        if (command.ArgCount < 2 || !int.TryParse(command.GetArg(1), out var slot) || slot < 1)
        {
            command.ReplyToCommand("Usage: css_zability_slot <slot>. Example: bind mouse5 \"css_zability_slot 1\"");
            return;
        }

        if (!TryGetSelectedZombie(state, command, out var zombie, out var progression))
            return;

        var loadout = GetUsableAbilities(zombie, progression).ToList();
        if (loadout.Count == 0)
        {
            command.ReplyToCommand("You do not have any usable abilities for this zombie type yet.");
            return;
        }

        if (slot > loadout.Count)
        {
            command.ReplyToCommand($"Ability slot {slot} is empty. You currently have {loadout.Count} usable ability slot(s).");
            return;
        }

        ActivateAbility(loadout[slot - 1], player!, state);
    }

    private void OnAbilitiesCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!TryGetPlayerState(player, command, out var state))
            return;

        if (!TryGetSelectedZombie(state, command, out var zombie, out var progression))
            return;

        if (command.ArgCount < 2 || IsListArgument(command.GetArg(1)))
        {
            PrintAbilityList(command, zombie, progression);
            return;
        }

        UnlockAbility(command.GetArg(1), command, zombie, progression);
    }

    private void ActivateAbility(AbilityType type, CCSPlayerController player, PlayerState state)
    {
        var context = new AbilityExecutionContext
        {
            Player = player,
            PlayerState = state,
            Plugin = _plugin,
            Config = _config,
            ServerTime = DateTime.Now,
            AllPlayers = Utilities.GetPlayers()
                .Where(p => p.IsValid)
                .ToList()
        };

        _abilityManager.TryActivateAbility(type, context);
    }

    private void UnlockAbility(
        string requestedAbility,
        CommandInfo command,
        Zombie zombie,
        ZombieProgression progression)
    {
        if (!TryResolveAbilityType(requestedAbility, out var type))
        {
            command.ReplyToCommand(_config.MessagesConfig.InvalidAbility);
            return;
        }

        if (zombie.DefaultAbilities.Contains(type))
        {
            command.ReplyToCommand($"{GetAbilityDisplayName(type)} is already available by default for {zombie.Name}.");
            return;
        }

        if (!zombie.UnlockableAbilities.Contains(type))
        {
            command.ReplyToCommand($"{GetAbilityDisplayName(type)} is not unlockable by {zombie.Name}.");
            return;
        }

        if (progression.UnlockedAbilities.Contains(type))
        {
            command.ReplyToCommand($"{GetAbilityDisplayName(type)} is already unlocked.");
            return;
        }

        var ability = AbilityRegistry.Get(type);
        if (ability == null)
        {
            command.ReplyToCommand($"{GetAbilityDisplayName(type)} is configured but not implemented yet.");
            return;
        }

        var totalAbilityCount = zombie.DefaultAbilities.Length + progression.UnlockedAbilities.Count;
        if (totalAbilityCount >= _config.ZombieConfig.MaxAbilitiesPerZombie)
        {
            command.ReplyToCommand(_config.MessagesConfig.MaxAbilitiesReached);
            return;
        }

        if (progression.XP < ability.UnlockCost)
        {
            command.ReplyToCommand(_config.MessagesConfig.NotEnoughExp);
            return;
        }

        progression.XP -= ability.UnlockCost;
        progression.UnlockedAbilities.Add(type);

        command.ReplyToCommand(string.Format(_config.MessagesConfig.AbilityUnlocked, ability.Name));
    }

    private void PrintAbilityList(CommandInfo command, Zombie zombie, ZombieProgression progression)
    {
        command.ReplyToCommand($"{_config.MessagesConfig.ShopHeader} {zombie.Name}");
        command.ReplyToCommand($"XP: {progression.XP} | Bind example: bind mouse4 \"css_zability pounce\"");

        var usableAbilities = GetUsableAbilities(zombie, progression).ToList();
        if (usableAbilities.Count == 0)
        {
            command.ReplyToCommand("No implemented abilities are currently usable for this zombie type.");
        }

        for (var slot = 0; slot < usableAbilities.Count; slot++)
        {
            var ability = AbilityRegistry.Get(usableAbilities[slot])!;

            command.ReplyToCommand($"Slot {slot + 1}: {ability.Name} [{ability.Id}]");
        }

        foreach (var type in zombie.UnlockableAbilities)
        {
            if (progression.UnlockedAbilities.Contains(type))
                continue;

            var ability = AbilityRegistry.Get(type);
            var label = ability == null
                ? $"{GetAbilityDisplayName(type)} (not implemented)"
                : $"{ability.Name} [{ability.Id}] - Cost: {ability.UnlockCost} XP";

            command.ReplyToCommand($"Unlockable: {label}");
        }
    }

    private IEnumerable<AbilityType> GetUsableAbilities(Zombie zombie, ZombieProgression progression)
    {
        IEnumerable<AbilityType> configuredLoadout = progression.ActiveAbilities.Count > 0
            ? progression.ActiveAbilities
            : zombie.DefaultAbilities.Concat(progression.UnlockedAbilities);

        return configuredLoadout
            .Distinct()
            .Where(type => AbilityRegistry.Get(type) != null);
    }

    private bool TryGetPlayerState(
        CCSPlayerController? player,
        CommandInfo command,
        out PlayerState state)
    {
        state = null!;

        if (player == null || !player.IsValid)
        {
            command.ReplyToCommand("This command can only be used by a connected player.");
            return false;
        }

        state = player.GetState(_playerStates);
        return true;
    }

    private bool TryGetSelectedZombie(
        PlayerState state,
        CommandInfo command,
        out Zombie zombie,
        out ZombieProgression progression)
    {
        zombie = null!;
        progression = null!;

        if (!state.IsZombie || state.SelectedZombieType == null)
        {
            command.ReplyToCommand($"{_config.ChatConfig.ZombiePrefix} You need to be a zombie to use zombie abilities.");
            return false;
        }

        zombie = state.SelectedZombieType;
        if (!state.ZombieProgression.TryGetValue(zombie.Id, out progression!))
        {
            progression = new ZombieProgression();
            state.ZombieProgression[zombie.Id] = progression;
        }

        return true;
    }

    private static bool TryResolveRegisteredAbility(string value, out AbilityType type, out Ability? ability)
    {
        foreach (var entry in AbilityRegistry.Abilities)
        {
            if (AbilityMatches(value, entry.Key, entry.Value))
            {
                type = entry.Key;
                ability = entry.Value;
                return true;
            }
        }

        type = default;
        ability = null;
        return false;
    }

    private static bool TryResolveAbilityType(string value, out AbilityType type)
    {
        foreach (var candidate in Enum.GetValues<AbilityType>())
        {
            if (AbilityMatches(value, candidate, AbilityRegistry.Get(candidate)))
            {
                type = candidate;
                return true;
            }
        }

        type = default;
        return false;
    }

    private static bool AbilityMatches(string value, AbilityType type, Ability? ability)
    {
        var normalized = NormalizeAbilityName(value);

        return normalized == NormalizeAbilityName(type.ToString())
            || (ability != null
                && (normalized == NormalizeAbilityName(ability.Id)
                    || normalized == NormalizeAbilityName(ability.Name)));
    }

    private static string GetAbilityDisplayName(AbilityType type)
    {
        return AbilityRegistry.Get(type)?.Name ?? type.ToString();
    }

    private static bool IsListArgument(string value)
    {
        var normalized = NormalizeAbilityName(value);
        return normalized is "list" or "show" or "help";
    }

    private static string NormalizeAbilityName(string value)
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
