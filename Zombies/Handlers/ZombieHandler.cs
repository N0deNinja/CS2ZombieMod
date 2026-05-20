using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API;
using ZombieModPlugin.Configs;
using ZombieModPlugin.States;
using ZombieModPlugin.Extensions;
using CounterStrikeSharp.API.Modules.Utils;
using System.Drawing;

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
        var zombie = playerState.SelectedZombieType ?? _config.ZombieConfig.ZombieTypes.FirstOrDefault();
        if (zombie == null)
            return;

        playerState.SelectedZombieType = zombie;
        playerState.InfectionHitsTaken = 0;

        player.PrintToChat($"{_config.ChatConfig.ZombiePrefix} You are now a {zombie.Name}!");
        Console.WriteLine($"[ZombieMod] {player.PlayerName} transformed into a zombie ({zombie.Name}).");

        Server.NextFrame(() =>
        {
            if (!player.IsValid)
                return;

            MoveToZombieTeam(player);

            Server.NextFrame(() =>
            {
                if (!player.IsValid)
                    return;

                if (player.IsBot || !player.PawnIsAlive)
                    player.Respawn();

                Server.NextFrame(() =>
                {
                    if (!player.IsValid)
                        return;

                    player.RemoveWeapons();
                    player.GiveNamedItem("weapon_knife");

                    var pawn = player.PlayerPawn.Value;
                    if (pawn == null || !pawn.IsValid)
                        return;

                    pawn.Render = Color.FromArgb(255, 255, 255, 255);
                    pawn.MaxHealth = zombie.Health;
                    pawn.Health = zombie.Health;
                    pawn.VelocityModifier = zombie.SpeedModifier;
                    pawn.GravityScale = zombie.Gravity;
                    pawn.MarkPlayerStatsStateChanged();
                });
            });
        });
    }

    private static void MoveToZombieTeam(CCSPlayerController player)
    {
        if (player.Team == CsTeam.Terrorist)
            return;

        if (player.IsBot)
            player.ChangeTeam(CsTeam.Terrorist);
        else
            player.SwitchTeam(CsTeam.Terrorist);
    }
}
