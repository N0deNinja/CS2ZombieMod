using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Events;
using ZombieModPlugin.Configs;
using ZombieModPlugin.Extensions;
using ZombieModPlugin.States;
using ZombieModPlugin.Zombies.Handlers;
using ZombieModPlugin.Humans.Handlers;

namespace ZombieModPlugin.Handlers;

public class GeneralHandlers
{
    private readonly Dictionary<ulong, PlayerState> _playerStates;
    private readonly BaseConfig _config;
    private readonly ZombieHandler _zombieHandler;
    private readonly HumanHandler _humanHandler;

    public GeneralHandlers(
        Dictionary<ulong, PlayerState> playerStates,
        BaseConfig config,
        ZombieHandler zombieHandler,
        HumanHandler humanHandler)
    {
        _playerStates = playerStates;
        _config = config;
        _zombieHandler = zombieHandler;
        _humanHandler = humanHandler;
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

        state.OnZombieStateChanged += (playerState, isZombie) =>
        {
            if (isZombie)
                _zombieHandler.OnBecomeZombie(player, playerState);
            else
                _humanHandler.OnBecomeHuman(player, playerState);
        };

        player.PrintToChat($"Welcome, {player.PlayerName}!");
        Console.WriteLine($"[ZombieMod] Player {player.PlayerName} joined — PlayerState initialized.");

        return HookResult.Continue;
    }
}
