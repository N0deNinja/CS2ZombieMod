using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API;
using ZombieModPlugin.Configs;
using ZombieModPlugin.States;
using ZombieModPlugin.Extensions;

namespace ZombieModPlugin.Zombies.Handlers;

public class ZombieHandler
{
    private readonly Dictionary<ulong, PlayerState> _playerStates;
    private readonly BaseConfig _config;
    private readonly Random _random = new();

    public ZombieHandler(Dictionary<ulong, PlayerState> playerStates, BaseConfig config)
    {
        _playerStates = playerStates;
        _config = config;
    }

    public void OnBecomeZombie(CCSPlayerController player, PlayerState playerState)
    {
        var zombie = _config.ZombieConfig.ZombieTypes.FirstOrDefault();
        if (zombie == null)
            return;

        playerState.SelectedZombieType = zombie;

        player.PrintToChat($"{_config.ChatConfig.ZombiePrefix} You are now a {zombie.Name}!");
        Console.WriteLine($"[ZombieMod] {player.PlayerName} transformed into a zombie ({zombie.Name}).");

        var playerPawn = player.PlayerPawn.Value;
        if (playerPawn != null)
        {
            Server.NextFrame(() =>
                {
                    Server.NextFrame(() =>
                    {
                        var pawn = player.PlayerPawn?.Value;
                        if (pawn == null || !pawn.IsValid) return;

                        pawn.Health = zombie.Health;
                        pawn.MaxHealth = zombie.Health;
                        pawn.VelocityModifier = zombie.SpeedModifier;
                        pawn.GravityScale = zombie.Gravity;
                    });
                });
        }
    }

    public HookResult OnRoundStartInfectPlayer(EventRoundStart @event, GameEventInfo gameEventInfo)
    {
        Console.WriteLine($"[ZombieMod] Round started. Infection will begin in {_config.GeneralConfig.FirstInfectionDelaySeconds}s.");

        Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(_config.GeneralConfig.FirstInfectionDelaySeconds));

            Server.NextFrame(() =>
            {
                try
                {
                    Console.WriteLine("[ZombieMod] Infection delay complete. Selecting player...");

                    var players = Utilities.GetPlayers()
                        .Where(p => p.IsValid && !p.IsBot && p.Connected == PlayerConnectedState.PlayerConnected && p.PawnIsAlive)
                        .ToList();

                    Console.WriteLine($"[ZombieMod] Found {players.Count} candidates for infection.");

                    if (players.Count == 0)
                    {
                        Console.WriteLine("[ZombieMod] No valid players to infect.");
                        return;
                    }

                    var randomIndex = _random.Next(players.Count);
                    var chosen = players[randomIndex];

                    // Always ensure PlayerState exists
                    var state = chosen.GetState(_playerStates);

                    state.IsZombie = true;

                    chosen.PrintToChat($"{_config.ChatConfig.ZombiePrefix} You have been infected as the first zombie!");
                    Console.WriteLine($"[ZombieMod] {chosen.PlayerName} infected as the first zombie.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ZombieMod] ERROR during infection (main thread): {ex}");
                }
            });
        });

        return HookResult.Continue;
    }
}
