namespace ZombieModPlugin.Abilities;

public class Ability
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public float Cooldown { get; set; }
    public float Duration { get; set; }
    public int UnlockCost { get; set; }



    public Ability(string id, string name, string description, float cooldown, int unlockCost, float duration)
    {
        Id = id;
        Name = name;
        Description = description;
        Cooldown = cooldown;
        UnlockCost = unlockCost;
        Duration = duration;
    }

    public virtual void Execute(AbilityExecutionContext context) { }
}