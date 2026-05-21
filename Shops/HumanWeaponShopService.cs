using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using ZombieModPlugin.Configs;
using ZombieModPlugin.Progression.Services;
using ZombieModPlugin.States;

namespace ZombieModPlugin.Shops;

public sealed class HumanWeaponShopService
{
    private readonly BaseConfig _config;
    private readonly ProgressionService _progressionService;

    public HumanWeaponShopService(BaseConfig config, ProgressionService progressionService)
    {
        _config = config;
        _progressionService = progressionService;
    }

    public void PrintMenu(CommandInfo command, PlayerState state, int page, string category = "")
    {
        var items = GetItems(category).ToList();
        var pageSize = Math.Max(3, _config.HumanConfig.WeaponShop.PageSize);
        var totalPages = Math.Max(1, (int)Math.Ceiling(items.Count / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        var currentCategory = string.IsNullOrWhiteSpace(category) ? "All Weapons" : category;

        Header(command, $"Human Weapon Shop - {currentCategory}");
        Reply(command, $"{Gold("Balance")} {Green($"${state.Money}")} | Earn money by killing zombies and surviving as human.");
        Reply(command, $"Buy anywhere: {Command("!buy awp")} or native {Command("B")}. Chat shop uses your persistent balance.");
        Reply(command, $"Categories: {string.Join(", ", GetCategories().Select(Command))}");

        foreach (var item in items.Skip((page - 1) * pageSize).Take(pageSize))
        {
            var price = _config.HumanConfig.WeaponShop.FreeWeapons
                ? Green("FREE")
                : Gold($"${Math.Max(0, item.Cost)}");
            Reply(command, $"{Id(item.Id)} {Human(item.Name)} {price} | {item.WeaponName} | {Command($"!buy {item.Id}")}");
        }

        Footer(command, $"Page {page}/{totalPages}. Next: !weapons {NextPageArgument(category, Math.Min(totalPages, page + 1))}");
    }

    public ShopPurchaseResult TryGiveWeapon(CCSPlayerController player, PlayerState state, string requestedWeapon)
    {
        if (!_config.HumanConfig.WeaponShop.Enabled)
            return ShopPurchaseResult.Fail("Weapon shop is disabled.");

        if (!player.IsValid || !player.PawnIsAlive)
            return ShopPurchaseResult.Fail("You must be alive to buy weapons.");

        if (state.IsZombie)
            return ShopPurchaseResult.Fail("Zombies cannot buy weapons.");

        if (!TryResolveItem(requestedWeapon, out var item, out var weaponName))
            return ShopPurchaseResult.Fail($"Unknown weapon: {requestedWeapon}. Use !weapons for the list.");

        _progressionService.SyncNativeMoney(player, state, save: true);
        var cost = _config.HumanConfig.WeaponShop.FreeWeapons
            ? 0
            : Math.Max(0, item?.Cost ?? _config.HumanConfig.WeaponShop.DefaultUnlistedWeaponCost);
        if (state.Money < cost)
            return ShopPurchaseResult.Fail($"Need ${cost}. You have ${state.Money}.");

        try
        {
            player.GiveNamedItem(weaponName);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ZombieMod] Failed to give weapon '{weaponName}' to {player.PlayerName}: {ex}");
            return ShopPurchaseResult.Fail($"Could not give {weaponName}. Check the weapon id.");
        }

        if (!_progressionService.TrySpendMoney(player, state, cost, out var error))
            return ShopPurchaseResult.Fail(error);

        var displayName = item?.Name ?? weaponName;
        var priceText = cost > 0 ? $" for ${cost}" : "";
        return ShopPurchaseResult.Ok($"Bought {displayName}{priceText}. Balance: ${state.Money}.");
    }

    public IEnumerable<string> GetCategories()
    {
        return _config.HumanConfig.WeaponShop.Items
            .Select(item => item.Category)
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<WeaponShopItem> GetItems(string category)
    {
        var items = _config.HumanConfig.WeaponShop.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.WeaponName));

        if (string.IsNullOrWhiteSpace(category))
            return items;

        var normalized = Normalize(category);
        return items.Where(item => Normalize(item.Category) == normalized);
    }

    private bool TryResolveItem(string value, out WeaponShopItem? item, out string weaponName)
    {
        item = null;
        weaponName = string.Empty;

        var normalized = Normalize(value);
        foreach (var candidate in _config.HumanConfig.WeaponShop.Items)
        {
            if (Normalize(candidate.Id) == normalized
                || Normalize(candidate.Name) == normalized
                || Normalize(candidate.WeaponName) == normalized
                || candidate.Aliases.Any(alias => Normalize(alias) == normalized))
            {
                item = candidate;
                weaponName = candidate.WeaponName;
                return true;
            }
        }

        if (!_config.HumanConfig.WeaponShop.AllowUnlistedWeaponNames)
            return false;

        var raw = value.Trim().ToLowerInvariant();
        if (!IsSafeWeaponToken(raw))
            return false;

        weaponName = raw.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase)
            ? raw
            : $"weapon_{raw}";
        return true;
    }

    private static bool IsSafeWeaponToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
            return false;

        return value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
    }

    private static string NextPageArgument(string category, int page)
    {
        return string.IsNullOrWhiteSpace(category)
            ? page.ToString()
            : $"{category} {page}";
    }

    private static void Header(CommandInfo command, string title)
    {
        command.ReplyToCommand($"{ChatColors.Gold}==== {ChatColors.LightBlue}{title}{ChatColors.Gold} ===={ChatColors.Default}");
    }

    private static void Footer(CommandInfo command, string message)
    {
        command.ReplyToCommand($"{ChatColors.Gold}-- {ChatColors.Default}{message}");
    }

    private static void Reply(CommandInfo command, string message)
    {
        command.ReplyToCommand($"{ChatColors.LightPurple}[ZM]{ChatColors.Default} {message}");
    }

    private static string Command(string command)
    {
        return $"{ChatColors.Lime}{command}{ChatColors.Default}";
    }

    private static string Id(string id)
    {
        return $"{ChatColors.Yellow}[{id}]{ChatColors.Default}";
    }

    private static string Gold(object value)
    {
        return $"{ChatColors.Gold}{value}{ChatColors.Default}";
    }

    private static string Green(object value)
    {
        return $"{ChatColors.Lime}{value}{ChatColors.Default}";
    }

    private static string Human(string value)
    {
        return $"{ChatColors.LightBlue}{value}{ChatColors.Default}";
    }

    private static string Normalize(string value)
    {
        return value
            .Trim()
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty)
            .ToLowerInvariant();
    }
}

public readonly record struct ShopPurchaseResult(bool Success, string Message)
{
    public static ShopPurchaseResult Ok(string message)
    {
        return new ShopPurchaseResult(true, message);
    }

    public static ShopPurchaseResult Fail(string message)
    {
        return new ShopPurchaseResult(false, message);
    }
}
