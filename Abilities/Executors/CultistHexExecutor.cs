using ZombieModPlugin.Abilities.Utils;
using ZombieModPlugin.Extensions;
using ZombieModPlugin.Sounds;

namespace ZombieModPlugin.Abilities.Executors;

public class CultistHexExecutor : Ability
{
    public CultistHexExecutor()
        : base(
            id: "cultist_hex",
            name: "Cultist Hex",
            description: "Weakens nearby humans, reducing their speed and zombie knockback.",
            cooldown: 18f,
            unlockCost: 450,
            duration: 4f)
    {
    }

    public override void Execute(AbilityExecutionContext context)
    {
        var player = context.Player;
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid)
            return;

        var config = context.Config.AbilityConfig.CultistHex;
        var radius = Math.Clamp(config.Radius, 64f, 2048f);
        var speedMultiplier = Math.Clamp(config.HumanSpeedMultiplier, 0.1f, 1.0f);
        var knockbackMultiplier = Math.Clamp(config.KnockbackMultiplier, 0.1f, 1.0f);
        var affected = 0;
        ZombieSounds.Emit(pawn, context.Config, config.ActivationSound);

        foreach (var target in player.GetPlayersInProximity(context.AllPlayers, radius))
        {
            if (!target.IsValid || !target.PawnIsAlive)
                continue;

            var targetState = target.GetState(context.PlayerStates);
            if (targetState.IsZombie)
                continue;

            AbilityUtils.ApplySpeedModifier(target, speedMultiplier, config.DurationSeconds);
            AbilityUtils.ApplyKnockbackDebuff(targetState, knockbackMultiplier, config.DurationSeconds);
            target.PrintToChat($"{context.Config.ChatConfig.ZombiePrefix} A hex weakens your movement and knockback.");
            affected++;
        }

        player.PrintToChat(affected > 0
            ? $"{context.Config.ChatConfig.ZombiePrefix} Cultist Hex affected {affected} human(s)."
            : $"{context.Config.ChatConfig.ZombiePrefix} Cultist Hex found no humans in range.");

        context.PlayerState.SetCooldown(AbilityType.CultistHex, config.CooldownSeconds);
        AbilityUtils.TrackActiveAbilityDuration(player, AbilityType.CultistHex, config.DurationSeconds, context.PlayerState);
    }
}
