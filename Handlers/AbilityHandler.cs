using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using ZombieModPlugin.Abilities;
using ZombieModPlugin.Abilities.Managers;
using ZombieModPlugin.Configs;
using ZombieModPlugin.Extensions;
using ZombieModPlugin.Formatting;
using ZombieModPlugin.Progression.Services;
using ZombieModPlugin.States;
using ZombieModPlugin.Zombies.Models;

namespace ZombieModPlugin.Handlers;

public class AbilityHandler
{
    private readonly Dictionary<ulong, PlayerState> _playerStates;
    private readonly BaseConfig _config;
    private readonly BasePlugin _plugin;
    private readonly AbilityManager _abilityManager;
    private readonly ProgressionService _progressionService;

    public AbilityHandler(
        Dictionary<ulong, PlayerState> playerStates,
        BaseConfig config,
        BasePlugin plugin,
        AbilityManager abilityManager,
        ProgressionService progressionService)
    {
        _playerStates = playerStates;
        _config = config;
        _plugin = plugin;
        _abilityManager = abilityManager;
        _progressionService = progressionService;
    }

    public void RegisterCommands()
    {
        _plugin.AddCommand("css_zability", "Use a zombie ability by id or slot number.", OnUseAbilityCommand);
        _plugin.AddCommand("css_zability_slot", "Use a zombie ability by slot number.", OnUseAbilitySlotCommand);
    }

    private void OnUseAbilityCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!TryGetPlayerState(player, command, out var state))
            return;

        if (command.ArgCount < 2)
        {
            Reply(command, $"Usage: {ChatText.Command("css_zability <slot|ability_id>")} Example: bind mouse4 \"css_zability 1\"");
            return;
        }

        var requestedAbility = command.GetArg(1);
        if (int.TryParse(requestedAbility, out var slot))
        {
            ActivateAbilitySlot(slot, player!, state, command);
            return;
        }

        if (!TryResolveRegisteredAbility(requestedAbility, out var type, out _))
        {
            ReplyError(command, $"Unknown or unimplemented ability: {ChatColors.Yellow}{requestedAbility}{ChatColors.Default}");
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
            Reply(command, $"Usage: {ChatText.Command("css_zability_slot <slot>")} Example: bind mouse5 \"css_zability_slot 1\"");
            return;
        }

        ActivateAbilitySlot(slot, player!, state, command);
    }

    private void ActivateAbilitySlot(int slot, CCSPlayerController player, PlayerState state, CommandInfo command)
    {
        if (slot < 1)
        {
            ReplyError(command, "Ability slots start at 1.");
            return;
        }

        if (!TryGetSelectedZombie(state, command, out var zombie, out var progression))
            return;

        var loadout = _progressionService.GetUsableAbilities(state, zombie).ToList();
        if (loadout.Count == 0)
        {
            ReplyError(command, "You do not have any usable abilities for this zombie type yet.");
            return;
        }

        if (slot > loadout.Count)
        {
            ReplyError(command, $"Ability slot {ChatText.Number(slot)} is empty. You currently have {ChatText.Number(loadout.Count)} usable ability slot(s).");
            return;
        }

        ActivateAbility(loadout[slot - 1], player, state);
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
            PlayerStates = _playerStates,
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
            ReplyError(command, _config.MessagesConfig.InvalidAbility);
            return;
        }

        if (zombie.DefaultAbilities.Contains(type))
        {
            Reply(command, $"{ChatColors.Gold}{GetAbilityDisplayName(type)}{ChatColors.Default} is already available by default for {ChatText.Name(zombie.Name)}.");
            return;
        }

        if (!zombie.UnlockableAbilities.Contains(type))
        {
            ReplyError(command, $"{ChatColors.Gold}{GetAbilityDisplayName(type)}{ChatColors.Default} is not unlockable by {ChatText.Name(zombie.Name)}.");
            return;
        }

        if (progression.UnlockedAbilities.Contains(type))
        {
            Reply(command, $"{ChatColors.Gold}{GetAbilityDisplayName(type)}{ChatColors.Default} is already unlocked.");
            return;
        }

        var ability = AbilityRegistry.Get(type);
        if (ability == null)
        {
            ReplyError(command, $"{ChatColors.Gold}{GetAbilityDisplayName(type)}{ChatColors.Default} is configured but not implemented yet.");
            return;
        }

        var totalAbilityCount = zombie.DefaultAbilities.Length + progression.UnlockedAbilities.Count;
        if (totalAbilityCount >= _config.ZombieConfig.MaxAbilitiesPerZombie)
        {
            ReplyError(command, _config.MessagesConfig.MaxAbilitiesReached);
            return;
        }

        var unlockCost = _config.AbilityConfig.GetSettings(type).UnlockCost;
        if (progression.XP < unlockCost)
        {
            ReplyError(command, _config.MessagesConfig.NotEnoughExp);
            return;
        }

        progression.XP -= unlockCost;
        progression.UnlockedAbilities.Add(type);

        Reply(command, string.Format(_config.MessagesConfig.AbilityUnlocked, $"{ChatColors.Gold}{ability.Name}{ChatColors.Default}"));
    }

    private void PrintAbilityList(CommandInfo command, Zombie zombie, ZombieProgression progression)
    {
        command.ReplyToCommand($"{ChatColors.Gold}======== {ChatColors.LightPurple}Ability Shop{ChatColors.Gold} ========{ChatColors.Default}");
        Reply(command, $"{ChatText.Name(zombie.Name)} | XP: {ChatColors.Lime}{progression.XP}{ChatColors.Default} | bind mouse4 \"css_zability 1\"");

        var usableAbilities = GetUsableAbilities(zombie, progression).ToList();
        if (usableAbilities.Count == 0)
        {
            ReplyError(command, "No implemented abilities are currently usable for this zombie type.");
        }

        for (var slot = 0; slot < usableAbilities.Count; slot++)
        {
            var ability = AbilityRegistry.Get(usableAbilities[slot])!;

            Reply(command, $"Slot {ChatText.Number(slot + 1)}: {ChatColors.Gold}{ability.Name}{ChatColors.Default} {ChatColors.Yellow}[{ability.Id}]{ChatColors.Default}");
        }

        foreach (var type in zombie.UnlockableAbilities)
        {
            if (progression.UnlockedAbilities.Contains(type))
                continue;

            var ability = AbilityRegistry.Get(type);
            var label = ability == null
                ? $"{GetAbilityDisplayName(type)} (not implemented)"
                : $"{ability.Name} [{ability.Id}] - Cost: {_config.AbilityConfig.GetSettings(type).UnlockCost} XP";

            Reply(command, $"Unlockable: {label}");
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
            ReplyError(command, "This command can only be used by a connected player.");
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
            command.ReplyToCommand(ChatText.Zombie("You need to be a zombie to use zombie abilities."));
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

    private static void Reply(CommandInfo command, string message)
    {
        command.ReplyToCommand(ChatText.Info(message));
    }

    private static void ReplyError(CommandInfo command, string message)
    {
        command.ReplyToCommand(ChatText.Error(message));
    }
}
