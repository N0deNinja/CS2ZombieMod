using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Extensions;
using CounterStrikeSharp.API.Modules.Utils;
using ZombieModPlugin.Configs;
using ZombieModPlugin.Extensions;
using ZombieModPlugin.Sounds;
using ZombieModPlugin.States;
using ZombieModPlugin.Zombies.Models;

namespace ZombieModPlugin.Zombies.Services;

public class ZombieMeleeVisualService
{
    private const string FallbackZombieMeleeWeaponName = "weapon_knife";
    private const int DisabledItemDefinitionIndex = 0;
    private const uint NoDrawEffect = (uint)EntityEffects_t.EF_NODRAW;
    private const uint NoDrawButTransmitEffect = (uint)EntityEffects_t.EF_NODRAW_BUT_TRANSMIT;
    private const ushort KnifeGearSlot = (ushort)gear_slot_t.GEAR_SLOT_KNIFE;
    private const double SlashSoundMinIntervalSeconds = 0.25;
    private const double HitSoundMinIntervalSeconds = 0.08;

    private readonly BaseConfig _config;
    private readonly HashSet<string> _modelPathFailureWarnings = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _weaponGiveFailureWarnings = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _itemDefinitionFailureWarnings = [];

    public ZombieMeleeVisualService(BaseConfig config)
    {
        _config = config;
    }

    public void ScheduleApplyZombieMeleeVisuals(CCSPlayerController player, PlayerState state)
    {
        ApplyZombieMeleeVisuals(player, state);

        Server.NextFrame(() => ApplyZombieMeleeVisuals(player, state));

        _ = Task.Run(async () =>
        {
            await Task.Delay(150);
            Server.NextFrame(() => ApplyZombieMeleeVisuals(player, state));

            await Task.Delay(350);
            Server.NextFrame(() => ApplyZombieMeleeVisuals(player, state));
        });
    }

    public void ApplyZombieMeleeVisuals(CCSPlayerController player, PlayerState state)
    {
        if (!IsLiveZombie(player, state))
            return;

        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid)
            return;

        var meleeWeapon = EnsureMeleeEquipped(player, pawn);
        if (meleeWeapon == null || !meleeWeapon.IsValid)
            return;

        ApplyKnifeVisuals(player, meleeWeapon);
    }

    public void OnZombieKnifeSlash(CCSPlayerController player, PlayerState state, string? weaponName)
    {
        if (!IsLiveZombie(player, state) || !IsKnifeWeaponName(weaponName))
            return;

        ApplyZombieMeleeVisuals(player, state);
        TryEmitClawSound(player, _config.ZombieMeleeVisualConfig.ZombieClawSlashSound, isHitSound: false, state);
    }

    public void OnZombieAttackButtonsChanged(CCSPlayerController player, PlayerState state, PlayerButtons pressed)
    {
        if (!pressed.HasFlag(PlayerButtons.Attack) && !pressed.HasFlag(PlayerButtons.Attack2))
            return;

        if (!IsLiveZombie(player, state) || !IsActiveKnife(player))
            return;

        ApplyZombieMeleeVisuals(player, state);
        TryEmitClawSound(player, _config.ZombieMeleeVisualConfig.ZombieClawSlashSound, isHitSound: false, state);
    }

    public void OnZombieKnifeHit(CCSPlayerController attacker, PlayerState attackerState, CCSPlayerController victim)
    {
        if (!IsLiveZombie(attacker, attackerState) || !IsActiveKnife(attacker))
            return;

        TryEmitClawSound(attacker, _config.ZombieMeleeVisualConfig.ZombieClawHitSound, isHitSound: true, attackerState);
        ApplyClassSpecificClawEffects(new ZombieClawAttackContext(attacker, victim, attackerState.SelectedZombieType));
    }

    public string GetZombieMeleeWeaponName()
    {
        return ResolveZombieMeleeWeaponName(_config);
    }

    public CBasePlayerWeapon? EnsureConfiguredZombieMeleeWeapon(CCSPlayerController player, CCSPlayerPawn pawn)
    {
        return EnsureMeleeEquipped(player, pawn);
    }

    public static string ResolveZombieMeleeWeaponName(BaseConfig config)
    {
        var configuredWeapon = config.ZombieMeleeVisualConfig.ZombieMeleeWeaponName?.Trim();
        return string.IsNullOrWhiteSpace(configuredWeapon)
            ? FallbackZombieMeleeWeaponName
            : configuredWeapon;
    }

    private CBasePlayerWeapon? EnsureMeleeEquipped(CCSPlayerController player, CCSPlayerPawn pawn)
    {
        var weaponServices = pawn.WeaponServices;
        if (weaponServices == null)
            return null;

        var configuredWeaponName = GetZombieMeleeWeaponName();
        var requiresExactWeaponName = !string.Equals(configuredWeaponName, FallbackZombieMeleeWeaponName, StringComparison.OrdinalIgnoreCase);
        var meleeWeapon = FindMeleeWeapon(weaponServices, configuredWeaponName, preferredOnly: requiresExactWeaponName);
        if (meleeWeapon == null)
        {
            TryGiveZombieMeleeWeapon(player, configuredWeaponName);
            meleeWeapon = FindMeleeWeapon(weaponServices, configuredWeaponName, preferredOnly: requiresExactWeaponName);
        }

        if (meleeWeapon == null && !string.Equals(configuredWeaponName, FallbackZombieMeleeWeaponName, StringComparison.OrdinalIgnoreCase))
        {
            if (_weaponGiveFailureWarnings.Add(configuredWeaponName))
                Console.WriteLine($"[ZombieMod] Failed to find configured zombie melee weapon '{configuredWeaponName}' after giving it. Falling back to '{FallbackZombieMeleeWeaponName}'.");

            TryGiveZombieMeleeWeapon(player, FallbackZombieMeleeWeaponName);
            meleeWeapon = FindMeleeWeapon(weaponServices, FallbackZombieMeleeWeaponName, preferredOnly: true);
        }

        if (meleeWeapon == null)
            meleeWeapon = FindMeleeWeapon(weaponServices);

        if (meleeWeapon != null && IsKnifeWeapon(meleeWeapon))
        {
            ApplyKnifeVisuals(player, meleeWeapon);
            player.ExecuteClientCommandFromServer("slot3");
        }

        return meleeWeapon;
    }

    private void TryGiveZombieMeleeWeapon(CCSPlayerController player, string weaponName)
    {
        try
        {
            player.GiveNamedItem(weaponName);
        }
        catch (Exception ex)
        {
            if (_weaponGiveFailureWarnings.Add(weaponName))
                Console.WriteLine($"[ZombieMod] Failed to give zombie melee weapon '{weaponName}': {ex.Message}");
        }
    }

    private void ApplyKnifeVisuals(CCSPlayerController player, CBasePlayerWeapon knife)
    {
        TryApplyMeleeLoadoutItemDefinition(player);
        TryApplyMeleeItemDefinition(knife);
        TryApplyReplacementModelPath(knife);

        if (_config.ZombieMeleeVisualConfig.HideZombieKnifeModel)
            HideNetworkedWeaponModel(knife);
    }

    private void TryApplyMeleeLoadoutItemDefinition(CCSPlayerController player)
    {
        var itemDefinitionIndex = _config.ZombieMeleeVisualConfig.ZombieMeleeItemDefinitionIndex;
        if (itemDefinitionIndex <= DisabledItemDefinitionIndex)
            return;

        try
        {
            var inventoryServices = player.InventoryServices;
            if (inventoryServices == null)
                return;

            var clampedItemDefinitionIndex = (ushort)Math.Clamp(itemDefinitionIndex, ushort.MinValue, ushort.MaxValue);
            foreach (var slot in inventoryServices.ServerAuthoritativeWeaponSlots)
            {
                if (slot == null || slot.UnClass != KnifeGearSlot)
                    continue;

                slot.UnItemDefIdx = clampedItemDefinitionIndex;
            }

            player.MarkInventoryStateChanged();
        }
        catch (Exception ex)
        {
            if (_itemDefinitionFailureWarnings.Add(itemDefinitionIndex))
                Console.WriteLine($"[ZombieMod] Failed to apply zombie melee loadout item definition '{itemDefinitionIndex}': {ex.Message}");
        }
    }

    private void TryApplyMeleeItemDefinition(CBasePlayerWeapon knife)
    {
        var itemDefinitionIndex = _config.ZombieMeleeVisualConfig.ZombieMeleeItemDefinitionIndex;
        if (itemDefinitionIndex <= DisabledItemDefinitionIndex)
            return;

        try
        {
            var item = knife.AttributeManager.Item;
            item.ItemDefinitionIndex = (ushort)Math.Clamp(itemDefinitionIndex, ushort.MinValue, ushort.MaxValue);
            item.ItemIDHigh = uint.MaxValue;
            item.ItemIDLow = 0;
            item.Initialized = true;

            knife.MarkEconStateChanged();
        }
        catch (Exception ex)
        {
            if (_itemDefinitionFailureWarnings.Add(itemDefinitionIndex))
                Console.WriteLine($"[ZombieMod] Failed to apply zombie melee item definition '{itemDefinitionIndex}': {ex.Message}");
        }
    }

    private void TryApplyReplacementModelPath(CBasePlayerWeapon knife)
    {
        var visualConfig = _config.ZombieMeleeVisualConfig;
        var modelPath = visualConfig.ZombieKnifeReplacementModelPath?.Trim();
        if (!visualConfig.EnableZombieKnifeReplacementModel || string.IsNullOrWhiteSpace(modelPath))
            return;

        try
        {
            knife.SetModel(modelPath);
        }
        catch (Exception ex)
        {
            if (_modelPathFailureWarnings.Add(modelPath))
                Console.WriteLine($"[ZombieMod] Failed to apply zombie knife replacement model '{modelPath}': {ex.Message}");
        }
    }

    private static void HideNetworkedWeaponModel(CBasePlayerWeapon weapon)
    {
        weapon.RenderMode = RenderMode_t.kRenderTransAlpha;
        weapon.RenderFX = RenderFx_t.kRenderFxNone;
        weapon.Render = Color.FromArgb(0, 255, 255, 255);
        weapon.Effects |= NoDrawEffect;
        weapon.MarkRenderStateChanged();
        weapon.MarkEffectsStateChanged();
    }

    public static void RestoreNetworkedWeaponModel(CBasePlayerWeapon weapon)
    {
        if (!weapon.IsValid)
            return;

        weapon.RenderMode = RenderMode_t.kRenderNormal;
        weapon.RenderFX = RenderFx_t.kRenderFxNone;
        weapon.Render = Color.FromArgb(255, 255, 255, 255);
        weapon.Effects &= ~(NoDrawEffect | NoDrawButTransmitEffect);
        weapon.MarkRenderStateChanged();
        weapon.MarkEffectsStateChanged();
    }

    private void TryEmitClawSound(CCSPlayerController player, string? soundEventName, bool isHitSound, PlayerState state)
    {
        if (string.IsNullOrWhiteSpace(soundEventName))
            return;

        var now = DateTime.UtcNow;
        if (isHitSound)
        {
            if (now < state.NextZombieClawHitSoundAtUtc)
                return;

            state.NextZombieClawHitSoundAtUtc = now.AddSeconds(HitSoundMinIntervalSeconds);
        }
        else
        {
            if (now < state.NextZombieClawSlashSoundAtUtc)
                return;

            state.NextZombieClawSlashSoundAtUtc = now.AddSeconds(SlashSoundMinIntervalSeconds);
        }

        ZombieSounds.Emit(player, _config, soundEventName);
    }

    private static void ApplyClassSpecificClawEffects(ZombieClawAttackContext context)
    {
        _ = context;
        // Future per-class effects belong here:
        // Brute knockback, Toxic poison, Frozen slow, Runner attack-speed tuning.
    }

    private static bool IsLiveZombie(CCSPlayerController player, PlayerState state)
    {
        return player.IsValid && player.PawnIsAlive && state.IsZombie;
    }

    private static bool IsActiveKnife(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid)
            return false;

        var activeWeapon = pawn.WeaponServices?.ActiveWeapon.Value;
        return activeWeapon != null && activeWeapon.IsValid && IsKnifeWeapon(activeWeapon);
    }

    private static CBasePlayerWeapon? FindMeleeWeapon(
        CPlayer_WeaponServices weaponServices,
        string? preferredWeaponName = null,
        bool preferredOnly = false)
    {
        var meleeWeapons = weaponServices.MyWeapons
            .Select(handle => handle.Value)
            .Where(weapon => weapon != null && weapon.IsValid && IsKnifeWeapon(weapon))
            .ToList();

        if (!string.IsNullOrWhiteSpace(preferredWeaponName))
        {
            var preferredWeapon = meleeWeapons.FirstOrDefault(weapon => IsWeaponNamed(weapon!, preferredWeaponName));
            if (preferredWeapon != null)
                return preferredWeapon;

            if (preferredOnly)
                return null;
        }

        return meleeWeapons.FirstOrDefault();
    }

    public static bool IsKnifeWeapon(CBasePlayerWeapon weapon)
    {
        string? weaponName;
        try
        {
            weaponName = weapon.GetWeaponName();
        }
        catch
        {
            weaponName = string.Empty;
        }

        return IsKnifeWeaponName(weaponName) || IsKnifeWeaponName(weapon.DesignerName);
    }

    public static bool IsKnifeWeaponName(string? weaponName)
    {
        if (string.IsNullOrWhiteSpace(weaponName))
            return false;

        return weaponName.Contains("knife", StringComparison.OrdinalIgnoreCase)
            || weaponName.Contains("bayonet", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWeaponNamed(CBasePlayerWeapon weapon, string weaponName)
    {
        var trimmedWeaponName = weaponName.Trim();
        if (string.IsNullOrWhiteSpace(trimmedWeaponName))
            return false;

        try
        {
            if (string.Equals(weapon.GetWeaponName(), trimmedWeaponName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch
        {
        }

        return string.Equals(weapon.DesignerName, trimmedWeaponName, StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct ZombieClawAttackContext(
        CCSPlayerController Attacker,
        CCSPlayerController Victim,
        Zombie? ZombieClass);
}
