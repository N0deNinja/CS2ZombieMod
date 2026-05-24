using CounterStrikeSharp.API.Modules.Utils;

namespace ZombieModPlugin.Formatting;

public static class ChatText
{
    public static string ModPrefix => $"{ChatColors.LightPurple}[ZM]{ChatColors.Default}";
    public static string MoneyPrefix => $"{ChatColors.LightPurple}[ZM MONEY]{ChatColors.Default}";
    public static string XpPrefix => $"{ChatColors.LightPurple}[ZM XP]{ChatColors.Default}";
    public static string HumanPrefix => $"{ChatColors.LightBlue}[HUMAN]{ChatColors.Default}";
    public static string ZombiePrefix => $"{ChatColors.Red}[ZOMBIE]{ChatColors.Default}";
    public static string AdminPrefix => $"{ChatColors.Gold}[ZM ADMIN]{ChatColors.Default}";

    public static string Human(string message) => $"{HumanPrefix} {message}";
    public static string Zombie(string message) => $"{ZombiePrefix} {message}";
    public static string Info(string message) => $"{ModPrefix} {message}";
    public static string Error(string message) => $"{ChatColors.Red}[ZM]{ChatColors.Default} {message}";
    public static string Admin(string message) => $"{AdminPrefix} {message}";

    public static string Money(int amount) => $"{ChatColors.Lime}${amount}{ChatColors.Default}";
    public static string Number(int value) => $"{ChatColors.Lime}{value}{ChatColors.Default}";
    public static string Command(string command) => $"{ChatColors.Lime}{command}{ChatColors.Default}";
    public static string Good(string message) => $"{ChatColors.Lime}{message}{ChatColors.Default}";
    public static string Warn(string message) => $"{ChatColors.Yellow}{message}{ChatColors.Default}";
    public static string Bad(string message) => $"{ChatColors.Red}{message}{ChatColors.Default}";
    public static string Name(string message) => $"{ChatColors.LightBlue}{message}{ChatColors.Default}";
}
