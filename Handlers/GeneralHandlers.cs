using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Utils;
using ZombieModPlugin.Configs;
using ZombieModPlugin.Diagnostics;
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

        CrashBreadcrumbs.Log($"connect init state start {CrashBreadcrumbs.DescribePlayer(player)}");
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

        CrashBreadcrumbs.Log($"progression BeginLoadPlayer call start {CrashBreadcrumbs.DescribePlayer(player)}");
        _progressionService.BeginLoadPlayer(player, state);
        CrashBreadcrumbs.Log($"progression BeginLoadPlayer call end {CrashBreadcrumbs.DescribePlayer(player)}");
        CrashBreadcrumbs.Log($"auto team schedule start {CrashBreadcrumbs.DescribePlayer(player)}");
        ScheduleAutoAssignToCounterTerrorist(player);
        CrashBreadcrumbs.Log($"auto team schedule end {CrashBreadcrumbs.DescribePlayer(player)}");

        player.PrintToChat(ChatText.Info($"Welcome, {ChatText.Name(player.PlayerName)}!"));
        player.PrintToChat(ChatText.Info($"Type {ChatText.Command("!help")} or {ChatText.Command("!shop")} for Zombie Mod progression."));
        Console.WriteLine($"[ZombieMod] Player {player.PlayerName} joined - PlayerState initialized.");
        CrashBreadcrumbs.Log($"connect init state end {CrashBreadcrumbs.DescribePlayer(player)}");

        return HookResult.Continue;
    }

    private static void ScheduleAutoAssignToCounterTerrorist(CCSPlayerController player)
    {
        CrashBreadcrumbs.SafeNextFrame("auto team immediate", () => AutoAssignToCounterTerrorist(player));

        _ = Task.Run(async () =>
        {
            try
            {
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)));
                    var capturedAttempt = attempt + 1;
                    CrashBreadcrumbs.SafeNextFrame($"auto team retry attempt={capturedAttempt}", () => AutoAssignToCounterTerrorist(player));
                }
            }
            catch (Exception ex)
            {
                CrashBreadcrumbs.LogException($"auto team retry task {CrashBreadcrumbs.DescribePlayer(player)}", ex);
            }
        });
    }

    private static void AutoAssignToCounterTerrorist(CCSPlayerController player)
    {
        CrashBreadcrumbs.Log($"auto team start {CrashBreadcrumbs.DescribePlayer(player)}");
        if (!player.IsValid || player.Connected != PlayerConnectedState.Connected)
        {
            CrashBreadcrumbs.Log($"auto team skipped invalid/disconnected {CrashBreadcrumbs.DescribePlayer(player)}");
            return;
        }

        if (player.Team != CsTeam.CounterTerrorist)
        {
            CrashBreadcrumbs.Log($"auto team SwitchTeam start target=CounterTerrorist {CrashBreadcrumbs.DescribePlayer(player)}");
            player.SwitchTeam(CsTeam.CounterTerrorist);
            CrashBreadcrumbs.Log($"auto team SwitchTeam end target=CounterTerrorist {CrashBreadcrumbs.DescribePlayer(player)}");
        }

        CrashBreadcrumbs.Log($"auto team ForceTeamState start target=CounterTerrorist {CrashBreadcrumbs.DescribePlayer(player)}");
        player.ForceTeamState(CsTeam.CounterTerrorist);
        CrashBreadcrumbs.Log($"auto team ForceTeamState end target=CounterTerrorist {CrashBreadcrumbs.DescribePlayer(player)}");

        if (!player.PawnIsAlive)
        {
            CrashBreadcrumbs.Log($"auto team Respawn start {CrashBreadcrumbs.DescribePlayer(player)}");
            player.Respawn();
            CrashBreadcrumbs.Log($"auto team Respawn end {CrashBreadcrumbs.DescribePlayer(player)}");
        }

        CrashBreadcrumbs.Log($"auto team end {CrashBreadcrumbs.DescribePlayer(player)}");
    }
}
