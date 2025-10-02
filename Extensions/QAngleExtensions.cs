using CounterStrikeSharp.API.Modules.Utils;
using System;

namespace ZombieModPlugin.Extensions;

public static class QAngleExtensions
{
    /// <summary>
    /// Converts the QAngle (pitch, yaw, roll) into a forward-facing Vector.
    /// </summary>
    public static Vector ToForwardVector(this QAngle angles)
    {
        float pitch = angles.X * MathF.PI / 180f;
        float yaw = angles.Y * MathF.PI / 180f;

        float cosPitch = MathF.Cos(pitch);

        return new Vector(
            MathF.Cos(yaw) * cosPitch,
            MathF.Sin(yaw) * cosPitch,
            -MathF.Sin(pitch)
        );
    }
}
