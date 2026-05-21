using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System.Runtime.InteropServices;
using ZombieModPlugin.Configs;
using ZombieModPlugin.Sounds;

namespace ZombieModPlugin.Abilities.Executors;

public class WallClimbExecutor : Ability
{
    private const int RayTypeEndPoint = 0;
    private const int GameTraceHitNormalOffset = 144;
    private const int GameTraceFractionOffset = 172;
    private const int GameTraceStartInSolidOffset = 183;
    private const uint DefaultWallTraceMask = 0xC3001;
    private static bool _reportedWallTraceFailure;

    public WallClimbExecutor()
        : base(
            id: "wall_cling",
            name: "Wall Cling",
            description: "Cling to a nearby wall until you release with Space.",
            cooldown: 14f,
            unlockCost: 350,
            duration: 0f)
    {
    }

    public override void Execute(AbilityExecutionContext context)
    {
        var player = context.Player;
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null)
            return;

        var config = context.Config.AbilityConfig.WallClimb;
        if (!CanStartWallCling(player, pawn, config))
        {
            player.PrintToCenter(config.WallRequiredMessage);
            return;
        }

        var origin = pawn.AbsOrigin;
        var state = context.PlayerState;

        state.IsWallClinging = true;
        state.WallClingExpiresAtUtc = DateTime.MaxValue;
        state.WallClingAnchorX = origin.X;
        state.WallClingAnchorY = origin.Y;
        state.WallClingAnchorZ = origin.Z;
        state.ActiveAbilities.Add(AbilityType.WallClimb);

        pawn.Teleport(velocity: new Vector(0.0f, 0.0f, 0.0f));
        ZombieSounds.EmitAbilityActivation(player, context.Config, config);

        state.SetCooldown(AbilityType.WallClimb, config.CooldownSeconds);
    }

    private static bool CanStartWallCling(CCSPlayerController player, CCSPlayerPawn pawn, WallClimbAbilityConfig config)
    {
        if (!config.RequireWallContact)
            return true;

        if (config.RequireAirborne && IsOnGround(pawn))
            return false;

        return HasNearbyWall(player, pawn, config);
    }

    private static bool HasNearbyWall(CCSPlayerController player, CCSPlayerPawn pawn, WallClimbAbilityConfig config)
    {
        var origin = pawn.AbsOrigin;
        if (origin == null)
            return false;

        var traceDistance = Math.Clamp(config.WallTraceDistance, 8.0f, 160.0f);
        var heightOffset = Math.Clamp(config.WallTraceHeightOffset, 0.0f, 96.0f);
        var mask = config.WallTraceMask == 0 ? DefaultWallTraceMask : config.WallTraceMask;
        var ignoreEntityIndex = (int)pawn.Index;

        foreach (var (x, y) in WallTraceDirections())
        {
            var start = new Vector(origin.X, origin.Y, origin.Z + heightOffset);
            var end = new Vector(
                origin.X + x * traceDistance,
                origin.Y + y * traceDistance,
                origin.Z + heightOffset);

            if (TraceHitsWall(player, start, end, ignoreEntityIndex, mask))
                return true;
        }

        return false;
    }

    private static bool TraceHitsWall(
        CCSPlayerController player,
        Vector start,
        Vector end,
        int ignoreEntityIndex,
        uint mask)
    {
        try
        {
            var ray = NativeAPI.CreateRay1(RayTypeEndPoint, start.Handle, end.Handle);
            var trace = NativeAPI.NewTraceResult();
            var filter = NativeAPI.NewSimpleTraceFilter(ignoreEntityIndex);

            NativeAPI.TraceRay(ray, trace, filter, mask);
            return DidTraceHitVerticalSurface(trace);
        }
        catch (Exception ex)
        {
            if (!_reportedWallTraceFailure)
            {
                _reportedWallTraceFailure = true;
                Console.WriteLine($"[ZombieMod] Wall Cling trace failed for {player.PlayerName}: {ex.Message}");
            }

            return false;
        }
    }

    private static bool DidTraceHitVerticalSurface(IntPtr trace)
    {
        var fraction = ReadFloat(trace, GameTraceFractionOffset);
        var startInSolid = Marshal.ReadByte(trace, GameTraceStartInSolidOffset) != 0;

        if (!startInSolid && fraction >= 1.0f)
            return false;

        if (startInSolid)
            return true;

        var normalZ = ReadFloat(trace, GameTraceHitNormalOffset + sizeof(float) * 2);
        return MathF.Abs(normalZ) <= 0.65f;
    }

    private static float ReadFloat(IntPtr pointer, int offset)
    {
        return BitConverter.Int32BitsToSingle(Marshal.ReadInt32(pointer, offset));
    }

    private static IEnumerable<(float X, float Y)> WallTraceDirections()
    {
        yield return (1.0f, 0.0f);
        yield return (-1.0f, 0.0f);
        yield return (0.0f, 1.0f);
        yield return (0.0f, -1.0f);

        const float diagonal = 0.70710678f;
        yield return (diagonal, diagonal);
        yield return (diagonal, -diagonal);
        yield return (-diagonal, diagonal);
        yield return (-diagonal, -diagonal);
    }

    private static bool IsOnGround(CCSPlayerPawn pawn)
    {
        const uint onGroundFlag = 1u;
        return pawn.OnGroundLastTick
            || (pawn.Flags & onGroundFlag) == onGroundFlag
            || pawn.GroundEntity.Value != null;
    }
}
