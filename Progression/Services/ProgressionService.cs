using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using ZombieModPlugin.Abilities;
using ZombieModPlugin.Configs;
using ZombieModPlugin.Diagnostics;
using ZombieModPlugin.Extensions;
using ZombieModPlugin.Formatting;
using ZombieModPlugin.Humans.Models;
using ZombieModPlugin.Progression.Models;
using ZombieModPlugin.Progression.Persistence;
using ZombieModPlugin.States;
using ZombieModPlugin.Zombies.Models;

namespace ZombieModPlugin.Progression.Services;

public sealed class ProgressionService
{
    private readonly Dictionary<ulong, PlayerState> _playerStates;
    private readonly BaseConfig _config;
    private readonly IPlayerProgressionRepository _repository;
    private readonly ProgressionLevelService _levelService;
    private readonly ProgressionUnlockService _unlockService;

    public ProgressionService(
        Dictionary<ulong, PlayerState> playerStates,
        BaseConfig config,
        IPlayerProgressionRepository repository,
        ProgressionLevelService levelService,
        ProgressionUnlockService unlockService)
    {
        _playerStates = playerStates;
        _config = config;
        _repository = repository;
        _levelService = levelService;
        _unlockService = unlockService;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return _repository.InitializeAsync(cancellationToken);
    }

    public void BeginLoadPlayer(CCSPlayerController player, PlayerState state)
    {
        CrashBreadcrumbs.Log($"progression load start {CrashBreadcrumbs.DescribePlayer(player)} enabled={_config.ProgressionConfig.Enabled}");
        EnsureRuntimeDefaults(state);

        if (player.IsBot || !_config.ProgressionConfig.Enabled)
        {
            state.ProgressionLoaded = true;
            CrashBreadcrumbs.Log($"progression load skipped bot-or-disabled {CrashBreadcrumbs.DescribePlayer(player)}");
            return;
        }

        var steamId = player.SteamID;
        var playerName = player.PlayerName;

        _ = Task.Run(async () =>
        {
            try
            {
                CrashBreadcrumbs.Log($"progression load task start steam={steamId} name=\"{playerName}\"");
                var data = await _repository.LoadAsync(steamId) ?? CreateNewProgressionData(steamId, playerName);
                CrashBreadcrumbs.Log($"progression repository load end steam={steamId} name=\"{playerName}\"");
                data.PlayerName = playerName;

                CrashBreadcrumbs.SafeNextFrame($"progression apply steam={steamId}", () =>
                {
                    if (!player.IsValid)
                    {
                        CrashBreadcrumbs.Log($"progression apply skipped invalid steam={steamId}");
                        return;
                    }

                    CrashBreadcrumbs.Log($"progression ApplyProgressionData start {CrashBreadcrumbs.DescribePlayer(player)}");
                    ApplyProgressionData(state, data);
                    state.ProgressionLoaded = true;
                    CrashBreadcrumbs.Log($"progression ApplyInGameMoney start {CrashBreadcrumbs.DescribePlayer(player)}");
                    ApplyInGameMoney(player, state);
                    CrashBreadcrumbs.Log($"progression SavePlayerFireAndForget start {CrashBreadcrumbs.DescribePlayer(player)}");
                    SavePlayerFireAndForget(player, state);

                    player.PrintToChat($"{ChatColors.LightPurple}[ZM]{ChatColors.Default} Progress loaded: {ChatColors.Gold}Level {state.GlobalLevel}{ChatColors.Default} | Money {ChatColors.Lime}${state.Money}{ChatColors.Default}.");
                    CrashBreadcrumbs.Log($"progression apply end {CrashBreadcrumbs.DescribePlayer(player)} level={state.GlobalLevel} money={state.Money}");
                });
            }
            catch (Exception ex)
            {
                CrashBreadcrumbs.LogException($"progression load task steam={steamId} name=\"{playerName}\"", ex);
                Console.WriteLine($"[ZombieMod] Failed to load progression for {playerName} ({steamId}): {ex}");
                CrashBreadcrumbs.SafeNextFrame($"progression failure notify steam={steamId}", () =>
                {
                    if (player.IsValid)
                        player.PrintToChat($"{ChatColors.Red}[ZM]{ChatColors.Default} Progression failed to load; using temporary defaults this session.");
                });
            }
        });
    }

    public void SaveAllConnectedPlayers()
    {
        var snapshots = new List<PlayerProgressionData>();
        foreach (var player in Utilities.GetPlayers().Where(player => player is { IsValid: true, IsBot: false }))
        {
            if (!_playerStates.TryGetValue(player.GetStateKey(), out var state))
                continue;

            EnsureRuntimeDefaults(state);
            snapshots.Add(CreateSnapshot(player, state));
        }

        if (snapshots.Count == 0)
            return;

        try
        {
            Task.Run(async () =>
            {
                foreach (var snapshot in snapshots)
                    await _repository.SaveAsync(snapshot);
            }).Wait(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ZombieMod] Failed to flush progression on unload: {ex}");
        }
    }

    public void SavePlayerFireAndForget(CCSPlayerController player, PlayerState state)
    {
        if (player.IsBot || !_config.ProgressionConfig.Enabled)
            return;

        PlayerProgressionData data;
        try
        {
            EnsureRuntimeDefaults(state);
            data = CreateSnapshot(player, state);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ZombieMod] Failed to snapshot progression for {player.PlayerName}: {ex}");
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _repository.SaveAsync(data);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ZombieMod] Failed to save progression for {data.PlayerName} ({data.SteamId}): {ex}");
            }
        });
    }

    public int GetStartingMoney()
    {
        return Math.Max(0, _config.HumanConfig.StartingMoney);
    }

    public int GetNativeMoneyDisplayCap()
    {
        return Math.Clamp(_config.HumanConfig.NativeMoneyDisplayCap, 0, 65535);
    }

    public int GetNativeMoneyForBalance(int balance)
    {
        return Math.Clamp(balance, 0, GetNativeMoneyDisplayCap());
    }

    public void ApplyInGameMoney(CCSPlayerController player, PlayerState state, bool save = false)
    {
        EnsureRuntimeDefaults(state);

        var money = GetNativeMoneyForBalance(state.Money);
        TryRequestNativeMoneyHud(player, state);
        WriteNativeMoney(player, state, money);
        QueueNativeMoneyRefresh(player, state, money);

        if (save)
            SavePlayerFireAndForget(player, state);
    }

    public bool SyncNativeMoney(CCSPlayerController player, PlayerState state, bool save = false)
    {
        EnsureRuntimeDefaults(state);
        if (player.InGameMoneyServices == null)
            return false;

        var currentNativeMoney = Math.Clamp(player.InGameMoneyServices.Account, 0, GetNativeMoneyDisplayCap());
        var expectedNativeMoney = Math.Clamp(
            state.LastAppliedNativeMoney > 0
                ? state.LastAppliedNativeMoney
                : GetNativeMoneyForBalance(state.Money),
            0,
            GetNativeMoneyDisplayCap());

        if (!state.NativeMoneySyncReady)
        {
            if (currentNativeMoney == expectedNativeMoney)
                state.NativeMoneySyncReady = true;
            else
                WriteNativeMoney(player, state, expectedNativeMoney);

            return false;
        }

        if (currentNativeMoney == 0 && expectedNativeMoney > 0)
        {
            state.NativeMoneySyncReady = false;
            WriteNativeMoney(player, state, expectedNativeMoney);
            return false;
        }

        if (currentNativeMoney >= expectedNativeMoney)
        {
            state.LastAppliedNativeMoney = currentNativeMoney;
            return false;
        }

        var spent = expectedNativeMoney - currentNativeMoney;
        state.Money = Math.Max(0, state.Money - spent);
        ApplyInGameMoney(player, state, save);
        player.PrintToChat($"{ChatText.MoneyPrefix} Spent {ChatColors.Gold}${spent}{ChatColors.Default}. Balance: {ChatText.Money(state.Money)}.");
        return true;
    }

    public bool TrySpendMoney(CCSPlayerController player, PlayerState state, int amount, out string error)
    {
        EnsureRuntimeDefaults(state);
        amount = Math.Max(0, amount);
        error = string.Empty;

        if (amount == 0)
        {
            ApplyInGameMoney(player, state);
            return true;
        }

        if (state.Money < amount)
        {
            error = $"Need ${amount}. You have ${state.Money}.";
            return false;
        }

        state.Money -= amount;
        ApplyInGameMoney(player, state, save: true);
        return true;
    }

    public bool AwardHumanMoney(CCSPlayerController player, PlayerState state, int amount, string reason)
    {
        if (state.IsZombie)
            return false;

        return AwardMoney(player, state, amount, reason);
    }

    public bool AwardMoney(CCSPlayerController player, PlayerState state, int amount, string reason)
    {
        EnsureRuntimeDefaults(state);
        if (amount <= 0)
            return false;

        state.Money = checked((int)Math.Min(int.MaxValue, (long)state.Money + amount));
        IncrementStatistic(state, "money_earned", amount);
        ApplyInGameMoney(player, state, save: true);
        player.PrintToChat($"{ChatColors.LightPurple}[ZM MONEY]{ChatColors.Default} {ChatColors.Lime}+${amount}{ChatColors.Default} {ChatColors.Yellow}{reason}{ChatColors.Default} | Balance {ChatColors.Lime}${state.Money}{ChatColors.Default}");
        return true;
    }

    public UnlockAttemptResult SaveAbilityBind(CCSPlayerController player, PlayerState state, int slot, string keyName)
    {
        EnsureRuntimeDefaults(state);
        var maxSlots = Math.Max(
            Math.Max(1, _config.ProgressionConfig.MaxEquippedZombieAbilities),
            Math.Max(1, _config.ProgressionConfig.MaxEquippedHumanAbilities));
        slot = Math.Clamp(slot, 1, maxSlots);
        keyName = keyName.Trim();

        if (string.IsNullOrWhiteSpace(keyName))
            return new UnlockAttemptResult { Success = false, Message = "Key name cannot be empty." };

        state.AbilitySlotBinds[slot] = keyName;
        SavePlayerFireAndForget(player, state);

        return new UnlockAttemptResult
        {
            Success = true,
            Message = $"Saved slot {slot} bind label as {keyName}. Console: bind {keyName} \"css_zability {slot}\""
        };
    }

    public Zombie GetPreferredZombie(PlayerState state)
    {
        EnsureRuntimeDefaults(state);

        var preferred = state.PreferredZombieType;
        if (preferred != null && IsClassUnlocked(state, ProgressionClassRole.Zombie, preferred.Id))
            return preferred;

        var starter = GetStarterZombie();
        state.PreferredZombieType = starter;
        return starter;
    }

    public HumanClass GetPreferredHuman(PlayerState state)
    {
        EnsureRuntimeDefaults(state);

        var preferred = state.PreferredHumanClass;
        if (preferred != null && IsClassUnlocked(state, ProgressionClassRole.Human, preferred.Id))
            return preferred;

        var starter = GetStarterHuman();
        state.PreferredHumanClass = starter;
        return starter;
    }

    public bool IsClassUnlocked(PlayerState state, ProgressionClassRole role, string classId)
    {
        EnsureRuntimeDefaults(state);

        return role == ProgressionClassRole.Zombie
            ? state.UnlockedZombieClassIds.Contains(classId)
            : state.UnlockedHumanClassIds.Contains(classId);
    }

    public bool IsClassUnlockReady(PlayerState state, ProgressionClassRole role, string classId)
    {
        if (IsClassUnlocked(state, role, classId))
            return true;

        return _unlockService.RequirementsMet(state, _unlockService.GetClassUnlockDefinition(role, classId));
    }

    public string FormatClassRequirements(PlayerState state, ProgressionClassRole role, string classId)
    {
        return _unlockService.FormatRequirements(state, _unlockService.GetClassUnlockDefinition(role, classId));
    }

    public IReadOnlyList<RequirementCheck> GetClassRequirementChecks(PlayerState state, ProgressionClassRole role, string classId)
    {
        return _unlockService.CheckRequirements(state, _unlockService.GetClassUnlockDefinition(role, classId));
    }

    public string FormatAbilityRequirements(PlayerState state, ProgressionClassRole role, string classId, AbilityType ability)
    {
        return _unlockService.FormatRequirements(state, _unlockService.GetAbilityUnlockDefinition(role, classId, ability));
    }

    public bool IsAbilityUnlocked(PlayerState state, ProgressionClassRole role, string classId, AbilityType ability)
    {
        EnsureRuntimeDefaults(state);
        return role == ProgressionClassRole.Zombie
            ? GetZombieProgression(state, classId).UnlockedAbilities.Contains(ability)
            : GetHumanProgression(state, classId).UnlockedAbilities.Contains(ability);
    }

    public bool IsAbilityUnlockReady(PlayerState state, ProgressionClassRole role, string classId, AbilityType ability)
    {
        if (IsAbilityUnlocked(state, role, classId, ability))
            return true;

        return _unlockService.RequirementsMet(state, _unlockService.GetAbilityUnlockDefinition(role, classId, ability));
    }

    public bool HasAbilityAvailable(PlayerState state, AbilityType ability)
    {
        EnsureRuntimeDefaults(state);

        if (state.IsZombie)
        {
            var zombie = state.SelectedZombieType ?? state.PreferredZombieType;
            if (zombie == null)
                return false;

            if (zombie.DefaultAbilities.Contains(ability))
                return true;

            var progression = GetZombieProgression(state, zombie.Id);
            return progression.UnlockedAbilities.Contains(ability)
                || progression.ActiveAbilities.Contains(ability);
        }

        var humanClass = state.SelectedHumanClass ?? state.PreferredHumanClass;
        if (humanClass == null)
            return false;

        if (humanClass.DefaultAbilities.Contains(ability))
            return true;

        var humanProgression = GetHumanProgression(state, humanClass.Id);
        return humanProgression.UnlockedAbilities.Contains(ability)
            || humanProgression.ActiveAbilities.Contains(ability);
    }

    public IEnumerable<AbilityType> GetUsableAbilities(PlayerState state, Zombie zombie)
    {
        var progression = GetZombieProgression(state, zombie.Id);
        IEnumerable<AbilityType> configuredLoadout = progression.ActiveAbilities.Count > 0
            ? progression.ActiveAbilities
            : zombie.DefaultAbilities.Concat(progression.UnlockedAbilities);

        return configuredLoadout
            .Distinct()
            .Where(type => AbilityRegistry.Get(type) != null);
    }

    public UnlockAttemptResult SetPreferredClass(
        CCSPlayerController player,
        PlayerState state,
        ProgressionClassRole role,
        string classId)
    {
        EnsureRuntimeDefaults(state);

        if (role == ProgressionClassRole.Zombie)
        {
            var zombie = FindZombie(classId);
            if (zombie == null)
                return new UnlockAttemptResult { Success = false, Message = $"Unknown zombie class: {classId}" };

            var autoUnlocked = EnsureClassUnlockedIfReady(state, role, zombie.Id, out var lockMessage);
            if (!autoUnlocked && !IsClassUnlocked(state, role, zombie.Id))
                return new UnlockAttemptResult { Success = false, Message = lockMessage };

            state.PreferredZombieType = zombie;
            GetZombieProgression(state, zombie.Id);
            SavePlayerFireAndForget(player, state);
            return new UnlockAttemptResult
            {
                Success = true,
                Message = autoUnlocked
                    ? $"Unlocked {zombie.Name} and set it as your default zombie."
                    : $"Default zombie set to {zombie.Name}."
            };
        }

        var human = FindHuman(classId);
        if (human == null)
            return new UnlockAttemptResult { Success = false, Message = $"Unknown human class: {classId}" };

        var humanAutoUnlocked = EnsureClassUnlockedIfReady(state, role, human.Id, out var humanLockMessage);
        if (!humanAutoUnlocked && !IsClassUnlocked(state, role, human.Id))
            return new UnlockAttemptResult { Success = false, Message = humanLockMessage };

        state.PreferredHumanClass = human;
        GetHumanProgression(state, human.Id);
        SavePlayerFireAndForget(player, state);
        return new UnlockAttemptResult
        {
            Success = true,
            Message = humanAutoUnlocked
                ? $"Unlocked {human.Name} and set it as your default human."
                : $"Default human set to {human.Name}."
        };
    }

    public UnlockAttemptResult TryUnlockClass(
        CCSPlayerController player,
        PlayerState state,
        ProgressionClassRole role,
        string classId)
    {
        EnsureRuntimeDefaults(state);

        var resolvedClassId = role == ProgressionClassRole.Zombie
            ? FindZombie(classId)?.Id
            : FindHuman(classId)?.Id;

        if (resolvedClassId == null)
            return new UnlockAttemptResult { Success = false, Message = $"Unknown class: {classId}" };

        if (IsClassUnlocked(state, role, resolvedClassId))
            return new UnlockAttemptResult { Success = false, Message = "Class is already unlocked." };

        var definition = _unlockService.GetClassUnlockDefinition(role, resolvedClassId);
        if (!_unlockService.RequirementsMet(state, definition))
        {
            return new UnlockAttemptResult
            {
                Success = false,
                Message = $"Requirements not met: {_unlockService.FormatRequirements(state, definition)}"
            };
        }

        UnlockClassInState(state, role, resolvedClassId);
        SavePlayerFireAndForget(player, state);
        return new UnlockAttemptResult { Success = true, Message = $"Unlocked {GetClassName(role, resolvedClassId)}." };
    }

    public UnlockAttemptResult ForceUnlockClass(
        CCSPlayerController player,
        PlayerState state,
        ProgressionClassRole role,
        string classId)
    {
        EnsureRuntimeDefaults(state);
        var resolvedClassId = role == ProgressionClassRole.Zombie
            ? FindZombie(classId)?.Id
            : FindHuman(classId)?.Id;
        if (resolvedClassId == null)
            return new UnlockAttemptResult { Success = false, Message = $"Unknown class: {classId}" };

        UnlockClassInState(state, role, resolvedClassId);
        SavePlayerFireAndForget(player, state);
        return new UnlockAttemptResult { Success = true, Message = $"Unlocked {GetClassName(role, resolvedClassId)}." };
    }

    public UnlockAttemptResult TryUnlockAbility(
        CCSPlayerController player,
        PlayerState state,
        ProgressionClassRole role,
        string classId,
        AbilityType ability)
    {
        EnsureRuntimeDefaults(state);

        if (!IsClassUnlocked(state, role, classId))
            return new UnlockAttemptResult { Success = false, Message = $"Class is locked: {GetClassName(role, classId)}." };

        if (!IsAbilityConfiguredForClass(role, classId, ability, out var isDefault))
            return new UnlockAttemptResult { Success = false, Message = $"{ability} is not available for {GetClassName(role, classId)}." };

        if (isDefault)
            return new UnlockAttemptResult { Success = false, Message = $"{ability} is innate for {GetClassName(role, classId)}." };

        if (IsAbilityUnlocked(state, role, classId, ability))
            return new UnlockAttemptResult { Success = false, Message = $"{ability} is already unlocked." };

        var definition = _unlockService.GetAbilityUnlockDefinition(role, classId, ability);
        if (!_unlockService.RequirementsMet(state, definition))
        {
            return new UnlockAttemptResult
            {
                Success = false,
                Message = $"Requirements not met: {_unlockService.FormatRequirements(state, definition)}"
            };
        }

        UnlockAbilityInState(state, role, classId, ability);
        SavePlayerFireAndForget(player, state);
        return new UnlockAttemptResult { Success = true, Message = $"Unlocked {GetAbilityName(ability)} for {GetClassName(role, classId)}." };
    }

    public UnlockAttemptResult ForceUnlockAbility(
        CCSPlayerController player,
        PlayerState state,
        ProgressionClassRole role,
        string classId,
        AbilityType ability)
    {
        EnsureRuntimeDefaults(state);
        if (!IsAbilityConfiguredForClass(role, classId, ability, out _))
            return new UnlockAttemptResult { Success = false, Message = $"{ability} is not available for {GetClassName(role, classId)}." };

        UnlockAbilityInState(state, role, classId, ability);
        SavePlayerFireAndForget(player, state);
        return new UnlockAttemptResult { Success = true, Message = $"Unlocked {GetAbilityName(ability)} for {GetClassName(role, classId)}." };
    }

    public UnlockAttemptResult TryEquipAbility(
        CCSPlayerController player,
        PlayerState state,
        ProgressionClassRole role,
        string classId,
        AbilityType ability,
        int slot)
    {
        EnsureRuntimeDefaults(state);
        slot = Math.Max(1, slot);

        if (!IsAbilityConfiguredForClass(role, classId, ability, out var isDefault))
            return new UnlockAttemptResult { Success = false, Message = $"{ability} is not available for {GetClassName(role, classId)}." };

        if (!isDefault && !IsAbilityUnlocked(state, role, classId, ability))
            return new UnlockAttemptResult { Success = false, Message = $"{ability} is locked. Requirements: {FormatAbilityRequirements(state, role, classId, ability)}" };

        var maxSlots = role == ProgressionClassRole.Zombie
            ? Math.Max(1, _config.ProgressionConfig.MaxEquippedZombieAbilities)
            : Math.Max(1, _config.ProgressionConfig.MaxEquippedHumanAbilities);

        if (slot > maxSlots)
            return new UnlockAttemptResult { Success = false, Message = $"Slot must be 1-{maxSlots}." };

        var loadout = GetLoadout(state, role, classId);
        loadout.RemoveAll(existing => existing == ability);

        if (slot > loadout.Count + 1)
            return new UnlockAttemptResult { Success = false, Message = $"Slot {slot} is empty. Equip slots in order or use slot {loadout.Count + 1}." };

        if (slot <= loadout.Count)
            loadout[slot - 1] = ability;
        else
            loadout.Add(ability);

        SavePlayerFireAndForget(player, state);
        return new UnlockAttemptResult { Success = true, Message = $"Equipped {GetAbilityName(ability)} in slot {slot}." };
    }

    public UnlockAttemptResult UnequipAbility(
        CCSPlayerController player,
        PlayerState state,
        ProgressionClassRole role,
        string classId,
        int slot)
    {
        EnsureRuntimeDefaults(state);
        var loadout = GetLoadout(state, role, classId);
        if (slot < 1 || slot > loadout.Count)
            return new UnlockAttemptResult { Success = false, Message = "That slot is already empty." };

        var removed = loadout[slot - 1];
        loadout.RemoveAt(slot - 1);
        SavePlayerFireAndForget(player, state);
        return new UnlockAttemptResult { Success = true, Message = $"Unequipped {GetAbilityName(removed)}." };
    }

    public ProgressionAwardResult AwardReward(
        CCSPlayerController player,
        PlayerState state,
        ProgressionRewardType rewardType,
        string statisticKey)
    {
        var reward = _config.ProgressionConfig.XpRewards.GetReward(rewardType);
        return AwardXp(player, state, reward.GlobalXp, reward.ClassXp, reward.Message, statisticKey);
    }

    public ProgressionAwardResult AwardXp(
        CCSPlayerController player,
        PlayerState state,
        int globalXp,
        int classXp,
        string reason,
        string statisticKey = "")
    {
        EnsureRuntimeDefaults(state);

        if (!string.IsNullOrWhiteSpace(statisticKey))
            IncrementStatistic(state, statisticKey);

        var result = new ProgressionAwardResult();
        if (globalXp > 0)
        {
            var newLevel = _levelService.ApplyXp(
                state.GlobalLevel,
                state.GlobalXP,
                globalXp,
                _config.ProgressionConfig.GlobalLevelCurve,
                out var remainingGlobalXp,
                out var globalLevelsGained);

            state.GlobalLevel = newLevel;
            state.GlobalXP = remainingGlobalXp;
            result.GlobalXpAwarded = globalXp;
            result.GlobalLevelsGained = globalLevelsGained;
            result.NewGlobalLevel = state.GlobalLevel;
        }

        var role = state.IsZombie ? ProgressionClassRole.Zombie : ProgressionClassRole.Human;
        var classId = state.IsZombie ? state.SelectedZombieType?.Id : state.SelectedHumanClass?.Id;
        if (classXp > 0 && !string.IsNullOrWhiteSpace(classId))
        {
            int newClassLevel;
            int classLevelsGained;
            if (role == ProgressionClassRole.Zombie)
            {
                var progression = GetZombieProgression(state, classId);
                newClassLevel = _levelService.ApplyXp(
                    progression.Level,
                    progression.XP,
                    classXp,
                    _config.ProgressionConfig.ClassLevelCurve,
                    out var remainingClassXp,
                    out classLevelsGained);

                progression.Level = newClassLevel;
                progression.XP = remainingClassXp;
            }
            else
            {
                var progression = GetHumanProgression(state, classId);
                newClassLevel = _levelService.ApplyXp(
                    progression.Level,
                    progression.XP,
                    classXp,
                    _config.ProgressionConfig.ClassLevelCurve,
                    out var remainingClassXp,
                    out classLevelsGained);

                progression.Level = newClassLevel;
                progression.XP = remainingClassXp;
            }

            result.ClassXpAwarded = classXp;
            result.ClassLevelsGained = classLevelsGained;
            result.NewClassLevel = newClassLevel;
            result.ClassName = GetClassName(role, classId);
        }

        PrintAwardMessages(player, result, reason);
        SavePlayerFireAndForget(player, state);
        return result;
    }

    public ProgressionAwardResult AwardClassXp(
        CCSPlayerController player,
        PlayerState state,
        ProgressionClassRole role,
        string classId,
        int classXp,
        string reason,
        string statisticKey = "")
    {
        EnsureRuntimeDefaults(state);

        if (!string.IsNullOrWhiteSpace(statisticKey))
            IncrementStatistic(state, statisticKey);

        int newClassLevel;
        int levelsGained;
        if (role == ProgressionClassRole.Zombie)
        {
            var progression = GetZombieProgression(state, classId);
            newClassLevel = _levelService.ApplyXp(
                progression.Level,
                progression.XP,
                classXp,
                _config.ProgressionConfig.ClassLevelCurve,
                out var remainingXp,
                out levelsGained);

            progression.Level = newClassLevel;
            progression.XP = remainingXp;
        }
        else
        {
            var progression = GetHumanProgression(state, classId);
            newClassLevel = _levelService.ApplyXp(
                progression.Level,
                progression.XP,
                classXp,
                _config.ProgressionConfig.ClassLevelCurve,
                out var remainingXp,
                out levelsGained);

            progression.Level = newClassLevel;
            progression.XP = remainingXp;
        }

        var result = new ProgressionAwardResult
        {
            ClassXpAwarded = Math.Max(0, classXp),
            ClassLevelsGained = levelsGained,
            NewClassLevel = newClassLevel,
            ClassName = GetClassName(role, classId)
        };

        PrintAwardMessages(player, result, reason);
        SavePlayerFireAndForget(player, state);
        return result;
    }

    public void SetGlobalLevel(CCSPlayerController player, PlayerState state, int level)
    {
        EnsureRuntimeDefaults(state);
        state.GlobalLevel = Math.Clamp(level, 1, Math.Max(1, _config.ProgressionConfig.GlobalLevelCurve.MaxLevel));
        state.GlobalXP = 0;
        SavePlayerFireAndForget(player, state);
    }

    public void SetClassLevel(
        CCSPlayerController player,
        PlayerState state,
        ProgressionClassRole role,
        string classId,
        int level)
    {
        EnsureRuntimeDefaults(state);
        var maxLevel = Math.Max(1, _config.ProgressionConfig.ClassLevelCurve.MaxLevel);
        if (role == ProgressionClassRole.Zombie)
        {
            var progression = GetZombieProgression(state, classId);
            progression.Level = Math.Clamp(level, 1, maxLevel);
            progression.XP = 0;
        }
        else
        {
            var progression = GetHumanProgression(state, classId);
            progression.Level = Math.Clamp(level, 1, maxLevel);
            progression.XP = 0;
        }

        SavePlayerFireAndForget(player, state);
    }

    public void UnlockAll(CCSPlayerController player, PlayerState state)
    {
        EnsureRuntimeDefaults(state);

        foreach (var zombie in _config.ZombieConfig.ZombieTypes)
        {
            UnlockClassInState(state, ProgressionClassRole.Zombie, zombie.Id);
            foreach (var ability in zombie.UnlockableAbilities)
                UnlockAbilityInState(state, ProgressionClassRole.Zombie, zombie.Id, ability);
        }

        foreach (var human in _config.HumanConfig.HumanClasses)
        {
            UnlockClassInState(state, ProgressionClassRole.Human, human.Id);
            foreach (var ability in human.UnlockableAbilities)
                UnlockAbilityInState(state, ProgressionClassRole.Human, human.Id, ability);
        }

        SavePlayerFireAndForget(player, state);
    }

    public void ResetProgress(CCSPlayerController player, PlayerState state)
    {
        state.ProgressionLoaded = false;
        state.GlobalLevel = 1;
        state.GlobalXP = 0;
        state.Money = GetStartingMoney();
        state.NativeMoneySyncReady = false;
        state.UnlockedZombieClassIds.Clear();
        state.UnlockedHumanClassIds.Clear();
        state.ZombieProgression.Clear();
        state.HumanProgression.Clear();
        state.Statistics.Clear();
        state.AbilitySlotBinds.Clear();
        state.PreferredZombieType = null;
        state.PreferredHumanClass = null;
        state.SelectedZombieType = null;
        state.SelectedHumanClass = null;
        EnsureRuntimeDefaults(state);
        state.ProgressionLoaded = true;

        if (!player.IsBot)
        {
            var snapshot = CreateSnapshot(player, state);
            _ = Task.Run(async () =>
            {
                await _repository.DeleteAsync(snapshot.SteamId);
                await _repository.SaveAsync(snapshot);
            });
        }
    }

    public int GetRequiredGlobalXpForNextLevel(PlayerState state)
    {
        return _levelService.GetRequiredXpForNextLevel(state.GlobalLevel, _config.ProgressionConfig.GlobalLevelCurve);
    }

    public int GetRequiredClassXpForNextLevel(PlayerState state, ProgressionClassRole role, string classId)
    {
        var level = role == ProgressionClassRole.Zombie
            ? GetZombieProgression(state, classId).Level
            : GetHumanProgression(state, classId).Level;

        return _levelService.GetRequiredXpForNextLevel(level, _config.ProgressionConfig.ClassLevelCurve);
    }

    public ZombieProgression GetZombieProgression(PlayerState state, string classId)
    {
        if (!state.ZombieProgression.TryGetValue(classId, out var progression))
        {
            progression = new ZombieProgression();
            state.ZombieProgression[classId] = progression;
        }

        progression.Level = Math.Max(1, progression.Level);
        progression.XP = Math.Max(0, progression.XP);
        return progression;
    }

    public HumanProgression GetHumanProgression(PlayerState state, string classId)
    {
        if (!state.HumanProgression.TryGetValue(classId, out var progression))
        {
            progression = new HumanProgression();
            state.HumanProgression[classId] = progression;
        }

        progression.Level = Math.Max(1, progression.Level);
        progression.XP = Math.Max(0, progression.XP);
        return progression;
    }

    public IEnumerable<Zombie> GetZombieClasses()
    {
        return _config.ZombieConfig.ZombieTypes;
    }

    public IEnumerable<HumanClass> GetHumanClasses()
    {
        return _config.HumanConfig.HumanClasses;
    }

    public Zombie? FindZombie(string value)
    {
        if (int.TryParse(value, out var index) && index >= 1 && index <= _config.ZombieConfig.ZombieTypes.Length)
            return _config.ZombieConfig.ZombieTypes[index - 1];

        return FindByIdOrName(_config.ZombieConfig.ZombieTypes, value, zombie => zombie.Id, zombie => zombie.Name);
    }

    public HumanClass? FindHuman(string value)
    {
        if (int.TryParse(value, out var index) && index >= 1 && index <= _config.HumanConfig.HumanClasses.Length)
            return _config.HumanConfig.HumanClasses[index - 1];

        return FindByIdOrName(_config.HumanConfig.HumanClasses, value, human => human.Id, human => human.Name);
    }

    public string GetClassName(ProgressionClassRole role, string classId)
    {
        return role == ProgressionClassRole.Zombie
            ? FindZombie(classId)?.Name ?? classId
            : FindHuman(classId)?.Name ?? classId;
    }

    public string GetAbilityName(AbilityType ability)
    {
        return AbilityRegistry.Get(ability)?.Name ?? ability.ToString();
    }

    public ProgressionClassRole GetCurrentRole(PlayerState state)
    {
        return state.IsZombie ? ProgressionClassRole.Zombie : ProgressionClassRole.Human;
    }

    public string GetCurrentClassId(PlayerState state)
    {
        if (state.IsZombie)
            return (state.SelectedZombieType ?? state.PreferredZombieType ?? GetStarterZombie()).Id;

        return (state.SelectedHumanClass ?? state.PreferredHumanClass ?? GetStarterHuman()).Id;
    }

    private void EnsureRuntimeDefaults(PlayerState state)
    {
        state.GlobalLevel = Math.Max(1, state.GlobalLevel);
        state.GlobalXP = Math.Max(0, state.GlobalXP);
        state.Money = Math.Max(0, state.Money);

        var starterZombie = GetStarterZombie();
        var starterHuman = GetStarterHuman();

        state.UnlockedZombieClassIds.Add(starterZombie.Id);
        state.UnlockedHumanClassIds.Add(starterHuman.Id);

        foreach (var zombie in _config.ZombieConfig.ZombieTypes)
            GetZombieProgression(state, zombie.Id);

        foreach (var human in _config.HumanConfig.HumanClasses)
            GetHumanProgression(state, human.Id);

        if (state.PreferredZombieType == null || !IsClassUnlockedInternal(state, ProgressionClassRole.Zombie, state.PreferredZombieType.Id))
            state.PreferredZombieType = starterZombie;

        if (state.PreferredHumanClass == null || !IsClassUnlockedInternal(state, ProgressionClassRole.Human, state.PreferredHumanClass.Id))
            state.PreferredHumanClass = starterHuman;
    }

    private void WriteNativeMoney(CCSPlayerController player, PlayerState state, int money)
    {
        if (player.InGameMoneyServices == null)
            return;

        CrashBreadcrumbs.Log($"progression native money write start money={money} {CrashBreadcrumbs.DescribePlayer(player)}");
        player.InGameMoneyServices.Account = money;
        player.InGameMoneyServices.StartAccount = money;
        player.MarkMoneyStateChanged();
        state.LastAppliedNativeMoney = money;
        state.NativeMoneySyncReady = player.InGameMoneyServices.Account == money;
        CrashBreadcrumbs.Log($"progression native money write end money={money} {CrashBreadcrumbs.DescribePlayer(player)}");
    }

    private static void TryRequestNativeMoneyHud(CCSPlayerController player, PlayerState state)
    {
        var now = DateTime.UtcNow;
        if (now < state.NextNativeMoneyHudRefreshAtUtc)
            return;

        state.NextNativeMoneyHudRefreshAtUtc = now.AddSeconds(10);

        try
        {
            player.ExecuteClientCommand("cl_showloadout 1");
            player.ReplicateConVar("cl_showloadout", "1");
        }
        catch
        {
        }
    }

    private void QueueNativeMoneyRefresh(CCSPlayerController player, PlayerState state, int money)
    {
        Server.NextFrame(() => WriteNativeMoneyIfValid(player, state, money));

        _ = Task.Run(async () =>
        {
            await Task.Delay(150);
            Server.NextFrame(() => WriteNativeMoneyIfValid(player, state, money));

            await Task.Delay(350);
            Server.NextFrame(() => WriteNativeMoneyIfValid(player, state, money));
        });
    }

    private void WriteNativeMoneyIfValid(CCSPlayerController player, PlayerState state, int money)
    {
        if (!player.IsValid)
            return;

        WriteNativeMoney(player, state, money);
    }

    private PlayerProgressionData CreateNewProgressionData(ulong steamId, string playerName)
    {
        var data = new PlayerProgressionData
        {
            SteamId = steamId,
            PlayerName = playerName,
            GlobalLevel = 1,
            GlobalXp = 0,
            Money = GetStartingMoney(),
            SelectedZombieClassId = GetStarterZombie().Id,
            SelectedHumanClassId = GetStarterHuman().Id
        };

        data.UnlockedZombieClassIds.Add(GetStarterZombie().Id);
        data.UnlockedHumanClassIds.Add(GetStarterHuman().Id);

        foreach (var zombie in _config.ZombieConfig.ZombieTypes)
            data.ClassProgression[new ProgressionClassKey(ProgressionClassRole.Zombie, zombie.Id)] = new ClassProgressionData();

        foreach (var human in _config.HumanConfig.HumanClasses)
            data.ClassProgression[new ProgressionClassKey(ProgressionClassRole.Human, human.Id)] = new ClassProgressionData();

        return data;
    }

    private void ApplyProgressionData(PlayerState state, PlayerProgressionData data)
    {
        state.GlobalLevel = Math.Max(1, data.GlobalLevel);
        state.GlobalXP = Math.Max(0, data.GlobalXp);
        state.Money = Math.Max(0, data.Money);
        state.UnlockedZombieClassIds = new HashSet<string>(data.UnlockedZombieClassIds, StringComparer.OrdinalIgnoreCase);
        state.UnlockedHumanClassIds = new HashSet<string>(data.UnlockedHumanClassIds, StringComparer.OrdinalIgnoreCase);
        state.Statistics = new Dictionary<string, long>(data.Statistics, StringComparer.OrdinalIgnoreCase);
        state.AbilitySlotBinds = data.AbilitySlotBinds
            .Where(bind => bind.Key > 0 && !string.IsNullOrWhiteSpace(bind.Value))
            .ToDictionary(bind => bind.Key, bind => bind.Value, EqualityComparer<int>.Default);
        state.ZombieProgression.Clear();
        state.HumanProgression.Clear();

        foreach (var (key, classData) in data.ClassProgression)
        {
            if (key.Role == ProgressionClassRole.Zombie)
            {
                state.ZombieProgression[key.ClassId] = new ZombieProgression
                {
                    Level = Math.Max(1, classData.Level),
                    XP = Math.Max(0, classData.Xp),
                    UnlockedAbilities = classData.UnlockedAbilities.ToList()
                };
            }
            else
            {
                state.HumanProgression[key.ClassId] = new HumanProgression
                {
                    Level = Math.Max(1, classData.Level),
                    XP = Math.Max(0, classData.Xp),
                    UnlockedAbilities = classData.UnlockedAbilities.ToList()
                };
            }
        }

        foreach (var equipped in data.EquippedAbilities.OrderBy(record => record.Slot))
        {
            var loadout = GetLoadout(state, equipped.Role, equipped.ClassId);
            if (equipped.Slot <= loadout.Count)
                loadout[equipped.Slot - 1] = equipped.Ability;
            else
                loadout.Add(equipped.Ability);
        }

        var selectedZombie = FindZombie(data.SelectedZombieClassId);
        if (selectedZombie != null)
            state.PreferredZombieType = selectedZombie;

        var selectedHuman = FindHuman(data.SelectedHumanClassId);
        if (selectedHuman != null)
            state.PreferredHumanClass = selectedHuman;

        EnsureRuntimeDefaults(state);
    }

    private PlayerProgressionData CreateSnapshot(CCSPlayerController player, PlayerState state)
    {
        var data = new PlayerProgressionData
        {
            SteamId = player.SteamID,
            PlayerName = player.PlayerName,
            GlobalLevel = Math.Max(1, state.GlobalLevel),
            GlobalXp = Math.Max(0, state.GlobalXP),
            Money = Math.Max(0, state.Money),
            SelectedZombieClassId = (state.PreferredZombieType ?? GetStarterZombie()).Id,
            SelectedHumanClassId = (state.PreferredHumanClass ?? GetStarterHuman()).Id
        };

        foreach (var classId in state.UnlockedZombieClassIds)
            data.UnlockedZombieClassIds.Add(classId);

        foreach (var classId in state.UnlockedHumanClassIds)
            data.UnlockedHumanClassIds.Add(classId);

        foreach (var (classId, progression) in state.ZombieProgression)
        {
            var classData = new ClassProgressionData
            {
                Level = Math.Max(1, progression.Level),
                Xp = Math.Max(0, progression.XP)
            };

            foreach (var ability in progression.UnlockedAbilities.Distinct())
                classData.UnlockedAbilities.Add(ability);

            data.ClassProgression[new ProgressionClassKey(ProgressionClassRole.Zombie, classId)] = classData;

            for (var i = 0; i < progression.ActiveAbilities.Count; i++)
                data.EquippedAbilities.Add(new EquippedAbilityRecord(ProgressionClassRole.Zombie, classId, progression.ActiveAbilities[i], i + 1));
        }

        foreach (var (classId, progression) in state.HumanProgression)
        {
            var classData = new ClassProgressionData
            {
                Level = Math.Max(1, progression.Level),
                Xp = Math.Max(0, progression.XP)
            };

            foreach (var ability in progression.UnlockedAbilities.Distinct())
                classData.UnlockedAbilities.Add(ability);

            data.ClassProgression[new ProgressionClassKey(ProgressionClassRole.Human, classId)] = classData;

            for (var i = 0; i < progression.ActiveAbilities.Count; i++)
                data.EquippedAbilities.Add(new EquippedAbilityRecord(ProgressionClassRole.Human, classId, progression.ActiveAbilities[i], i + 1));
        }

        foreach (var (statKey, value) in state.Statistics)
            data.Statistics[statKey] = value;

        foreach (var (slot, keyName) in state.AbilitySlotBinds)
        {
            if (slot > 0 && !string.IsNullOrWhiteSpace(keyName))
                data.AbilitySlotBinds[slot] = keyName.Trim();
        }

        return data;
    }

    private void PrintAwardMessages(CCSPlayerController player, ProgressionAwardResult result, string reason)
    {
        var parts = new List<string>();
        if (result.GlobalXpAwarded > 0)
            parts.Add($"{ChatColors.Lime}+{result.GlobalXpAwarded} global XP{ChatColors.Default}");

        if (result.ClassXpAwarded > 0)
            parts.Add($"{ChatColors.Lime}+{result.ClassXpAwarded} {result.ClassName} XP{ChatColors.Default}");

        if (parts.Count > 0)
            player.PrintToChat($"{ChatColors.LightPurple}[ZM XP]{ChatColors.Default} {string.Join(" | ", parts)} {ChatColors.Yellow}{reason}{ChatColors.Default}");

        if (result.GlobalLevelsGained > 0)
        {
            player.PrintToChat($"{ChatColors.Gold}[LEVEL UP]{ChatColors.Default} Global level {ChatColors.Lime}{result.NewGlobalLevel}{ChatColors.Default}.");
            player.PrintToCenterHtml($"<font color='#ffd45a'>GLOBAL LEVEL {result.NewGlobalLevel}</font><br><font color='#ffffff'>New progression rewards available</font>", 4);
        }

        if (result.ClassLevelsGained > 0)
        {
            player.PrintToChat($"{ChatColors.Gold}[CLASS UP]{ChatColors.Default} {result.ClassName} level {ChatColors.Lime}{result.NewClassLevel}{ChatColors.Default}.");
            player.PrintToCenterHtml($"<font color='#9cff7a'>{result.ClassName} LEVEL {result.NewClassLevel}</font><br><font color='#ffffff'>Check !abilities and !shop</font>", 4);
        }
    }

    private void UnlockClassInState(PlayerState state, ProgressionClassRole role, string classId)
    {
        if (role == ProgressionClassRole.Zombie)
        {
            state.UnlockedZombieClassIds.Add(classId);
            GetZombieProgression(state, classId);
        }
        else
        {
            state.UnlockedHumanClassIds.Add(classId);
            GetHumanProgression(state, classId);
        }
    }

    private bool EnsureClassUnlockedIfReady(
        PlayerState state,
        ProgressionClassRole role,
        string classId,
        out string lockMessage)
    {
        lockMessage = string.Empty;
        if (IsClassUnlocked(state, role, classId))
            return false;

        var definition = _unlockService.GetClassUnlockDefinition(role, classId);
        if (!_unlockService.RequirementsMet(state, definition))
        {
            lockMessage = $"Locked. Requirements: {_unlockService.FormatRequirements(state, definition)}";
            return false;
        }

        UnlockClassInState(state, role, classId);
        return true;
    }

    private void UnlockAbilityInState(PlayerState state, ProgressionClassRole role, string classId, AbilityType ability)
    {
        if (role == ProgressionClassRole.Zombie)
        {
            var progression = GetZombieProgression(state, classId);
            if (!progression.UnlockedAbilities.Contains(ability))
                progression.UnlockedAbilities.Add(ability);
        }
        else
        {
            var progression = GetHumanProgression(state, classId);
            if (!progression.UnlockedAbilities.Contains(ability))
                progression.UnlockedAbilities.Add(ability);
        }
    }

    private List<AbilityType> GetLoadout(PlayerState state, ProgressionClassRole role, string classId)
    {
        return role == ProgressionClassRole.Zombie
            ? GetZombieProgression(state, classId).ActiveAbilities
            : GetHumanProgression(state, classId).ActiveAbilities;
    }

    private bool IsAbilityConfiguredForClass(
        ProgressionClassRole role,
        string classId,
        AbilityType ability,
        out bool isDefault)
    {
        isDefault = false;
        if (role == ProgressionClassRole.Zombie)
        {
            var zombie = FindZombie(classId);
            if (zombie == null)
                return false;

            isDefault = zombie.DefaultAbilities.Contains(ability);
            return isDefault || zombie.UnlockableAbilities.Contains(ability);
        }

        var human = FindHuman(classId);
        if (human == null)
            return false;

        isDefault = human.DefaultAbilities.Contains(ability);
        return isDefault || human.UnlockableAbilities.Contains(ability);
    }

    private bool IsClassUnlockedInternal(PlayerState state, ProgressionClassRole role, string classId)
    {
        return role == ProgressionClassRole.Zombie
            ? state.UnlockedZombieClassIds.Contains(classId)
            : state.UnlockedHumanClassIds.Contains(classId);
    }

    private void IncrementStatistic(PlayerState state, string key, long amount = 1)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        state.Statistics.TryGetValue(key, out var value);
        state.Statistics[key] = value + amount;
    }

    private Zombie GetStarterZombie()
    {
        return _config.ZombieConfig.ZombieTypes.FirstOrDefault(zombie =>
                string.Equals(zombie.Id, _config.ZombieConfig.DefaultZombieClassId, StringComparison.OrdinalIgnoreCase))
            ?? _config.ZombieConfig.ZombieTypes.First();
    }

    private HumanClass GetStarterHuman()
    {
        return _config.HumanConfig.HumanClasses.FirstOrDefault(human =>
                string.Equals(human.Id, _config.HumanConfig.DefaultHumanClassId, StringComparison.OrdinalIgnoreCase))
            ?? _config.HumanConfig.HumanClasses.First();
    }

    private static T? FindByIdOrName<T>(IEnumerable<T> values, string value, Func<T, string> idSelector, Func<T, string> nameSelector)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = Normalize(value);
        return values.FirstOrDefault(candidate =>
            Normalize(idSelector(candidate)) == normalized
            || Normalize(nameSelector(candidate)) == normalized);
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
