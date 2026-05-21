using ZombieModPlugin.Abilities.Utils;
using ZombieModPlugin.Sounds;

namespace ZombieModPlugin.Abilities.Executors;

public class BerserkExecutor : Ability
{
    public BerserkExecutor()
        : base(
            id: "berserk",
            name: "Berserk",
            description: "Temporarily increases your speed and damage.",
            cooldown: 10f,
            unlockCost: 100,
            duration: 5f)
    {
    }

    public override void Execute(AbilityExecutionContext context)
    {
        var player = context.Player;
        var playerPawn = player.PlayerPawn.Value;
        if (playerPawn == null) return;

        var state = context.PlayerState;
        var config = context.Config.AbilityConfig.Berserk;
        var speedMultiplier = Math.Clamp(config.SpeedMultiplier, 0.1f, 4.0f);

        AbilityUtils.TrackActiveAbilityDuration(player, AbilityType.Berserk, config.DurationSeconds, state);
        AbilityUtils.ApplySpeedBoost(player, speedMultiplier, config.DurationSeconds);
        ZombieSounds.EmitWithExtras(playerPawn, context.Config, config.ActivationSound, config.ExtraActivationSounds);

        context.PlayerState.SetCooldown(AbilityType.Berserk, config.CooldownSeconds);
    }
}
