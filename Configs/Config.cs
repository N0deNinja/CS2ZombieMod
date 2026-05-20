using CounterStrikeSharp.API.Core;
using ZombieModPlugin.Abilities;
using ZombieModPlugin.Humans.Models;
using ZombieModPlugin.Zombies.Models;

namespace ZombieModPlugin.Configs;

public class BaseConfig : BasePluginConfig
{
    public ZombieConfig ZombieConfig { get; set; } = new();
    public HumanConfig HumanConfig { get; set; } = new();
    public AbilityConfig AbilityConfig { get; set; } = new();
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
    public bool AutoDownloadWorkshopAddons { get; set; } = true;
    public string[] WorkshopAddonIds { get; set; } = ["3170427476"];
}

public class AdminTestConfig
{
    public bool Enabled { get; set; } = true;
    public bool RequireAdminPermissions { get; set; } = false;
    public string[] RequiredPermissions { get; set; } = ["@css/root"];
    public string MenuCommand { get; set; } = "zadmin";
    public string ClassCommand { get; set; } = "zclass";
    public string HumanCommand { get; set; } = "zhuman";
    public string HumanClassCommand { get; set; } = "hclass";
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
    public string PlayerModel { get; set; } = "";
    public string DefaultZombieClassId { get; set; } = "classic";

    public Zombie[] ZombieTypes { get; set; } = new[]
    {
        new Zombie
        {
            Id = "classic",
            Name = "Classic Zombie",
            Health = 1200,
            SpeedModifier = 1.0f,
            Damage = 25,
            Gravity = 1.0f,
            PlayerModel = "agents/models/gxp/classic_zombie/classic_zombie.vmdl",
            DefaultAbilities = [AbilityType.Pounce],
            UnlockableAbilities = [AbilityType.Berserk, AbilityType.HealthRegen]
        },
        new Zombie
        {
            Id = "molong",
            Name = "VIP Molong",
            Health = 1800,
            SpeedModifier = 1.05f,
            Damage = 30,
            Gravity = 0.9f,
            PlayerModel = "agents/models/han/molong/molong.vmdl",
            DefaultAbilities = [AbilityType.Pounce, AbilityType.MultiJump],
            UnlockableAbilities = [AbilityType.Berserk, AbilityType.Invisibility]
        },
        new Zombie
        {
            Id = "runner",
            Name = "Runner",
            Health = 850,
            SpeedModifier = 1.3f,
            Damage = 20,
            Gravity = 1.0f,
            PlayerModel = "agents/models/s2ze/zombie_basic/zombie_basic.vmdl",
            DefaultAbilities = [AbilityType.SpeedBoost],
            UnlockableAbilities = [AbilityType.Pounce]
        },
        new Zombie
        {
            Id = "brute",
            Name = "Brute",
            Health = 2000,
            SpeedModifier = 0.85f,
            Damage = 40,
            Gravity = 1.0f,
            PlayerModel = "agents/models/s2ze/zombie_chris_walker/zombie_chris_walker.vmdl",
            DefaultAbilities = [AbilityType.Berserk],
            UnlockableAbilities = [AbilityType.HealthRegen, AbilityType.SelfDestruct]
        },
        new Zombie
        {
            Id = "cultist",
            Name = "Cultist",
            Health = 1050,
            SpeedModifier = 1.0f,
            Damage = 20,
            Gravity = 1.0f,
            PlayerModel = "agents/models/s2ze/zombie_cultist/zombie_cultist.vmdl",
            DefaultAbilities = [AbilityType.CultistHex],
            UnlockableAbilities = [AbilityType.Invisibility, AbilityType.FrostBolt]
        },
        new Zombie
        {
            Id = "frozen",
            Name = "Frozen Zombie",
            Health = 1150,
            SpeedModifier = 0.95f,
            Damage = 22,
            Gravity = 1.0f,
            PlayerModel = "agents/models/s2ze/zombie_frozen/zombie_frozen.vmdl",
            DefaultAbilities = [AbilityType.FrostBolt],
            UnlockableAbilities = [AbilityType.HealthRegen, AbilityType.CultistHex]
        },
        new Zombie
        {
            Id = "lurker",
            Name = "Lurker",
            Health = 800,
            SpeedModifier = 1.2f,
            Damage = 18,
            Gravity = 0.75f,
            PlayerModel = "characters/models/kolka/2025/lurker/lurker.vmdl",
            DefaultAbilities = [AbilityType.LurkerCloak, AbilityType.Pounce],
            UnlockableAbilities = [AbilityType.MultiJump, AbilityType.Invisibility]
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
    public string PlayerModel { get; set; } = "agents/models/s2ze/earthgovsol/deadspace_earthgovsol_hitbox.vmdl";
    public string DefaultHumanClassId { get; set; } = "security";
    public string[] DefaultWeapons { get; set; } = ["weapon_knife", "weapon_usp_silencer"];
    public int StartingMoney { get; set; } = 5000;
    public int MoneyPerKill { get; set; } = 100;
    public int MoneyPerRound { get; set; } = 50;
    public float ZombieKnockbackForce { get; set; } = 420f;
    public float ZombieKnockbackUpForce { get; set; } = 90f;
    public int InfectionHitsRequired { get; set; } = 3;

    public HumanClass[] HumanClasses { get; set; } =
    [
        new HumanClass
        {
            Id = "security",
            Name = "EarthGov Security",
            PlayerModel = "agents/models/s2ze/earthgovsol/deadspace_earthgovsol_hitbox.vmdl",
            Health = 100,
            SpeedModifier = 1.0f,
            Gravity = 1.0f,
            DefaultWeapons = ["weapon_knife", "weapon_usp_silencer"],
            StartingMoney = 5000
        },
        new HumanClass
        {
            Id = "tac",
            Name = "Tactical Trooper",
            PlayerModel = "agents/models/s2ze/hd2_b01_tac/hd2_b01_tac_nohitbox.vmdl",
            Health = 105,
            SpeedModifier = 1.02f,
            Gravity = 1.0f,
            DefaultWeapons = ["weapon_knife", "weapon_usp_silencer", "weapon_famas"],
            StartingMoney = 5500
        },
        new HumanClass
        {
            Id = "hunter",
            Name = "Hunter",
            PlayerModel = "agents/models/apple/vector/vector.vmdl",
            Health = 100,
            SpeedModifier = 1.18f,
            Gravity = 0.95f,
            InfectionHitsRequired = 3,
            DefaultWeapons = ["weapon_knife", "weapon_usp_silencer"],
            DefaultAbilities = [AbilityType.MultiJump],
            StartingMoney = 5000
        },
        new HumanClass
        {
            Id = "vip_heavy",
            Name = "VIP Heavy",
            PlayerModel = "agents/models/reborn/deadpool/deadpool.vmdl",
            Health = 150,
            SpeedModifier = 0.95f,
            Gravity = 1.0f,
            InfectionHitsRequired = 5,
            ZombieKnockbackForce = 760f,
            ZombieKnockbackUpForce = 130f,
            DefaultWeapons = ["weapon_knife", "weapon_deagle", "weapon_m4a1_silencer"],
            StartingMoney = 9000
        },
        new HumanClass
        {
            Id = "vip_tactical",
            Name = "VIP Tactical",
            PlayerModel = "characters/models/kolka/ghost/ghost.vmdl",
            Health = 125,
            SpeedModifier = 1.05f,
            Gravity = 1.0f,
            InfectionHitsRequired = 4,
            ZombieKnockbackForce = 650f,
            ZombieKnockbackUpForce = 110f,
            DefaultWeapons = ["weapon_knife", "weapon_usp_silencer", "weapon_galilar", "weapon_hegrenade", "weapon_smokegrenade"],
            StartingMoney = 8000
        }
    ];
}

public class AbilityConfig
{
    public PounceAbilityConfig Pounce { get; set; } = new();
    public SpeedBoostAbilityConfig SpeedBoost { get; set; } = new();
    public InvisibilityAbilityConfig Invisibility { get; set; } = new();
    public HealthRegenAbilityConfig HealthRegen { get; set; } = new();
    public BerserkAbilityConfig Berserk { get; set; } = new();
    public SelfDestructAbilityConfig SelfDestruct { get; set; } = new();
    public FrostBoltAbilityConfig FrostBolt { get; set; } = new();
    public CultistHexAbilityConfig CultistHex { get; set; } = new();
    public MultiJumpAbilityConfig MultiJump { get; set; } = new();
    public LurkerCloakAbilityConfig LurkerCloak { get; set; } = new();

    public AbilitySettingsConfig GetSettings(AbilityType type)
    {
        return type switch
        {
            AbilityType.Pounce => Pounce,
            AbilityType.SpeedBoost => SpeedBoost,
            AbilityType.Invisibility => Invisibility,
            AbilityType.HealthRegen => HealthRegen,
            AbilityType.Berserk => Berserk,
            AbilityType.SelfDestruct => SelfDestruct,
            AbilityType.FrostBolt => FrostBolt,
            AbilityType.CultistHex => CultistHex,
            AbilityType.MultiJump => MultiJump,
            AbilityType.LurkerCloak => LurkerCloak,
            _ => new AbilitySettingsConfig()
        };
    }
}

public class AbilitySettingsConfig
{
    public float CooldownSeconds { get; set; } = 10f;
    public float DurationSeconds { get; set; } = 5f;
    public int UnlockCost { get; set; } = 100;
}

public class PounceAbilityConfig : AbilitySettingsConfig
{
    public PounceAbilityConfig()
    {
        CooldownSeconds = 8f;
        DurationSeconds = 0.1f;
        UnlockCost = 400;
    }

    public float Force { get; set; } = 700f;
    public float UpForce { get; set; } = 300f;
}

public class SpeedBoostAbilityConfig : AbilitySettingsConfig
{
    public float SpeedMultiplier { get; set; } = 1.5f;
}

public class InvisibilityAbilityConfig : AbilitySettingsConfig
{
    public InvisibilityAbilityConfig()
    {
        CooldownSeconds = 20f;
        DurationSeconds = 6f;
        UnlockCost = 500;
    }

    public int Alpha { get; set; } = 60;
}

public class HealthRegenAbilityConfig : AbilitySettingsConfig
{
    public int HealPerTick { get; set; } = 20;
    public float TickIntervalSeconds { get; set; } = 1f;
}

public class BerserkAbilityConfig : AbilitySettingsConfig
{
    public float SpeedMultiplier { get; set; } = 1.5f;
}

public class SelfDestructAbilityConfig : AbilitySettingsConfig
{
    public SelfDestructAbilityConfig()
    {
        CooldownSeconds = 20f;
        DurationSeconds = 0.1f;
        UnlockCost = 300;
    }

    public float Radius { get; set; } = 400f;
    public float Damage { get; set; } = 40f;
    public float Force { get; set; } = 4000f;
}

public class FrostBoltAbilityConfig : AbilitySettingsConfig
{
    public FrostBoltAbilityConfig()
    {
        CooldownSeconds = 12f;
        DurationSeconds = 3f;
        UnlockCost = 350;
    }

    public float Speed { get; set; } = 1200f;
    public float Range { get; set; } = 1200f;
    public float AimConeDot { get; set; } = 0.82f;
    public float SlowMultiplier { get; set; } = 0.55f;
    public string HitMessage { get; set; } = "You were chilled by Frost Bolt.";
}

public class CultistHexAbilityConfig : AbilitySettingsConfig
{
    public CultistHexAbilityConfig()
    {
        CooldownSeconds = 18f;
        DurationSeconds = 4f;
        UnlockCost = 450;
    }

    public float Radius { get; set; } = 450f;
    public float HumanSpeedMultiplier { get; set; } = 0.75f;
    public float KnockbackMultiplier { get; set; } = 0.65f;
}

public class MultiJumpAbilityConfig : AbilitySettingsConfig
{
    public MultiJumpAbilityConfig()
    {
        CooldownSeconds = 0f;
        DurationSeconds = 0f;
        UnlockCost = 250;
    }

    public int HumanAdditionalJumps { get; set; } = 1;
    public int ZombieAdditionalJumps { get; set; } = 2;
    public float UpForce { get; set; } = 300f;
    public float ForwardForce { get; set; } = 90f;
}

public class LurkerCloakAbilityConfig : AbilitySettingsConfig
{
    public LurkerCloakAbilityConfig()
    {
        CooldownSeconds = 0f;
        DurationSeconds = 0f;
        UnlockCost = 300;
    }

    public float StationaryDelaySeconds { get; set; } = 2.0f;
    public float MovementThreshold { get; set; } = 12f;
    public float TickIntervalSeconds { get; set; } = 0.2f;
    public int Alpha { get; set; } = 45;
}


