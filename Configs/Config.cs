using CounterStrikeSharp.API.Core;
using ZombieModPlugin.Abilities;
using ZombieModPlugin.Zombies.Models;

namespace ZombieModPlugin.Configs;

public class BaseConfig : BasePluginConfig
{
    public ZombieConfig ZombieConfig { get; set; } = new();
    public HumanConfig HumanConfig { get; set; } = new();
    public GeneralConfig GeneralConfig { get; set; } = new();
    public ChatConfig ChatConfig { get; set; } = new();
    public CommandsConfig CommandsConfig { get; set; } = new();
    public MessagesConfig MessagesConfig { get; set; } = new();
}

public class GeneralConfig
{
    public float FirstInfectionDelaySeconds { get; set; } = 15f;
}

public class CommandsConfig
{
    public string Shop { get; set; } = "shop";
    public string Abilities { get; set; } = "abilities";
    public string Level { get; set; } = "level";
    public string SwitchZombie { get; set; } = "zombie";
}

public class ChatConfig
{
    public string ZombiePrefix { get; set; } = "[ZOMBIE]";
    public string HumanPrefix { get; set; } = "[HUMAN]";
}

public class MessagesConfig
{
    /// Message shown when the player does not have enough money to purchase an ability.
    public string NotEnoughMoney { get; set; } = "You do not have enough money to purchase this item.";

    /// Message shown when the player does not have enough experience to unlock an ability.
    public string NotEnoughExp { get; set; } = "You do not have enough experience to unlock this ability.";

    /// Message shown when the player successfully unlocks an ability. {0} = ability name.
    public string AbilityUnlocked { get; set; } = "You have unlocked the ability: {0}";

    /// Message shown when an ability is still on cooldown. {0} = ability name, {1} = seconds remaining.
    public string AbilityOnCooldown { get; set; } = "Ability {0} is on cooldown for {1} more seconds.";

    /// Message shown when the player uses an ability. {0} = ability name.
    public string AbilityUsed { get; set; } = "You have used the ability: {0}";

    /// Message shown when the player has reached the max number of abilities for their zombie type.
    public string MaxAbilitiesReached { get; set; } = "You have reached the maximum number of abilities for your zombie type.";

    /// Message shown when the specified ability does not exist.
    public string InvalidAbility { get; set; } = "The specified ability does not exist.";

    /// Message showing the player's current level and XP progress. {0} = level, {1} = current XP, {2} = XP required for next level.
    public string CurrentLevel { get; set; } = "You are currently level {0} with {1}/{2} XP.";

    /// Message shown when the player levels up. {0} = new level.
    public string LevelUp { get; set; } = "Congratulations! You have leveled up to level {0}.";

    /// Message shown at round start when the player is a zombie.
    public string StartingAsZombie { get; set; } = "You are starting as a zombie. Survive and infect humans!";

    /// Message shown at round start when the player is a human.
    public string StartingAsHuman { get; set; } = "You are starting as a human. Survive the zombie onslaught!";

    /// Header text for the ability shop.
    public string ShopHeader { get; set; } = "=== Ability Shop ===";

    /// Format for displaying each shop item. {0} = index, {1} = ability name, {2} = description, {3} = cost.
    public string ShopItemFormat { get; set; } = "{0}. {1} - {2} (Cost: {3})";

    /// Footer text for the ability shop.
    public string ShopFooter { get; set; } = "Type !abilities <ability_name> to unlock an ability.";
}


public class ZombieConfig
{
    public Zombie[] ZombieTypes { get; set; } = new[]
    {
        new Zombie
        {
            Id = "brute",
            Name = "Brute",
            Health = 300,
            Speed = 0.85f,
            Damage = 40,
            Gravity = 1.0f,
            DefaultAbilities = new[] { AbilityType.DamageResistance },
            UnlockableAbilities = new[] { AbilityType.Berserk, AbilityType.Roar, AbilityType.SelfDestruct }
        },
        new Zombie
        {
            Id = "runner",
            Name = "Runner",
            Health = 150,
            Speed = 1.3f,
            Damage = 20,
            Gravity = 1.0f,
            DefaultAbilities = [AbilityType.SpeedBoost],
            UnlockableAbilities = [AbilityType.Pounce, AbilityType.ToxicAura, AbilityType.BlindSpit]
        },
        new Zombie
        {
            Id = "stalker",
            Name = "Stalker",
            Health = 200,
            Speed = 1.1f,
            Damage = 25,
            Gravity = 1.0f,
            DefaultAbilities = [AbilityType.Invisibility],
            UnlockableAbilities = [AbilityType.Roar, AbilityType.Pounce, AbilityType.SpeedBoost]
        },
        new Zombie
        {
            Id = "medic",
            Name = "Infected Healer",
            Health = 220,
            Speed = 1.0f,
            Damage = 15,
            Gravity = 1.0f,
            DefaultAbilities = [AbilityType.HealthRegen],
            UnlockableAbilities = [AbilityType.ToxicAura, AbilityType.SelfDestruct, AbilityType.BlindSpit]
        }
    };

    public int StartingLevel { get; set; } = 1;
    public int MaxLevel { get; set; } = 5;
    public int XPPerKill { get; set; } = 25;
    public int MaxAbilitiesPerZombie { get; set; } = 4;
}


public class HumanConfig
{
    public int StartingMoney { get; set; } = 5000;
    public int MoneyPerKill { get; set; } = 100;
    public int MoneyPerRound { get; set; } = 50;
}


