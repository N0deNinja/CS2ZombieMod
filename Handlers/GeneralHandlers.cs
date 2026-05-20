using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Events;
using ZombieModPlugin.Configs;
using ZombieModPlugin.Extensions;
using ZombieModPlugin.States;

namespace ZombieModPlugin.Handlers;

public class GeneralHandlers
{
    private readonly Dictionary<ulong, PlayerState> _playerStates;
    private readonly BaseConfig _config;

    public GeneralHandlers(
        Dictionary<ulong, PlayerState> playerStates,
        BaseConfig config)
    {
        _playerStates = playerStates;
        _config = config;
    }

    public HookResult OnPlayerConnectFullInitState(EventPlayerConnectFull @event, GameEventInfo gameEventInfo)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid)
            return HookResult.Continue;

        var state = player.GetState(_playerStates);

        state.IsZombie = false;
        state.SelectedZombieType = null;
        state.GlobalCooldowns.Clear();

        foreach (var zombieType in _config.ZombieConfig.ZombieTypes)
        {
            if (!state.ZombieProgression.ContainsKey(zombieType.Id))
                state.ZombieProgression[zombieType.Id] = new ZombieProgression();
        }

        player.PrintToChat($"Welcome, {player.PlayerName}!");
        Console.WriteLine($"[ZombieMod] Player {player.PlayerName} joined - PlayerState initialized.");

        return HookResult.Continue;
    }
}
