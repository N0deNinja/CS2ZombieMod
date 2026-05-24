using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using ZombieModPlugin.Abilities;
using ZombieModPlugin.Configs;
using ZombieModPlugin.Progression.Models;
using ZombieModPlugin.Progression.Services;
using ZombieModPlugin.States;

namespace ZombieModPlugin.Progression.Menus;

public sealed class ProgressionMenuRenderer
{
    private const int PageSize = 5;
    private const int AbilityPageSize = 3;

    private readonly BaseConfig _config;
    private readonly ProgressionService _progressionService;

    public ProgressionMenuRenderer(BaseConfig config, ProgressionService progressionService)
    {
        _config = config;
        _progressionService = progressionService;
    }

    public void PrintMain(CommandInfo command, PlayerState state)
    {
        Header(command, "Zombie Mod Progression");
        Reply(command, $"{Gold("Account")} Level {Green(state.GlobalLevel)} {FormatXp(state.GlobalXP, _progressionService.GetRequiredGlobalXpForNextLevel(state))}");
        Reply(command, $"{Gold("Money")} {Green($"${state.Money}")} | {Command("!weapons")} human weapon shop | {Command("!buy awp")}");
        Reply(command, $"{Command("!xp")} overview | {Command("!zombies")} zombie classes | {Command("!humans")} human classes");
        Reply(command, $"{Command("!abilities")} unlocks | {Command("!abilities equip")} loadout | {Command("!bind 1 mouse4")} quick ability bind");
        Reply(command, $"Current defaults: {Zombie(_progressionService.GetPreferredZombie(state).Name)} / {Human(_progressionService.GetPreferredHuman(state).Name)}");
        Footer(command, "Use page numbers like !zombies 2.");
    }

    public void PrintOverview(CommandInfo command, PlayerState state)
    {
        Header(command, "XP and Stats");
        Reply(command, $"{Gold("Global")} Level {Green(state.GlobalLevel)} {FormatXp(state.GlobalXP, _progressionService.GetRequiredGlobalXpForNextLevel(state))}");
        Reply(command, $"{Gold("Money")} {Green($"${state.Money}")} persistent | {Command("!money")} balance | {Command("!weapons")} shop");

        var zombie = state.SelectedZombieType ?? _progressionService.GetPreferredZombie(state);
        var zombieProgression = _progressionService.GetZombieProgression(state, zombie.Id);
        Reply(command, $"{Zombie(zombie.Name)} Level {Green(zombieProgression.Level)} {FormatXp(zombieProgression.XP, _progressionService.GetRequiredClassXpForNextLevel(state, ProgressionClassRole.Zombie, zombie.Id))}");

        var human = state.SelectedHumanClass ?? _progressionService.GetPreferredHuman(state);
        var humanProgression = _progressionService.GetHumanProgression(state, human.Id);
        Reply(command, $"{Human(human.Name)} Level {Green(humanProgression.Level)} {FormatXp(humanProgression.XP, _progressionService.GetRequiredClassXpForNextLevel(state, ProgressionClassRole.Human, human.Id))}");

        var stats = new[]
        {
            ("infections", "Infections"),
            ("zombie_kills", "Zombie kills"),
            ("wins", "Wins"),
            ("survivals", "Survivals"),
            ("assists", "Assists")
        };

        Reply(command, string.Join($"{ChatColors.Default} | ", stats.Select(stat =>
        {
            state.Statistics.TryGetValue(stat.Item1, out var value);
            return $"{Gold(stat.Item2)} {Green(value)}";
        })));
    }

    public void PrintZombieClasses(CommandInfo command, PlayerState state, int page)
    {
        Header(command, "Zombie Classes");
        Reply(command, $"Unlock with {Command("!zombies unlock <id>")} or select with {Command("!zombie <id>")}.");
        PrintPaged(
            command,
            _progressionService.GetZombieClasses().ToList(),
            Math.Max(1, page),
            zombie =>
            {
                var progression = _progressionService.GetZombieProgression(state, zombie.Id);
                var isUnlocked = _progressionService.IsClassUnlocked(state, ProgressionClassRole.Zombie, zombie.Id);
                var isReady = _progressionService.IsClassUnlockReady(state, ProgressionClassRole.Zombie, zombie.Id);
                var selected = string.Equals(_progressionService.GetPreferredZombie(state).Id, zombie.Id, StringComparison.OrdinalIgnoreCase)
                    ? $" {Gold("(default)")}"
                    : "";
                var status = isUnlocked
                    ? Green("UNLOCKED")
                    : isReady
                        ? $"{ChatColors.Yellow}READY TO UNLOCK{ChatColors.Default}"
                        : Red("LOCKED");
                var requirement = isUnlocked
                    ? "owned"
                    : _progressionService.FormatClassRequirements(state, ProgressionClassRole.Zombie, zombie.Id);

                return $"{Id(zombie.Id)} {Zombie(zombie.Name)} {status}{selected} HP {Green(zombie.Health)} SPD {Green(zombie.SpeedModifier.ToString("0.##"))} | L{Green(progression.Level)} {FormatXp(progression.XP, _progressionService.GetRequiredClassXpForNextLevel(state, ProgressionClassRole.Zombie, zombie.Id))} | {requirement}";
            },
            "!zombies");
    }

    public void PrintHumanClasses(CommandInfo command, PlayerState state, int page)
    {
        Header(command, "Human Classes");
        Reply(command, $"Unlock with {Command("!humans unlock <id>")} or select with {Command("!human <id>")}.");
        PrintPaged(
            command,
            _progressionService.GetHumanClasses().ToList(),
            Math.Max(1, page),
            human =>
            {
                var progression = _progressionService.GetHumanProgression(state, human.Id);
                var isUnlocked = _progressionService.IsClassUnlocked(state, ProgressionClassRole.Human, human.Id);
                var isReady = _progressionService.IsClassUnlockReady(state, ProgressionClassRole.Human, human.Id);
                var selected = string.Equals(_progressionService.GetPreferredHuman(state).Id, human.Id, StringComparison.OrdinalIgnoreCase)
                    ? $" {Gold("(default)")}"
                    : "";
                var status = isUnlocked
                    ? Green("UNLOCKED")
                    : isReady
                        ? $"{ChatColors.Yellow}READY TO UNLOCK{ChatColors.Default}"
                        : Red("LOCKED");
                var requirement = isUnlocked
                    ? "owned"
                    : _progressionService.FormatClassRequirements(state, ProgressionClassRole.Human, human.Id);

                return $"{Id(human.Id)} {Human(human.Name)} {status}{selected} HP {Green(human.Health)} SPD {Green(human.SpeedModifier.ToString("0.##"))} | L{Green(progression.Level)} {FormatXp(progression.XP, _progressionService.GetRequiredClassXpForNextLevel(state, ProgressionClassRole.Human, human.Id))} | {requirement}";
            },
            "!humans");
    }

    public void PrintAbilities(CommandInfo command, PlayerState state, int page)
    {
        var role = _progressionService.GetCurrentRole(state);
        var classId = _progressionService.GetCurrentClassId(state);
        var className = _progressionService.GetClassName(role, classId);
        Header(command, $"{className} Abilities");

        var abilities = GetConfiguredAbilities(role, classId).ToList();
        var classLevel = role == ProgressionClassRole.Zombie
            ? _progressionService.GetZombieProgression(state, classId).Level
            : _progressionService.GetHumanProgression(state, classId).Level;
        var equipped = GetEquippedAbilities(state, role, classId);
        var maxSlots = GetMaxSlots(role);

        Reply(command, $"{Gold("Class")} {RoleColor(role, className)} Level {Green(classLevel)} | {Gold("Slots")} {FormatSlotSummary(equipped, state, maxSlots)}");
        Reply(command, $"{Command("!abilities unlock <id>")} unlocks ready abilities | {Command("!abilities equip <id> <slot>")} equips");
        Reply(command, $"{Command("!bind <slot> <key>")} saves a key label and prints the console bind command.");

        if (abilities.Count == 0)
        {
            Reply(command, "No abilities are configured for this class yet.");
            return;
        }

        var totalPages = Math.Max(1, (int)Math.Ceiling(abilities.Count / (double)AbilityPageSize));
        page = Math.Clamp(Math.Max(1, page), 1, totalPages);

        foreach (var entry in abilities.Skip((page - 1) * AbilityPageSize).Take(AbilityPageSize))
        {
            var ability = AbilityRegistry.Get(entry.Ability);
            var abilityId = GetAbilityId(entry.Ability);
            var cooldown = _config.AbilityConfig.GetSettings(entry.Ability).CooldownSeconds;
            var unlocked = entry.IsDefault || _progressionService.IsAbilityUnlocked(state, role, classId, entry.Ability);
            var ready = _progressionService.IsAbilityUnlockReady(state, role, classId, entry.Ability);
            var equippedSlot = GetEquippedSlot(equipped, entry.Ability);
            var slotText = equippedSlot.HasValue
                ? $" | {Gold($"Slot {equippedSlot.Value}")}{FormatBindLabel(state, equippedSlot.Value)}"
                : unlocked
                    ? $" | {Command($"!abilities equip {abilityId} 1")}"
                    : "";
            var requirement = entry.IsDefault
                ? "included at class level 1"
                : _progressionService.FormatAbilityRequirements(state, role, classId, entry.Ability);
            var requirementSuffix = unlocked && !entry.IsDefault
                ? $" {Green("(owned)")}"
                : "";
            var useText = equippedSlot.HasValue
                ? Command($"css_zability {equippedSlot.Value}")
                : unlocked
                    ? "equip first"
                    : "unlock first";
            var description = ability?.Description ?? "Configured ability.";

            Reply(command, $"{Id(abilityId)} {Green(_progressionService.GetAbilityName(entry.Ability))} {FormatAbilityState(entry.IsDefault, unlocked, ready)}{slotText}");
            Reply(command, $"  {Gold("Cooldown")} {Green($"{cooldown:0.#}s")} | {Gold("Use")} {useText}");
            Reply(command, $"  {Gold("Unlock")} {requirement}{requirementSuffix}");
            Reply(command, $"  {description}");
        }

        Footer(command, $"Page {page}/{totalPages}. Next: !abilities {Math.Min(totalPages, page + 1)}");
    }

    public void PrintEquipMenu(CommandInfo command, PlayerState state)
    {
        var role = _progressionService.GetCurrentRole(state);
        var classId = _progressionService.GetCurrentClassId(state);
        var className = _progressionService.GetClassName(role, classId);
        Header(command, $"{className} Loadout");

        var configured = GetConfiguredAbilities(role, classId)
            .Where(entry => entry.IsDefault || _progressionService.IsAbilityUnlocked(state, role, classId, entry.Ability))
            .ToList();

        var equipped = role == ProgressionClassRole.Zombie
            ? _progressionService.GetZombieProgression(state, classId).ActiveAbilities
            : _progressionService.GetHumanProgression(state, classId).ActiveAbilities;

        var maxSlots = GetMaxSlots(role);

        Reply(command, $"{Command("!abilities equip <id> <slot>")} changes a slot. {Command("!bind <slot> <key>")} stores the key helper.");

        for (var slot = 1; slot <= maxSlots; slot++)
        {
            var label = slot <= equipped.Count
                ? Green(_progressionService.GetAbilityName(equipped[slot - 1]))
                : Red("empty");
            var useText = slot <= equipped.Count
                ? Command($"css_zability {slot}")
                : "empty";
            Reply(command, $"{Gold($"Slot {slot}")}: {label}{FormatBindLabel(state, slot)} | {Gold("Use")} {useText}");
        }

        if (configured.Count == 0)
            Reply(command, "No abilities are available for this class yet.");
        else
            Reply(command, $"Owned: {string.Join(", ", configured.Select(entry => Id(GetAbilityId(entry.Ability))))}");

        Footer(command, "Example: !bind 1 mouse4 prints bind mouse4 \"css_zability 1\".");
    }

    private void PrintPaged<T>(
        CommandInfo command,
        IReadOnlyList<T> items,
        int page,
        Func<T, string> formatter,
        string commandName)
    {
        var totalPages = Math.Max(1, (int)Math.Ceiling(items.Count / (double)PageSize));
        page = Math.Clamp(page, 1, totalPages);
        var start = (page - 1) * PageSize;

        foreach (var item in items.Skip(start).Take(PageSize))
            Reply(command, formatter(item));

        Footer(command, $"Page {page}/{totalPages}. Next: {commandName} {Math.Min(totalPages, page + 1)}");
    }

    private IEnumerable<AbilityMenuEntry> GetConfiguredAbilities(ProgressionClassRole role, string classId)
    {
        if (role == ProgressionClassRole.Zombie)
        {
            var zombie = _progressionService.FindZombie(classId);
            if (zombie == null)
                yield break;

            foreach (var ability in zombie.DefaultAbilities)
                yield return new AbilityMenuEntry(ability, true);

            foreach (var ability in zombie.UnlockableAbilities)
                yield return new AbilityMenuEntry(ability, false);

            yield break;
        }

        var human = _progressionService.FindHuman(classId);
        if (human == null)
            yield break;

        foreach (var ability in human.DefaultAbilities)
            yield return new AbilityMenuEntry(ability, true);

        foreach (var ability in human.UnlockableAbilities)
            yield return new AbilityMenuEntry(ability, false);
    }

    private IReadOnlyList<AbilityType> GetEquippedAbilities(PlayerState state, ProgressionClassRole role, string classId)
    {
        return role == ProgressionClassRole.Zombie
            ? _progressionService.GetZombieProgression(state, classId).ActiveAbilities
            : _progressionService.GetHumanProgression(state, classId).ActiveAbilities;
    }

    private int GetMaxSlots(ProgressionClassRole role)
    {
        return role == ProgressionClassRole.Zombie
            ? Math.Max(1, _config.ProgressionConfig.MaxEquippedZombieAbilities)
            : Math.Max(1, _config.ProgressionConfig.MaxEquippedHumanAbilities);
    }

    private static int? GetEquippedSlot(IReadOnlyList<AbilityType> equipped, AbilityType ability)
    {
        for (var i = 0; i < equipped.Count; i++)
        {
            if (equipped[i] == ability)
                return i + 1;
        }

        return null;
    }

    private string FormatSlotSummary(IReadOnlyList<AbilityType> equipped, PlayerState state, int maxSlots)
    {
        var slots = new List<string>();
        for (var slot = 1; slot <= maxSlots; slot++)
        {
            var label = slot <= equipped.Count
                ? _progressionService.GetAbilityName(equipped[slot - 1])
                : "empty";
            var key = state.AbilitySlotBinds.TryGetValue(slot, out var bind) && !string.IsNullOrWhiteSpace(bind)
                ? $"/{bind}"
                : "";

            slots.Add($"{Gold(slot)}:{label}{key}");
        }

        return string.Join($"{ChatColors.Default} ", slots);
    }

    private static string FormatBindLabel(PlayerState state, int slot)
    {
        return state.AbilitySlotBinds.TryGetValue(slot, out var keyName) && !string.IsNullOrWhiteSpace(keyName)
            ? $" | {ChatColors.LightBlue}{keyName}{ChatColors.Default}"
            : "";
    }

    private static string FormatAbilityState(bool innate, bool unlocked, bool ready)
    {
        if (innate)
            return $"{Gold("OWNED")} {ChatColors.Yellow}INNATE{ChatColors.Default}";

        if (unlocked)
            return Green("OWNED");

        if (ready)
            return $"{ChatColors.Yellow}READY TO UNLOCK{ChatColors.Default}";

        return Red("LOCKED");
    }

    private static string RoleColor(ProgressionClassRole role, string value)
    {
        return role == ProgressionClassRole.Zombie
            ? Zombie(value)
            : Human(value);
    }

    private static string GetAbilityId(AbilityType ability)
    {
        return AbilityRegistry.Get(ability)?.Id ?? ability.ToString().ToLowerInvariant();
    }

    private void Header(CommandInfo command, string title)
    {
        command.ReplyToCommand($"{ChatColors.Gold}======== {ChatColors.LightPurple}{title}{ChatColors.Gold} ========{ChatColors.Default}");
    }

    private void Footer(CommandInfo command, string message)
    {
        command.ReplyToCommand($"{ChatColors.Gold}-- {ChatColors.Default}{message}");
    }

    private static void Reply(CommandInfo command, string message)
    {
        command.ReplyToCommand($"{ChatColors.LightPurple}[ZM]{ChatColors.Default} {message}");
    }

    private static string FormatXp(int currentXp, int requiredXp)
    {
        return requiredXp <= 0
            ? $"{ChatColors.Gold}MAX{ChatColors.Default} ({ChatColors.Lime}{currentXp} saved{ChatColors.Default})"
            : $"{ChatColors.Lime}{currentXp}/{requiredXp} XP{ChatColors.Default}";
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

    private static string Red(object value)
    {
        return $"{ChatColors.Red}{value}{ChatColors.Default}";
    }

    private static string Zombie(string value)
    {
        return $"{ChatColors.Red}{value}{ChatColors.Default}";
    }

    private static string Human(string value)
    {
        return $"{ChatColors.LightBlue}{value}{ChatColors.Default}";
    }

    private readonly record struct AbilityMenuEntry(AbilityType Ability, bool IsDefault);
}
