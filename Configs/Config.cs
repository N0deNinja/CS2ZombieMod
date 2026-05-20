using CounterStrikeSharp.API.Core;
using ZombieModPlugin.Abilities;
using ZombieModPlugin.Zombies.Models;

namespace ZombieModPlugin.Configs;

public class BaseConfig : BasePluginConfig
{
    public ZombieConfig ZombieConfig { get; set; } = new();
    public HumanConfig HumanConfig { get; set; } = new();
    public GeneralConfig GeneralConfig { get; set; } = new();
    public AdminTestConfig AdminTestConfig { get; set; } = new();
    public ChatConfig ChatConfig { get; set; } = new();
    public CommandsConfig CommandsConfig { get; set; } = new();
    public MessagesConfig MessagesConfig { get; set; } = new();
}

public class GeneralConfig
{
    public int MinimumPlayersToStart { get; set; } = 2;
    public float FirstInfectionDelaySeconds { get; set; } = 15f;
    public int RoundDurationSeconds { get; set; } = 300;
    public int ActiveHudIntervalSeconds { get; set; } = 1;
    public int WaitingHudIntervalSeconds { get; set; } = 1;
    public float PostRoundDelaySeconds { get; set; } = 5f;
    public int MinimumInitialZombies { get; set; } = 1;
    public int MaximumInitialZombies { get; set; } = 0;
    public float InitialZombieRatio { get; set; } = 0.15f;
    public bool RandomizePlayerSpawns { get; set; } = true;
    public float SpawnScatterDelaySeconds { get; set; } = 0.3f;
    public bool IncludeBotsInRound { get; set; } = false;
}

public class AdminTestConfig
{
    public bool Enabled { get; set; } = true;
    public bool RequireAdminPermissions { get; set; } = false;
    public string[] RequiredPermissions { get; set; } = ["@css/root"];
    public string MenuCommand { get; set; } = "zadmin";
    public string ClassCommand { get; set; } = "zclass";
    public string HumanCommand { get; set; } = "zhuman";
    public string BotsCommand { get; set; } = "zbots";
    public string RoundCommand { get; set; } = "zround";
    public int DefaultBotCount { get; set; } = 3;
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
            Health = 5000,
            SpeedModifier = 0.85f,
            Damage = 40,
            Gravity = 1.0f,
            DefaultAbilities = new[] { AbilityType.DamageResistance },
            UnlockableAbilities = new[] { AbilityType.Berserk, AbilityType.Roar, AbilityType.SelfDestruct }
        },
        new Zombie
        {
            Id = "runner",
            Name = "Runner",
            Health = 3000,
            SpeedModifier = 1.3f,
            Damage = 20,
            Gravity = 1.0f,
            DefaultAbilities = [AbilityType.SpeedBoost],
            UnlockableAbilities = [AbilityType.Pounce, AbilityType.ToxicAura, AbilityType.BlindSpit]
        },
        new Zombie
        {
            Id = "stalker",
            Name = "Stalker",
            Health = 3500,
            SpeedModifier = 1.1f,
            Damage = 25,
            Gravity = 1.0f,
            DefaultAbilities = [AbilityType.Invisibility],
            UnlockableAbilities = [AbilityType.Roar, AbilityType.Pounce, AbilityType.SpeedBoost]
        },
        new Zombie
        {
            Id = "medic",
            Name = "Infected Healer",
            Health = 4000,
            SpeedModifier = 1.0f,
            Damage = 15,
            Gravity = 1.0f,
            DefaultAbilities = [AbilityType.HealthRegen],
            UnlockableAbilities = [AbilityType.ToxicAura, AbilityType.SelfDestruct, AbilityType.BlindSpit]
        }
    };

    public int StartingLevel { get; set; } = 1;
    public int MaxLevel { get; set; } = 5;
    public int InfectionHitsRequired { get; set; } = 3;
    public int XPPerKill { get; set; } = 25;
    public int XPPerLevel { get; set; } = 100;
    public int MaxAbilitiesPerZombie { get; set; } = 4;
}


public class HumanConfig
{
    public int StartingMoney { get; set; } = 5000;
    public int MoneyPerKill { get; set; } = 100;
    public int MoneyPerRound { get; set; } = 50;
}


