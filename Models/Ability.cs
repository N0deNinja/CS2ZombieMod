using CounterStrikeSharp.API.Core;

public class Ability
{
    public string Name { get; set; }
    public string Description { get; set; }
    public float Cooldown { get; set; }
    public int UnlockCost { get; set; }


    public Ability(string name, string description, float cooldown, int unlockCost)
    {
        Name = name;
        Description = description;
        Cooldown = cooldown;
        UnlockCost = unlockCost;
    }

    public virtual void Execute(CCSPlayerController player)
    {

    }
}