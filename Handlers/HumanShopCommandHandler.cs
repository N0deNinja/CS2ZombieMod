using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using ZombieModPlugin.Configs;
using ZombieModPlugin.Extensions;
using ZombieModPlugin.Progression.Services;
using ZombieModPlugin.Shops;
using ZombieModPlugin.States;

namespace ZombieModPlugin.Handlers;

public sealed class HumanShopCommandHandler
{
    private readonly Dictionary<ulong, PlayerState> _playerStates;
    private readonly BaseConfig _config;
    private readonly BasePlugin _plugin;
    private readonly ProgressionService _progressionService;
    private readonly HumanWeaponShopService _weaponShop;

    public HumanShopCommandHandler(
        Dictionary<ulong, PlayerState> playerStates,
        BaseConfig config,
        BasePlugin plugin,
        ProgressionService progressionService,
        HumanWeaponShopService weaponShop)
    {
        _playerStates = playerStates;
        _config = config;
        _plugin = plugin;
        _progressionService = progressionService;
        _weaponShop = weaponShop;
    }

    public void RegisterCommands()
    {
        var commands = _config.CommandsConfig;
        var registered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddCommand(registered, commands.Weapons, "weapons", "Show the human weapon shop.", OnWeaponsCommand);
        AddCommand(registered, commands.Guns, "guns", "Show the human weapon shop.", OnWeaponsCommand);
        AddCommand(registered, commands.Buy, "buy", "Buy a human weapon by id.", OnBuyCommand);
        AddCommand(registered, commands.Money, "money", "Show and refresh your money.", OnMoneyCommand);
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

    private void OnWeaponsCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!TryGetPlayerState(player, command, out var state))
            return;

        var category = string.Empty;
        var page = 1;

        if (command.ArgCount >= 2)
        {
            if (int.TryParse(command.GetArg(1), out var parsedPage))
            {
                page = parsedPage;
            }
            else
            {
                category = command.GetArg(1);
                if (command.ArgCount >= 3 && int.TryParse(command.GetArg(2), out var categoryPage))
                    page = categoryPage;
            }
        }

        _weaponShop.PrintMenu(command, state, Math.Max(1, page), category);
    }

    private void OnBuyCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!TryGetPlayerState(player, command, out var state))
            return;

        if (command.ArgCount < 2)
        {
            _weaponShop.PrintMenu(command, state, 1);
            return;
        }

        var result = _weaponShop.TryGiveWeapon(player!, state, command.GetArg(1));
        if (result.Success)
        {
            Reply(command, result.Message);
            player?.PrintToCenter(result.Message);
        }
        else
        {
            ReplyError(command, result.Message);
        }
    }

    private void OnMoneyCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!TryGetPlayerState(player, command, out var state))
            return;

        _progressionService.SyncNativeMoney(player!, state, save: true);
        _progressionService.ApplyInGameMoney(player!, state, save: true);
        Reply(command, $"Money: {ChatColors.Lime}${state.Money}{ChatColors.Default} ({ChatColors.Default}persistent{ChatColors.Default}). Earn it as a human, spend it in {ChatColors.Lime}!weapons{ChatColors.Default}.");
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

    private static void Reply(CommandInfo command, string message)
    {
        command.ReplyToCommand($"{ChatColors.LightPurple}[ZM]{ChatColors.Default} {message}");
    }

    private static void ReplyError(CommandInfo command, string message)
    {
        command.ReplyToCommand($"{ChatColors.Red}[ZM]{ChatColors.Default} {message}");
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
