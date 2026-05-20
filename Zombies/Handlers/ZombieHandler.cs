using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API;
using ZombieModPlugin.Configs;
using ZombieModPlugin.States;
using ZombieModPlugin.Extensions;
using CounterStrikeSharp.API.Modules.Utils;
using System.Drawing;
using ZombieModPlugin.Zombies.Models;

namespace ZombieModPlugin.Zombies.Handlers;

public class ZombieHandler
{
    private readonly BaseConfig _config;

    public ZombieHandler(Dictionary<ulong, PlayerState> playerStates, BaseConfig config)
    {
        _config = config;
    }

    public void OnBecomeZombie(CCSPlayerController player, PlayerState playerState)
    {
        var zombie = playerState.SelectedZombieType ?? GetDefaultZombieClass();
        if (zombie == null)
            return;

        playerState.SelectedZombieType = zombie;
        playerState.SelectedHumanClass = null;
        playerState.ResetRoleRuntimeState();

        player.PrintToChat($"{_config.ChatConfig.ZombiePrefix} You are now a {zombie.Name}!");
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

                ScheduleZombieLoadout(player, zombie);
            });
        });
    }

    private void ScheduleZombieLoadout(CCSPlayerController player, Zombie zombie)
    {
        var model = GetZombieModel(zombie);
        Server.NextFrame(() => ApplyZombieLoadout(player, zombie, model, resetHealth: true, resetWeapons: true));

        _ = Task.Run(async () =>
        {
            await Task.Delay(250);
            Server.NextFrame(() => ApplyZombieLoadout(player, zombie, model, resetHealth: true, resetWeapons: true));

            await Task.Delay(500);
            Server.NextFrame(() => ApplyZombieLoadout(player, zombie, model, resetHealth: true, resetWeapons: false));
        });
    }

    public void EnforceZombieEquipment(CCSPlayerController player, PlayerState playerState)
    {
        var zombie = playerState.SelectedZombieType ?? GetDefaultZombieClass();
        if (zombie == null)
            return;

        ApplyZombieLoadout(player, zombie, GetZombieModel(zombie), resetHealth: false, resetWeapons: true);
    }

    private string GetZombieModel(Zombie zombie)
    {
        return !string.IsNullOrWhiteSpace(zombie.PlayerModel)
            ? zombie.PlayerModel
            : _config.ZombieConfig.PlayerModel;
    }

    private Zombie? GetDefaultZombieClass()
    {
        return _config.ZombieConfig.ZombieTypes.FirstOrDefault(zombie =>
                string.Equals(zombie.Id, _config.ZombieConfig.DefaultZombieClassId, StringComparison.OrdinalIgnoreCase))
            ?? _config.ZombieConfig.ZombieTypes.FirstOrDefault();
    }

    private static void ApplyZombieLoadout(CCSPlayerController player, Zombie zombie, string model, bool resetHealth, bool resetWeapons)
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

        if (resetWeapons)
        {
            player.RemoveWeapons();
            player.GiveNamedItem("weapon_knife");
        }

        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid)
            return;

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
    }
}
