using System.Drawing;
using ZombieModPlugin.Abilities.Utils;

namespace ZombieModPlugin.Abilities.Executors;

public class InvisibilityExecutor : Ability
{
    private const int TransparentAlpha = 60;
    private const int NormalAlpha = 255;

    public InvisibilityExecutor()
        : base(
            id: "invisibility",
            name: "Invisibility",
            description: "Become semi-invisible for a short time.",
            cooldown: 20f,
            unlockCost: 500,
            duration: 6f)
    {
    }

    public override void Execute(AbilityExecutionContext context)
    {
        var player = context.Player;
        var pawn = player.PlayerPawn.Value;
        if (pawn == null) return;

        AbilityUtils.RunTimedEffect(
            player,
            Duration,
            apply: p => p.Render = Color.FromArgb(TransparentAlpha, 255, 255, 255),
            revert: p => p.Render = Color.FromArgb(NormalAlpha, 255, 255, 255)
        );

        context.PlayerState.SetCooldown(AbilityType.Invisibility, Cooldown);
        AbilityUtils.TrackActiveAbilityDuration(player, AbilityType.Invisibility, Duration, context.PlayerState);
    }
}
