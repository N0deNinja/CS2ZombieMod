using ZombieModPlugin.Abilities;

namespace ZombieModPlugin.Humans.Models;

public class HumanClass
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string PlayerModel { get; set; } = "";
    public int Health { get; set; } = 100;
    public float SpeedModifier { get; set; } = 1.0f;
    public float Gravity { get; set; } = 1.0f;
    public int? InfectionHitsRequired { get; set; }
    public float? ZombieKnockbackForce { get; set; }
    public float? ZombieKnockbackUpForce { get; set; }
    public string[] DefaultWeapons { get; set; } = ["weapon_knife", "weapon_usp_silencer"];
    public AbilityType[] DefaultAbilities { get; set; } = [];
    public AbilityType[] UnlockableAbilities { get; set; } = [];
    public int? StartingMoney { get; set; }

    public HumanClass()
    {
    }
}
