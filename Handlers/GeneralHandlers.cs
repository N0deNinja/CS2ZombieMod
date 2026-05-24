using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Utils;
using ZombieModPlugin.Configs;
using ZombieModPlugin.Extensions;
using ZombieModPlugin.Formatting;
using ZombieModPlugin.Progression.Services;
using ZombieModPlugin.States;

namespace ZombieModPlugin.Handlers;

public class GeneralHandlers
{
    private readonly Dictionary<ulong, PlayerState> _playerStates;
    private readonly BaseConfig _config;
    private readonly ProgressionService _progressionService;

    public GeneralHandlers(
        Dictionary<ulong, PlayerState> playerStates,
        BaseConfig config,
        ProgressionService progressionService)
    {
        _playerStates = playerStates;
        _config = config;
        _progressionService = progressionService;
    }

    public HookResult OnPlayerConnectFullInitState(EventPlayerConnectFull @event, GameEventInfo gameEventInfo)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid)
            return HookResult.Continue;

        var state = player.GetState(_playerStates);

        state.IsZombie = false;
        state.SelectedZombieType = null;
        state.SelectedHumanClass = null;
        state.GlobalCooldowns.Clear();

        foreach (var zombieType in _config.ZombieConfig.ZombieTypes)
        {
            if (!state.ZombieProgression.ContainsKey(zombieType.Id))
                state.ZombieProgression[zombieType.Id] = new ZombieProgression();
        }

        foreach (var humanClass in _config.HumanConfig.HumanClasses)
        {
            if (!state.HumanProgression.ContainsKey(humanClass.Id))
                state.HumanProgression[humanClass.Id] = new HumanProgression();
        }

        _progressionService.BeginLoadPlayer(player, state);
        ScheduleAutoAssignToCounterTerrorist(player);

        player.PrintToChat(ChatText.Info($"Welcome, {ChatText.Name(player.PlayerName)}!"));
        player.PrintToChat(ChatText.Info($"Type {ChatText.Command("!help")} or {ChatText.Command("!shop")} for Zombie Mod progression."));
        Console.WriteLine($"[ZombieMod] Player {player.PlayerName} joined - PlayerState initialized.");

        return HookResult.Continue;
    }

    private static void ScheduleAutoAssignToCounterTerrorist(CCSPlayerController player)
    {
        Server.NextFrame(() => AutoAssignToCounterTerrorist(player));

        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)));
                Server.NextFrame(() => AutoAssignToCounterTerrorist(player));
            }
        });
    }

    private static void AutoAssignToCounterTerrorist(CCSPlayerController player)
    {
        if (!player.IsValid || player.Connected != PlayerConnectedState.Connected)
            return;

        if (player.Team != CsTeam.CounterTerrorist)
            player.SwitchTeam(CsTeam.CounterTerrorist);

        player.ForceTeamState(CsTeam.CounterTerrorist);

        if (!player.PawnIsAlive)
            player.Respawn();
    }
}
