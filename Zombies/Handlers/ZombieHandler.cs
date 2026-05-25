using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Extensions;
using ReclaimCS.Shared.PlayerModels;
using ZombieModPlugin.Configs;
using ZombieModPlugin.States;
using ZombieModPlugin.Extensions;
using ZombieModPlugin.Formatting;
using CounterStrikeSharp.API.Modules.Utils;
using System.Drawing;
using ZombieModPlugin.Progression.Services;
using ZombieModPlugin.Zombies.Models;
using ZombieModPlugin.Zombies.Services;

namespace ZombieModPlugin.Zombies.Handlers;

public class ZombieHandler
{
    private readonly BaseConfig _config;
    private readonly ZombieMeleeVisualService _meleeVisualService;
    private readonly ProgressionService _progressionService;

    public ZombieHandler(
        Dictionary<ulong, PlayerState> playerStates,
        BaseConfig config,
        ZombieMeleeVisualService meleeVisualService,
        ProgressionService progressionService)
    {
        _config = config;
        _meleeVisualService = meleeVisualService;
        _progressionService = progressionService;
    }

    public void OnBecomeZombie(CCSPlayerController player, PlayerState playerState)
    {
        var zombie = playerState.SelectedZombieType
            ?? _progressionService.GetPreferredZombie(playerState);
        if (zombie == null)
            return;

        playerState.SelectedZombieType = zombie;
        playerState.SelectedHumanClass = null;
        playerState.ResetRoleRuntimeState();

        player.PrintToChat(ChatText.Zombie($"You are now a {ChatColors.Gold}{zombie.Name}{ChatColors.Default}!"));
        Console.WriteLine($"[ZombieMod] {player.PlayerName} transformed into a zombie ({zombie.Name}).");

        Server.NextFrame(() =>
        {
            if (!player.IsValid)
                return;

            player.SwitchTeam(CsTeam.Terrorist);
            player.ForceTeamState(CsTeam.Terrorist);

            Server.NextFrame(() =>
            {
                if (!player.IsValid)
                    return;

                player.ForceTeamState(CsTeam.Terrorist);

                if (!player.PawnIsAlive)
                    player.Respawn();

                ScheduleZombieLoadout(player, playerState, zombie);
            });
        });
    }

    private void ScheduleZombieLoadout(CCSPlayerController player, PlayerState playerState, Zombie zombie)
    {
        var model = GetZombieModel(zombie);
        Server.NextFrame(() => ApplyZombieLoadout(player, playerState, zombie, model, resetHealth: true, resetWeapons: true));

        _ = Task.Run(async () =>
        {
            await Task.Delay(250);
            Server.NextFrame(() => ApplyZombieLoadout(player, playerState, zombie, model, resetHealth: true, resetWeapons: false));

            await Task.Delay(500);
            Server.NextFrame(() => ApplyZombieLoadout(player, playerState, zombie, model, resetHealth: true, resetWeapons: false));

            await Task.Delay(750);
            Server.NextFrame(() => ApplyZombieLoadout(player, playerState, zombie, model, resetHealth: false, resetWeapons: false));
        });
    }

    public void EnforceZombieEquipment(CCSPlayerController player, PlayerState playerState)
    {
        var zombie = playerState.SelectedZombieType
            ?? _progressionService.GetPreferredZombie(playerState);
        if (zombie == null)
            return;

        ApplyZombieLoadout(player, playerState, zombie, GetZombieModel(zombie), resetHealth: false, resetWeapons: true);
    }

    private string GetZombieModel(Zombie zombie)
    {
        var configuredModel = !string.IsNullOrWhiteSpace(zombie.PlayerModel)
            ? zombie.PlayerModel
            : _config.ZombieConfig.PlayerModel;

        return ReclaimPlayerModels.ResolveModelPath(configuredModel);
    }

    private Zombie? GetDefaultZombieClass()
    {
        return _config.ZombieConfig.ZombieTypes.FirstOrDefault(zombie =>
                string.Equals(zombie.Id, _config.ZombieConfig.DefaultZombieClassId, StringComparison.OrdinalIgnoreCase))
            ?? _config.ZombieConfig.ZombieTypes.FirstOrDefault();
    }

    private void ApplyZombieLoadout(CCSPlayerController player, PlayerState playerState, Zombie zombie, string model, bool resetHealth, bool resetWeapons)
    {
        if (!player.IsValid)
            return;

        if (player.Team != CsTeam.Terrorist)
            player.SwitchTeam(CsTeam.Terrorist);

        player.ForceTeamState(CsTeam.Terrorist);

        if (!player.PawnIsAlive)
        {
            player.Respawn();
            return;
        }

        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid)
            return;

        if (resetWeapons)
        {
            if (pawn.WeaponServices != null)
                pawn.WeaponServices.PreventWeaponPickup = false;

            player.RemoveWeapons();
            _meleeVisualService.EnsureConfiguredZombieMeleeWeapon(player, pawn);
            StripNonKnifeWeapons(pawn);
            Server.NextFrame(() => StripNonKnifeWeapons(player));
        }

        if (pawn.WeaponServices != null)
            pawn.WeaponServices.PreventWeaponPickup = true;

        if (!string.IsNullOrWhiteSpace(model))
            pawn.SetModel(model);

        pawn.Render = Color.FromArgb(255, 255, 255, 255);
        pawn.MaxHealth = zombie.Health;
        pawn.Health = resetHealth
            ? zombie.Health
            : Math.Clamp(pawn.Health, 1, zombie.Health);
        pawn.VelocityModifier = zombie.SpeedModifier;
        pawn.GravityScale = zombie.Gravity;
        pawn.MarkPlayerStatsStateChanged();
        _meleeVisualService.ScheduleApplyZombieMeleeVisuals(player, playerState);
    }

    private static void StripNonKnifeWeapons(CCSPlayerController player)
    {
        if (!player.IsValid)
            return;

        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid)
            return;

        StripNonKnifeWeapons(pawn);
    }

    private static void StripNonKnifeWeapons(CCSPlayerPawn pawn)
    {
        var weaponServices = pawn.WeaponServices;
        if (weaponServices == null)
            return;

        var weapons = weaponServices.MyWeapons
            .Select(handle => handle.Value)
            .Where(weapon => weapon != null && weapon.IsValid)
            .ToList();

        foreach (var weapon in weapons)
        {
            if (weapon == null || !weapon.IsValid || ZombieMeleeVisualService.IsKnifeWeapon(weapon))
                continue;

            pawn.RemovePlayerItem(weapon);
            weapon.AcceptInput("Kill");
        }
    }
}
