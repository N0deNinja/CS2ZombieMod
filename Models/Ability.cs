using CounterStrikeSharp.API.Core;

public class Ability
{
    public string Name { get; set; }
    public string Description { get; set; }
    public float Cooldown { get; set; }
    public int UnlockCost { get; set; } = 0;


    public Ability(string name, string description, float cooldown)
    {
        Name = name;
        Description = description;
        Cooldown = cooldown;
    }

    public virtual void Execute(CCSPlayerController player)
    {

    }
}