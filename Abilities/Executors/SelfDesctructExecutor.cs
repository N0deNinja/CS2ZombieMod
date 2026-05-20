using ZombieModPlugin.Abilities.Utils;
using ZombieModPlugin.Extensions;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API;
using System.Drawing;

namespace ZombieModPlugin.Abilities.Executors;

public class SelfDestructExecutor : Ability
{
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
        var config = context.Config.AbilityConfig.SelfDestruct;

        pawn.EmitSound("tr.C4Explode");

        var explosion = Utilities.CreateEntityByName<CEnvExplosion>("env_explosion");

        explosion!.Magnitude = 40;
        explosion.PlayerDamage = Math.Clamp(config.Damage, 0f, 1000f);
        explosion.RadiusOverride = (int)Math.Clamp(config.Radius, 0f, 2000f);
        explosion.InnerRadius = 0f;
        explosion.DamageForce = Math.Clamp(config.Force, 0f, 10000f);
        explosion.CreateDebris = true;
        explosion.Render = Color.FromArgb(255, 255, 100, 0);

        explosion!.DispatchSpawn();
        explosion.Teleport(origin, null, null);
        explosion.AcceptInput("Explode");



        player.ExecuteClientCommandFromServer("kill");
        context.PlayerState.SetCooldown(AbilityType.SelfDestruct, config.CooldownSeconds);
    }
}
