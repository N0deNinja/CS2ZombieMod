using System.Drawing;
using ZombieModPlugin.Abilities.Utils;
using ZombieModPlugin.Sounds;

namespace ZombieModPlugin.Abilities.Executors;

public class InvisibilityExecutor : Ability
{
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

        var config = context.Config.AbilityConfig.Invisibility;
        var alpha = Math.Clamp(config.Alpha, 0, 255);
        ZombieSounds.EmitAbilityActivation(player, context.Config, config);

        AbilityUtils.RunTimedEffect(
            player,
            config.DurationSeconds,
            apply: p => p.Render = Color.FromArgb(alpha, 255, 255, 255),
            revert: p => p.Render = Color.FromArgb(NormalAlpha, 255, 255, 255)
        );

        context.PlayerState.SetCooldown(AbilityType.Invisibility, config.CooldownSeconds);
        AbilityUtils.TrackActiveAbilityDuration(player, AbilityType.Invisibility, config.DurationSeconds, context.PlayerState);
    }
}
