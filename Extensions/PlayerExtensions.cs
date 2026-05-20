using CounterStrikeSharp.API.Core;
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
        if (!player.IsValid)
            throw new InvalidOperationException("Invalid player");

        if (!player.IsBot)
            return player.SteamID;

        var userId = Convert.ToUInt64(player.UserId);
        return BotStateKeyPrefix | userId;
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
