using ZombieModPlugin.Abilities;

public class Zombie
{
    public string Id { get; set; }
    public string Name { get; set; }
    public int Health { get; set; }
    public float Speed { get; set; }
    public int Damage { get; set; }

    public float Gravity { get; set; }

    public AbilityType[] DefaultAbilities { get; set; }
    public AbilityType[] UnlockableAbilities { get; set; }

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