using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using ReclaimCS.Shared.CounterStrike;
using ZombieModPlugin.States;

namespace ZombieModPlugin.Extensions;

public static class PlayerExtensions
{
    private const ulong BotStateKeyPrefix = 0xB000_0000_0000_0000;

    public static PlayerState GetState(this CCSPlayerController player, Dictionary<ulong, PlayerState> states)
    {
        if (!player.IsValid) throw new InvalidOperationException("Invalid player");

        var key = player.GetStateKey();
        if (!states.TryGetValue(key, out var state))
        {
            state = new PlayerState();
            states[key] = state;
        }
        return state;
    }

    public static ulong GetStateKey(this CCSPlayerController player)
    {
        return PlayerIdentityExtensions.GetRuntimeKey(player, BotStateKeyPrefix);
    }

    public static void ForceTeamState(this CCSPlayerController player, CsTeam team)
    {
        PlayerIdentityExtensions.ForceTeamState(player, team);
    }

    public static List<CCSPlayerController> GetPlayersInProximity(
     this CCSPlayerController player,
     IEnumerable<CCSPlayerController> allPlayers,
     float radius)
    {
        return PlayerIdentityExtensions.GetPlayersInProximity(player, allPlayers, radius);
    }
}
