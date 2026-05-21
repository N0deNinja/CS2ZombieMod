using Microsoft.Extensions.Logging;
using ZombieModPlugin.Progression.Services;
using ZombieModPlugin.States;

namespace ZombieModPlugin.Abilities.Managers;

public class AbilityManager
{
    private readonly ProgressionService _progressionService;

    public AbilityManager(ProgressionService progressionService)
    {
        _progressionService = progressionService;
    }

    public void TryActivateAbility(AbilityType type, AbilityExecutionContext context)
    {
        var player = context.Player;
        var state = context.PlayerState;
        var config = context.Config;

        if (!player.IsValid)
            return;

        var zombie = state.SelectedZombieType;

        if (!state.IsZombie || zombie == null)
        {
            player.PrintToChat($"{config.ChatConfig.ZombiePrefix} You are not a zombie.");
            return;
        }


        if (!_progressionService.HasAbilityAvailable(state, type))
        {
            player.PrintToChat(config.MessagesConfig.InvalidAbility);
            return;
        }

        if (state.IsOnCooldown(type, out var remaining))
        {
            Console.WriteLine($"{type} - Is on cooldown");
            player.PrintToChat(string.Format(
                config.MessagesConfig.AbilityOnCooldown,
                AbilityRegistry.Get(type)?.Name ?? type.ToString(),
                Math.Ceiling(remaining)
            ));
            return;
        }

        var ability = AbilityRegistry.Get(type);
        if (ability == null)
        {
            player.PrintToChat(config.MessagesConfig.InvalidAbility);
            return;
        }

        try
        {
            ability.Execute(context);
            if (state.GlobalCooldowns.ContainsKey(type) || state.ActiveAbilities.Contains(type))
                player.PrintToChat(string.Format(config.MessagesConfig.AbilityUsed, ability.Name));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error executing ability {ability.Name}: {ex.Message}");
        }
    }
}
