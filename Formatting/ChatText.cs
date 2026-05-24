using CounterStrikeSharp.API.Modules.Utils;
using ReclaimCS.Shared.Chat;

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

    public static string Money(int amount) => ReclaimChatText.Money(amount);
    public static string Number(int value) => ReclaimChatText.Number(value);
    public static string Command(string command) => ReclaimChatText.Command(command);
    public static string Good(string message) => ReclaimChatText.Good(message);
    public static string Warn(string message) => ReclaimChatText.Warn(message);
    public static string Bad(string message) => ReclaimChatText.Bad(message);
    public static string Name(string message) => ReclaimChatText.Class(message);
}
