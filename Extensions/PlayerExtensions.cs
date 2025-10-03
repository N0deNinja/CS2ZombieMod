using CounterStrikeSharp.API.Core;
using ZombieModPlugin.States;
namespace ZombieModPlugin.Extensions;

public static class PlayerExtensions
{
    public static PlayerState GetState(this CCSPlayerController player, Dictionary<ulong, PlayerState> states)
    {
        if (!player.IsValid) throw new InvalidOperationException("Invalid player");
        var steamId = player.SteamID;
        if (!states.TryGetValue(steamId, out var state))
        {
            state = new PlayerState();
            states[steamId] = state;
        }
        return state;
    }
}
