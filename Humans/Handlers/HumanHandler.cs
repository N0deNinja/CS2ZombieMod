using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using ZombieModPlugin.Extensions;
using ZombieModPlugin.States;

namespace ZombieModPlugin.Humans.Handlers;

public class HumanHandler
{
    public void OnBecomeHuman(CCSPlayerController player, PlayerState playerState)
    {
        if (!player.IsValid)
            return;

        playerState.InfectionHitsTaken = 0;

        Server.NextFrame(() =>
        {
            if (!player.IsValid)
                return;

            MoveToHumanTeam(player);

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
                    player.GiveNamedItem("weapon_usp_silencer");

                    var pawn = player.PlayerPawn.Value;
                    if (pawn == null || !pawn.IsValid)
                        return;

                    pawn.Render = Color.FromArgb(255, 255, 255, 255);
                    pawn.MaxHealth = 100;
                    pawn.Health = 100;
                    pawn.VelocityModifier = 1.0f;
                    pawn.GravityScale = 1.0f;
                    pawn.MarkPlayerStatsStateChanged();
                });
            });
        });
    }

    private static void MoveToHumanTeam(CCSPlayerController player)
    {
        if (player.Team == CsTeam.CounterTerrorist)
            return;

        if (player.IsBot)
            player.ChangeTeam(CsTeam.CounterTerrorist);
        else
            player.SwitchTeam(CsTeam.CounterTerrorist);
    }
}
