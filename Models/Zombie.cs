using ZombieModPlugin.Models;

public class Zombie
{

    public string Name { get; set; }
    public int Health { get; set; }
    public float Speed { get; set; }
    public int Damage { get; set; }

    public float Gravity { get; set; }

    public AbilityType[] DefaultAbilities { get; set; }
    public AbilityType[] UnlockableAbilities { get; set; }

    public Zombie() { }
    public Zombie(string name, int health, float speed, int damage, float gravity, AbilityType[] defaultAbilities, AbilityType[] unlockableAbilities)
    {
        Name = name;
        Health = health;
        Speed = speed;
        Damage = damage;
        Gravity = gravity;
        DefaultAbilities = defaultAbilities;
        UnlockableAbilities = unlockableAbilities;
    }
}