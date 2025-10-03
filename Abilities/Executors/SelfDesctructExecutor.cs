using ZombieModPlugin.Abilities.Utils;
using ZombieModPlugin.Extensions;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API;
using System.Drawing;

namespace ZombieModPlugin.Abilities.Executors;

public class SelfDestructExecutor : Ability
{
    private const float ExplosionRadius = 300f;
    private const int ExplosionDamage = 400;

    public SelfDestructExecutor()
        : base(
            id: "self_destruct",
            name: "Self Destruct",
            description: "Explodes on use, damaging nearby humans.",
            cooldown: 20f,
            unlockCost: 300,
            duration: 0.1f)
    {
    }

    public override void Execute(AbilityExecutionContext context)
    {
        var player = context.Player;
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || pawn.AbsOrigin == null) return;

        var origin = pawn.AbsOrigin;

        pawn.EmitSound("tr.C4Explode");

        var explosion = Utilities.CreateEntityByName<CEnvExplosion>("env_explosion");

        explosion!.Magnitude = 40;
        explosion.PlayerDamage = 40f;
        explosion.RadiusOverride = 400;
        explosion.InnerRadius = 0f;
        explosion.DamageForce = 4000f;
        explosion.CreateDebris = true;
        explosion.Render = Color.FromArgb(255, 255, 100, 0);

        explosion!.DispatchSpawn();
        explosion.Teleport(origin, null, null);
        explosion.AcceptInput("Explode");



        player.ExecuteClientCommandFromServer("kill");
        context.SetCooldown(AbilityType.SelfDestruct, Cooldown);
    }
}
