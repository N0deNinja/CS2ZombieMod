using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using ReclaimCS.Shared.Administration;
using ReclaimCS.Shared.KillFeed;
using ReclaimCS.Shared.PlayerModels;
using ZombieModPlugin.Abilities;
using ZombieModPlugin.Humans.Models;
using ZombieModPlugin.Zombies.Models;

namespace ZombieModPlugin.Configs;

public class BaseConfig : BasePluginConfig
{
    public ZombieConfig ZombieConfig { get; set; } = new();
    public HumanConfig HumanConfig { get; set; } = new();
    public AbilityConfig AbilityConfig { get; set; } = new();
    public ProgressionConfig ProgressionConfig { get; set; } = new();
    public GeneralConfig GeneralConfig { get; set; } = new();
    public ReclaimAdminOptions Admin { get; set; } = new();
    public AdminTestConfig AdminTestConfig { get; set; } = new();
    public SoundConfig SoundConfig { get; set; } = new();
    public KillFeedIconOptions KillFeedIcons { get; set; } = new();
    public ZombieMeleeVisualConfig ZombieMeleeVisualConfig { get; set; } = new();
    public BlockadeConfig BlockadeConfig { get; set; } = new();
    public ChatConfig ChatConfig { get; set; } = new();
    public CommandsConfig CommandsConfig { get; set; } = new();
    public MessagesConfig MessagesConfig { get; set; } = new();
}

public class GeneralConfig
{
    public int MinimumPlayersToStart { get; set; } = 2;
    public float FirstInfectionDelaySeconds { get; set; } = 14f;
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
    public float AirAccelerate { get; set; } = 100f;
    public bool AutoDownloadWorkshopAddons { get; set; } = true;
    public string[] WorkshopAddonIds { get; set; } =
    [
        ..ReclaimPlayerModels.ZombieModWorkshopAddonIds,
        ReclaimPlayerModels.ReclaimCharactersWorkshopAddonId
    ];
    public bool RotateWorkshopMaps { get; set; } = true;
    public int RoundsPerWorkshopMap { get; set; } = 5;
    public string[] WorkshopMapIds { get; set; } = ["3623739053", "3685437201", "3222984182", "3283778158"];
    public string[] WorkshopMapNames { get; set; } = ["zm_vents_remake_m", "zm_liquid_anomaly_s", "zm_silent_village", "zm_mediumzm"];
}

public class AdminTestConfig
{
    public bool Enabled { get; set; } = true;
    public bool RequireAdminPermissions { get; set; } = true;
    public string[] RequiredPermissions { get; set; } = ["@css/root"];
    public string MenuCommand { get; set; } = "zadmin";
    public string ClassCommand { get; set; } = "zclass";
    public string HumanCommand { get; set; } = "zhuman";
    public string HumanClassCommand { get; set; } = "hclass";
    public string BotsCommand { get; set; } = "zbots";
    public string RoundCommand { get; set; } = "zround";
    public int DefaultBotCount { get; set; } = 3;
}

public class SoundConfig
{
    public bool Enabled { get; set; } = true;
    public float EmitVolume { get; set; } = 1.35f;
    public float EmitPitch { get; set; } = 1.0f;
    public bool UseTrackedPlayerSounds { get; set; } = false;
    public float PlayerSoundMaxDurationSeconds { get; set; } = 5.0f;
    public float PlayerSoundFollowIntervalSeconds { get; set; } = 0.1f;
    public string[] Resources { get; set; } =
    [
        "soundevents/soundevents_zr.vsndevts",
        "soundevents/soundevents_zr_extra.vsndevts",
        "sounds/claw_strike_1.vsnd",
        "sounds/claw_strike_2.vsnd",
        "sounds/zombie_attack_1.vsnd",
        "sounds/zombie_attack_2.vsnd",
        "sounds/zombie_attack_3.vsnd",
        "sounds/zombie_attack_4.vsnd",
        "sounds/zombie_attack_5.vsnd",
        "sounds/zombie_attack_6.vsnd",
        "sounds/zombie_attack_7.vsnd",
        "sounds/zombie_attack_8.vsnd",
        "sounds/zombie_attack_9.vsnd",
        "sounds/zombie_attack_10.vsnd",
        "sounds/zombie_attack_11.vsnd",
        "sounds/zombie_attack_12.vsnd",
        "sounds/inf_begun.vsnd",
        "sounds/inf_starts_14.vsnd",
        "sounds/prepare_for_infection.vsnd",
        "sounds/siren_14s.vsnd",
    ];
    public string FirstInfectionSound { get; set; } = "zr.amb.scream";
    public string[] ExtraFirstInfectionSounds { get; set; } = ["zr.zombie_attack_1"];
    public string InfectionSound { get; set; } = "zr.amb.scream";
    public string[] ExtraInfectionSounds { get; set; } = ["zr.zombie_attack_3"];
    public string InfectionCountdownStartSound { get; set; } = "zr.inf_starts_14";
    public string PrepareForInfectionSound { get; set; } = "zr.prepare_for_infection";
    public string FirstInfectionBegunSound { get; set; } = "zr.inf_begun";
    public string InfectionCountdownWorldSound { get; set; } = "zr.siren_14s";
    public float InfectionCountdownWorldSoundHeightOffset { get; set; } = 256f;
    public string InfectionHitSound { get; set; } = "zr.amb.zombie_voice_idle";
    public string[] ExtraInfectionHitSounds { get; set; } = ["zr.claw.hit"];
    public string ZombiePainSound { get; set; } = "zr.amb.zombie_pain";
    public string[] ExtraZombiePainSounds { get; set; } = ["zr.zombie_attack_5"];
    public string ZombieDeathSound { get; set; } = "zr.amb.zombie_die";
    public string[] ExtraZombieDeathSounds { get; set; } = ["zr.zombie_attack_6"];
    public string ZombieIdleSound { get; set; } = "zr.amb.zombie_voice_idle";
    public string[] ExtraZombieIdleSounds { get; set; } = ["zr.zombie_attack_7"];
    public string ZombiesWinSound { get; set; } = "zr.amb.scream";
    public string[] ExtraZombiesWinSounds { get; set; } = ["zr.zombie_attack_9"];
    public float ZombiePainMinIntervalSeconds { get; set; } = 1.2f;
    public float ZombieIdleIntervalSeconds { get; set; } = 14f;
}

public class ZombieMeleeVisualConfig
{
    public string ZombieMeleeWeaponName { get; set; } = "weapon_knife";
    public int ZombieMeleeItemDefinitionIndex { get; set; } = 516;
    public bool HideZombieKnifeModel { get; set; } = true;
    public bool EnableZombieKnifeReplacementModel { get; set; } = false;
    public string ZombieKnifeReplacementModelPath { get; set; } = "models/zombiemod/viewmodels/v_invisible_knife.vmdl";
    public string[] ZombieClawSoundResources { get; set; } = [];
    public string ZombieClawSlashSound { get; set; } = "zr.claw.slash";
    public string ZombieClawHitSound { get; set; } = "zr.claw.hit";
}

public class BlockadeConfig
{
    public bool Enabled { get; set; } = true;
    public bool DebugLogging { get; set; } = true;
    public string Command { get; set; } = "block";
    public string SmallArgument { get; set; } = "small";
    public string MainModel { get; set; } = "models/props/de_vertigo/pallet_cinderblock01.vmdl";
    public string SmallModel { get; set; } = "models/props/de_nuke/hr_nuke/nuke_concrete_barrier/nuke_concrete_block128.vmdl";
    public int MainCost { get; set; } = 2000;
    public int SmallCost { get; set; } = 1200;
    public int MainHits { get; set; } = 8;
    public int SmallHits { get; set; } = 5;
    public float PlacementDistance { get; set; } = 115f;
    public float PlacementHeightOffset { get; set; } = 0f;
    public float PlacementPlayerClearance { get; set; } = 72f;
    public float ZombieHitRange { get; set; } = 110f;
    public float ZombieHitCooldownSeconds { get; set; } = 0.45f;
    public int MaxPlacedPerPlayer { get; set; } = 2;
    public int PreviewAlpha { get; set; } = 120;
}

public class CommandsConfig
{
    public string Help { get; set; } = "help";
    public string XP { get; set; } = "xp";
    public string Shop { get; set; } = "shop";
    public string Progression { get; set; } = "progression";
    public string Stats { get; set; } = "stats";
    public string Abilities { get; set; } = "abilities";
    public string Bind { get; set; } = "bind";
    public string Weapons { get; set; } = "weapons";
    public string Guns { get; set; } = "guns";
    public string Buy { get; set; } = "buy";
    public string Money { get; set; } = "money";
    public string Level { get; set; } = "level";
    public string Zombies { get; set; } = "zombies";
    public string SwitchZombie { get; set; } = "zombie";
    public string DefaultZombie { get; set; } = "zdefault";
    public string Humans { get; set; } = "humans";
    public string SwitchHuman { get; set; } = "human";
    public string DefaultHuman { get; set; } = "hdefault";
}

public class ChatConfig
{
    public string ZombiePrefix { get; set; } = $"{ChatColors.Red}[ZOMBIE]{ChatColors.Default}";
    public string HumanPrefix { get; set; } = $"{ChatColors.LightBlue}[HUMAN]{ChatColors.Default}";
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
            Health = 6000,
            SpeedModifier = 1.0f,
            Damage = 25,
            Gravity = 1.0f,
            PlayerModel = ReclaimPlayerModels.ModelPaths.ClassicZombie,
            DefaultAbilities = [AbilityType.Pounce],
            UnlockableAbilities = [AbilityType.Berserk, AbilityType.HealthRegen]
        },
        new Zombie
        {
            Id = "molong",
            Name = "VIP Molong",
            Health = 6000,
            SpeedModifier = 1.05f,
            Damage = 30,
            Gravity = 0.9f,
            PlayerModel = ReclaimPlayerModels.ModelPaths.Molong,
            DefaultAbilities = [AbilityType.Pounce, AbilityType.MultiJump],
            UnlockableAbilities = [AbilityType.Berserk, AbilityType.Invisibility]
        },
        new Zombie
        {
            Id = "runner",
            Name = "Runner",
            Health = 6000,
            SpeedModifier = 1.3f,
            Damage = 20,
            Gravity = 1.0f,
            PlayerModel = ReclaimPlayerModels.ModelPaths.RunnerZombie,
            DefaultAbilities = [AbilityType.SpeedBoost],
            UnlockableAbilities = [AbilityType.Pounce]
        },
        new Zombie
        {
            Id = "brute",
            Name = "Brute",
            Health = 6000,
            SpeedModifier = 0.85f,
            Damage = 40,
            Gravity = 1.0f,
            PlayerModel = ReclaimPlayerModels.ModelPaths.BruteZombie,
            DefaultAbilities = [AbilityType.Berserk],
            UnlockableAbilities = [AbilityType.HealthRegen, AbilityType.SelfDestruct]
        },
        new Zombie
        {
            Id = "cultist",
            Name = "Cultist",
            Health = 6000,
            SpeedModifier = 1.0f,
            Damage = 20,
            Gravity = 1.0f,
            PlayerModel = ReclaimPlayerModels.ModelPaths.CultistZombie,
            DefaultAbilities = [AbilityType.CultistHex],
            UnlockableAbilities = [AbilityType.Invisibility, AbilityType.FrostBolt]
        },
        new Zombie
        {
            Id = "frozen",
            Name = "Frozen Zombie",
            Health = 6000,
            SpeedModifier = 0.95f,
            Damage = 22,
            Gravity = 1.0f,
            PlayerModel = ReclaimPlayerModels.ModelPaths.FrozenZombie,
            DefaultAbilities = [AbilityType.FrostBolt],
            UnlockableAbilities = [AbilityType.HealthRegen, AbilityType.CultistHex]
        },
        new Zombie
        {
            Id = "lurker",
            Name = "Lurker",
            Health = 6000,
            SpeedModifier = 1.2f,
            Damage = 18,
            Gravity = 0.75f,
            PlayerModel = ReclaimPlayerModels.ModelPaths.Lurker,
            DefaultAbilities = [AbilityType.LurkerCloak, AbilityType.Pounce, AbilityType.WallClimb],
            UnlockableAbilities = [AbilityType.Invisibility, AbilityType.SpeedBoost]
        }
    };

    public int StartingLevel { get; set; } = 1;
    public int MaxLevel { get; set; } = 5;
    public int InfectionHitsRequired { get; set; } = 3;
    public int XPPerKill { get; set; } = 25;
    public int XPPerLevel { get; set; } = 100;
    public int MaxAbilitiesPerZombie { get; set; } = 5;
}


public class HumanConfig
{
    public string PlayerModel { get; set; } = ReclaimPlayerModels.ModelPaths.EarthGovSecurity;
    public string DefaultHumanClassId { get; set; } = "security";
    public string[] DefaultWeapons { get; set; } = ["weapon_knife", "weapon_usp_silencer"];
    public int StartingMoney { get; set; } = 6000;
    public bool UnlimitedMoney { get; set; } = false;
    public int NativeMoneyDisplayCap { get; set; } = 65535;
    public bool BuyAnywhereAnytime { get; set; } = true;
    public int BuyTimeMinutes { get; set; } = 9999;
    public HumanWeaponShopConfig WeaponShop { get; set; } = new();
    public int MoneyPerKill { get; set; } = 350;
    public int MoneyPerInfection { get; set; } = 500;
    public int MoneyPerRound { get; set; } = 1500;
    public float ZombieKnockbackForce { get; set; } = 420f;
    public float ZombieKnockbackUpForce { get; set; } = 90f;
    public int InfectionHitsRequired { get; set; } = 3;
    public int StartingLevel { get; set; } = 1;
    public int MaxLevel { get; set; } = 5;
    public int XPPerKill { get; set; } = 15;
    public int XPPerLevel { get; set; } = 100;

    public HumanClass[] HumanClasses { get; set; } =
    [
        new HumanClass
        {
            Id = "security",
            Name = "EarthGov Security",
            PlayerModel = ReclaimPlayerModels.ModelPaths.EarthGovSecurity,
            Health = 100,
            SpeedModifier = 1.0f,
            Gravity = 1.0f,
            DefaultWeapons = ["weapon_knife", "weapon_usp_silencer"],
            StartingMoney = 6000
        },
        new HumanClass
        {
            Id = "tac",
            Name = "Tactical Trooper",
            PlayerModel = ReclaimPlayerModels.ModelPaths.TacticalTrooper,
            Health = 105,
            SpeedModifier = 1.02f,
            Gravity = 1.0f,
            DefaultWeapons = ["weapon_knife", "weapon_usp_silencer"],
            StartingMoney = 6000
        },
        new HumanClass
        {
            Id = "hunter",
            Name = "Hunter",
            PlayerModel = ReclaimPlayerModels.ModelPaths.VectorHunter,
            Health = 100,
            SpeedModifier = 1.18f,
            Gravity = 0.95f,
            InfectionHitsRequired = 3,
            DefaultWeapons = ["weapon_knife", "weapon_usp_silencer"],
            DefaultAbilities = [AbilityType.MultiJump],
            StartingMoney = 6000
        },
        new HumanClass
        {
            Id = "vip_heavy",
            Name = "VIP Heavy",
            PlayerModel = ReclaimPlayerModels.ModelPaths.DeadpoolReborn,
            Health = 150,
            SpeedModifier = 0.95f,
            Gravity = 1.0f,
            InfectionHitsRequired = 5,
            ZombieKnockbackForce = 760f,
            ZombieKnockbackUpForce = 130f,
            DefaultWeapons = ["weapon_knife", "weapon_deagle"],
            StartingMoney = 6000
        },
        new HumanClass
        {
            Id = "vip_tactical",
            Name = "VIP Tactical",
            PlayerModel = ReclaimPlayerModels.ModelPaths.GhostTactical,
            Health = 125,
            SpeedModifier = 1.05f,
            Gravity = 1.0f,
            InfectionHitsRequired = 4,
            ZombieKnockbackForce = 650f,
            ZombieKnockbackUpForce = 110f,
            DefaultWeapons = ["weapon_knife", "weapon_usp_silencer", "weapon_hegrenade", "weapon_smokegrenade"],
            StartingMoney = 6000
        }
    ];
}

public class HumanWeaponShopConfig
{
    public bool Enabled { get; set; } = true;
    public bool FreeWeapons { get; set; } = false;
    public bool AllowUnlistedWeaponNames { get; set; } = true;
    public bool RefillsAmmoByGivingWeapon { get; set; } = true;
    public int DefaultUnlistedWeaponCost { get; set; } = 2500;
    public int PageSize { get; set; } = 6;
    public WeaponShopItem[] Items { get; set; } = DefaultItems();

    private static WeaponShopItem[] DefaultItems()
    {
        return
        [
            Item("ak47", "AK-47", "weapon_ak47", "Rifles", 2700, ["ak"]),
            Item("m4a1", "M4A4", "weapon_m4a1", "Rifles", 3100, ["m4"]),
            Item("m4a1s", "M4A1-S", "weapon_m4a1_silencer", "Rifles", 2900, ["m4s", "m4a1-s"]),
            Item("famas", "FAMAS", "weapon_famas", "Rifles", 2050, []),
            Item("galil", "Galil AR", "weapon_galilar", "Rifles", 1800, ["galilar"]),
            Item("aug", "AUG", "weapon_aug", "Rifles", 3300, []),
            Item("sg556", "SG 553", "weapon_sg556", "Rifles", 3000, ["krieg"]),
            Item("awp", "AWP", "weapon_awp", "Rifles", 4750, []),
            Item("scout", "SSG 08", "weapon_ssg08", "Rifles", 1700, ["ssg08", "ssg"]),
            Item("scar20", "SCAR-20", "weapon_scar20", "Rifles", 5000, []),
            Item("g3sg1", "G3SG1", "weapon_g3sg1", "Rifles", 5000, []),

            Item("mac10", "MAC-10", "weapon_mac10", "SMGs", 1050, []),
            Item("mp9", "MP9", "weapon_mp9", "SMGs", 1250, []),
            Item("mp7", "MP7", "weapon_mp7", "SMGs", 1500, []),
            Item("mp5", "MP5-SD", "weapon_mp5sd", "SMGs", 1500, ["mp5sd"]),
            Item("ump45", "UMP-45", "weapon_ump45", "SMGs", 1200, ["ump"]),
            Item("p90", "P90", "weapon_p90", "SMGs", 2350, []),
            Item("bizon", "PP-Bizon", "weapon_bizon", "SMGs", 1400, []),

            Item("nova", "Nova", "weapon_nova", "Heavy", 1050, []),
            Item("xm1014", "XM1014", "weapon_xm1014", "Heavy", 2000, ["xm"]),
            Item("mag7", "MAG-7", "weapon_mag7", "Heavy", 1300, []),
            Item("sawedoff", "Sawed-Off", "weapon_sawedoff", "Heavy", 1100, []),
            Item("m249", "M249", "weapon_m249", "Heavy", 5200, []),
            Item("negev", "Negev", "weapon_negev", "Heavy", 1700, []),

            Item("usp", "USP-S", "weapon_usp_silencer", "Pistols", 200, ["usps", "usp-s"]),
            Item("p2000", "P2000", "weapon_hkp2000", "Pistols", 200, ["hkp2000"]),
            Item("glock", "Glock-18", "weapon_glock", "Pistols", 200, []),
            Item("p250", "P250", "weapon_p250", "Pistols", 300, []),
            Item("deagle", "Desert Eagle", "weapon_deagle", "Pistols", 700, ["deag"]),
            Item("revolver", "R8 Revolver", "weapon_revolver", "Pistols", 600, ["r8"]),
            Item("fiveseven", "Five-SeveN", "weapon_fiveseven", "Pistols", 500, ["57"]),
            Item("tec9", "Tec-9", "weapon_tec9", "Pistols", 500, []),
            Item("dualies", "Dual Berettas", "weapon_elite", "Pistols", 300, ["elite"]),

            Item("he", "HE Grenade", "weapon_hegrenade", "Grenades", 300, ["hegrenade"]),
            Item("flash", "Flashbang", "weapon_flashbang", "Grenades", 200, ["flashbang"]),
            Item("smoke", "Smoke Grenade", "weapon_smokegrenade", "Grenades", 300, ["smokegrenade"]),
            Item("molotov", "Molotov", "weapon_molotov", "Grenades", 400, ["molly"]),
            Item("inc", "Incendiary", "weapon_incgrenade", "Grenades", 500, ["incgrenade"]),
            Item("decoy", "Decoy", "weapon_decoy", "Grenades", 50, []),
            Item("zeus", "Zeus x27", "weapon_taser", "Gear", 200, ["taser"]),
            Item("knife", "Knife", "weapon_knife", "Gear", 0, [])
        ];
    }

    private static WeaponShopItem Item(string id, string name, string weaponName, string category, int cost, string[] aliases)
    {
        return new WeaponShopItem
        {
            Id = id,
            Name = name,
            WeaponName = weaponName,
            Category = category,
            Cost = cost,
            Aliases = aliases
        };
    }
}

public class WeaponShopItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string WeaponName { get; set; } = "";
    public string Category { get; set; } = "Weapons";
    public int Cost { get; set; }
    public string[] Aliases { get; set; } = [];
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
    public WallClimbAbilityConfig WallClimb { get; set; } = new();

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
            AbilityType.WallClimb => WallClimb,
            _ => new AbilitySettingsConfig()
        };
    }
}

public class AbilitySettingsConfig
{
    public float CooldownSeconds { get; set; } = 10f;
    public float DurationSeconds { get; set; } = 5f;
    public int UnlockCost { get; set; } = 100;
    public string ActivationSound { get; set; } = "";
    public string[] ActivationSounds { get; set; } = [];
    public string[] ExtraActivationSounds { get; set; } = [];
}

public class PounceAbilityConfig : AbilitySettingsConfig
{
    public PounceAbilityConfig()
    {
        CooldownSeconds = 8f;
        DurationSeconds = 0.1f;
        UnlockCost = 400;
        ActivationSound = "zr.amb.scream";
        ActivationSounds = ["zr.zombie_attack_1"];
        ExtraActivationSounds = ["zr.zombie_attack_1"];
    }

    public float Force { get; set; } = 780f;
    public float UpForce { get; set; } = 390f;
    public float TrailMaxDurationSeconds { get; set; } = 4f;
    public float TrailSegmentLifetimeSeconds { get; set; } = 8f;
    public float TrailFadeAfterLandingSeconds { get; set; } = 0.75f;
    public float TrailTickIntervalSeconds { get; set; } = 0.035f;
    public float TrailMinSegmentDistance { get; set; } = 14f;
    public float TrailHeightOffset { get; set; } = 16f;
    public float TrailBeamWidth { get; set; } = 0.45f;
    public string TrailBeamMaterial { get; set; } = "materials/sprites/laserbeam.vtex";
    public string TrailMarkerParticle { get; set; } = "";
    public float TrailMarkerRadiusScale { get; set; } = 0.6f;
}

public class SpeedBoostAbilityConfig : AbilitySettingsConfig
{
    public SpeedBoostAbilityConfig()
    {
        ActivationSound = "zr.amb.zombie_voice_idle";
        ActivationSounds = ["zr.zombie_attack_3"];
        ExtraActivationSounds = ["zr.zombie_attack_3"];
    }

    public float SpeedMultiplier { get; set; } = 1.5f;
}

public class InvisibilityAbilityConfig : AbilitySettingsConfig
{
    public InvisibilityAbilityConfig()
    {
        CooldownSeconds = 20f;
        DurationSeconds = 6f;
        UnlockCost = 500;
        ActivationSound = "zr.amb.zombie_voice_idle";
        ActivationSounds = ["zr.zombie_attack_5"];
        ExtraActivationSounds = ["zr.zombie_attack_5"];
    }

    public int Alpha { get; set; } = 60;
}

public class HealthRegenAbilityConfig : AbilitySettingsConfig
{
    public HealthRegenAbilityConfig()
    {
        ActivationSound = "zr.amb.zombie_voice_idle";
        ActivationSounds = ["zr.zombie_attack_7"];
        ExtraActivationSounds = ["zr.zombie_attack_7"];
    }

    public int HealPerTick { get; set; } = 20;
    public float TickIntervalSeconds { get; set; } = 1f;
}

public class BerserkAbilityConfig : AbilitySettingsConfig
{
    public BerserkAbilityConfig()
    {
        ActivationSound = "zr.amb.scream";
        ActivationSounds = ["zr.zombie_attack_9"];
        ExtraActivationSounds = ["zr.zombie_attack_9"];
    }

    public float SpeedMultiplier { get; set; } = 1.5f;
}

public class SelfDestructAbilityConfig : AbilitySettingsConfig
{
    public SelfDestructAbilityConfig()
    {
        CooldownSeconds = 20f;
        DurationSeconds = 0.1f;
        UnlockCost = 300;
        ActivationSound = "zr.amb.scream";
        ActivationSounds = ["zr.zombie_attack_4"];
        ExtraActivationSounds = ["zr.zombie_attack_4"];
    }

    public float Radius { get; set; } = 400f;
    public float Damage { get; set; } = 40f;
    public float Force { get; set; } = 4000f;
    public string ExplosionSound { get; set; } = "";
}

public class FrostBoltAbilityConfig : AbilitySettingsConfig
{
    public FrostBoltAbilityConfig()
    {
        CooldownSeconds = 12f;
        DurationSeconds = 3f;
        UnlockCost = 350;
    }

    public float Speed { get; set; } = 1350f;
    public float Range { get; set; } = 1400f;
    public float HitRadius { get; set; } = 38f;
    public float SpawnForwardOffset { get; set; } = 42f;
    public float SpawnUpOffset { get; set; } = 54f;
    public float TickIntervalSeconds { get; set; } = 0.025f;
    public float HitParticleLifetimeSeconds { get; set; } = 1.5f;
    public float BeamWidth { get; set; } = 3f;
    public float BeamLifetimeSeconds { get; set; } = 0.12f;
    public float AimConeDot { get; set; } = 0.82f;
    public float SlowMultiplier { get; set; } = 0.55f;
    public string CastParticle { get; set; } = "particles/weapons/cs_weapon_fx/weapon_muzzle_flash_taser.vpcf";
    public string ProjectileParticle { get; set; } = "particles/weapons/cs_weapon_fx/weapon_taser_glow.vpcf";
    public string HitParticle { get; set; } = "particles/weapons/cs_weapon_fx/weapon_taser_glow_impact.vpcf";
    public string BeamMaterial { get; set; } = "materials/sprites/laserbeam.vtex";
    public string CastSound { get; set; } = "zr.amb.scream";
    public string[] CastSounds { get; set; } = ["zr.zombie_attack_10"];
    public string[] ExtraCastSounds { get; set; } = ["zr.zombie_attack_10"];
    public string HitSound { get; set; } = "zr.amb.zombie_pain";
    public string[] HitSounds { get; set; } = ["zr.zombie_attack_6"];
    public string[] ExtraHitSounds { get; set; } = ["zr.zombie_attack_6"];
    public string HitMessage { get; set; } = "You were chilled by Frost Bolt.";
}

public class CultistHexAbilityConfig : AbilitySettingsConfig
{
    public CultistHexAbilityConfig()
    {
        CooldownSeconds = 18f;
        DurationSeconds = 4f;
        UnlockCost = 450;
        ActivationSound = "zr.amb.zombie_voice_idle";
        ActivationSounds = ["zr.zombie_attack_11"];
        ExtraActivationSounds = ["zr.zombie_attack_11"];
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

    public float StationaryDelaySeconds { get; set; } = 0.6f;
    public float FadeInSeconds { get; set; } = 0.9f;
    public float FadeOutSeconds { get; set; } = 0.25f;
    public float MovementThreshold { get; set; } = 12f;
    public float TickIntervalSeconds { get; set; } = 0.03f;
    public int Alpha { get; set; } = 45;
    public bool ApplyToWeapons { get; set; } = true;
    public bool ApplyToViewModel { get; set; } = true;
}

public class WallClimbAbilityConfig : AbilitySettingsConfig
{
    public WallClimbAbilityConfig()
    {
        CooldownSeconds = 14f;
        DurationSeconds = 0f;
        UnlockCost = 350;
        ActivationSound = "zr.amb.zombie_voice_idle";
        ActivationSounds = ["zr.zombie_attack_8"];
        ExtraActivationSounds = ["zr.zombie_attack_8"];
    }

    public bool RequireWallContact { get; set; } = true;
    public bool RequireAirborne { get; set; } = true;
    public float WallTraceDistance { get; set; } = 72f;
    public float WallTraceHeightOffset { get; set; } = 36f;
    public uint WallTraceMask { get; set; } = 0xC3001;
    public string WallRequiredMessage { get; set; } = "Wall Cling needs a nearby wall.";
    public string AirborneRequiredMessage { get; set; } = "Wall Cling only works while airborne.";
    public string CancelMessage { get; set; } = "Wall Cling released.";
}


