using ZombieModPlugin.Abilities.Utils;
using ZombieModPlugin.Extensions;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

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

        var nearbyPlayers = player.GetPlayersInProximity(context.AllPlayers, ExplosionRadius);

        Console.WriteLine("nearby players - " + nearbyPlayers.Count);



        foreach (var target in nearbyPlayers)
        {
            var targetPawn = target.PlayerPawn.Value;
            if (targetPawn == null || target == player || targetPawn.AbsOrigin == null)
                continue;

            var dist = (targetPawn.AbsOrigin - origin).Length();
            var damage = (int)(ExplosionDamage * (1 - (dist / ExplosionRadius)));

            if (damage > 0)
            {
                targetPawn.Health -= damage;

                if (targetPawn.Health <= 0)
                {
                    targetPawn.CommitSuicide(false, true);
                }
            }
        }

        pawn.EmitSound("tr.C4Explode");

        player.ExecuteClientCommandFromServer("kill");

        //TODO: Spawn explosion

        context.SetCooldown(AbilityType.SelfDestruct, Cooldown);
    }
}
