using ZombieModPlugin.Abilities;
namespace ZombieModPlugin.Zombies.Models;

public class Zombie
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public int Health { get; set; }
    public float Speed { get; set; }
    public int Damage { get; set; }

    public float Gravity { get; set; }

    public required AbilityType[] DefaultAbilities { get; set; }
    public required AbilityType[] UnlockableAbilities { get; set; }

    // Empty constructor for JSON Parsing
    public Zombie() { }

    public Zombie(string id, string name, int health, float speed, int damage, float gravity, AbilityType[] defaultAbilities, AbilityType[] unlockableAbilities)
    {
        Id = id;
        Name = name;
        Health = health;
        Speed = speed;
        Damage = damage;
        Gravity = gravity;
        DefaultAbilities = defaultAbilities;
        UnlockableAbilities = unlockableAbilities;
    }
}