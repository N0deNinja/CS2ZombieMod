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

    public static List<CCSPlayerController> GetPlayersInProximity(
     this CCSPlayerController player,
     IEnumerable<CCSPlayerController> allPlayers,
     float radius)
    {
        var result = new List<CCSPlayerController>();

        var pawn = player.PlayerPawn.Value;
        if (pawn == null || pawn.AbsOrigin == null)
            return result;

        var origin = pawn.AbsOrigin;

        foreach (var other in allPlayers)
        {
            if (other == null || !other.IsValid || other == player)
                continue;

            var otherPawn = other.PlayerPawn.Value;
            if (otherPawn == null || otherPawn.AbsOrigin == null)
                continue;

            var otherOrigin = otherPawn.AbsOrigin;

            float dx = otherOrigin.X - origin.X;
            float dy = otherOrigin.Y - origin.Y;
            float dz = otherOrigin.Z - origin.Z;
            float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);

            if (dist <= radius)
                result.Add(other);
        }

        return result;
    }


}
