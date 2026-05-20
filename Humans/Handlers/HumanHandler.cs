using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using ZombieModPlugin.Configs;
using ZombieModPlugin.Extensions;
using ZombieModPlugin.Humans.Models;
using ZombieModPlugin.States;

namespace ZombieModPlugin.Humans.Handlers;

public class HumanHandler
{
    private readonly BaseConfig _config;

    public HumanHandler(BaseConfig config)
    {
        _config = config;
    }

    public void OnBecomeHuman(CCSPlayerController player, PlayerState playerState)
    {
        if (!player.IsValid)
            return;

        var humanClass = playerState.SelectedHumanClass ?? GetDefaultHumanClass();
        playerState.SelectedHumanClass = humanClass;
        playerState.ResetRoleRuntimeState();

        player.PrintToChat($"{_config.ChatConfig.HumanPrefix} You are now {humanClass.Name}.");

        Server.NextFrame(() =>
        {
            if (!player.IsValid)
                return;

            player.SwitchTeam(CsTeam.CounterTerrorist);
            player.ForceTeamState(CsTeam.CounterTerrorist);

            Server.NextFrame(() =>
            {
                if (!player.IsValid)
                    return;

                player.ForceTeamState(CsTeam.CounterTerrorist);

                if (!player.PawnIsAlive)
                    player.Respawn();

                ScheduleHumanLoadout(player, humanClass);
            });
        });
    }

    public HumanClass GetDefaultHumanClass()
    {
        var configuredDefault = _config.HumanConfig.HumanClasses.FirstOrDefault(human =>
            string.Equals(human.Id, _config.HumanConfig.DefaultHumanClassId, StringComparison.OrdinalIgnoreCase));

        return configuredDefault
            ?? _config.HumanConfig.HumanClasses.FirstOrDefault()
            ?? new HumanClass
            {
                Id = "default",
                Name = "Human",
                PlayerModel = _config.HumanConfig.PlayerModel,
                Health = 100,
                SpeedModifier = 1.0f,
                Gravity = 1.0f,
                DefaultWeapons = _config.HumanConfig.DefaultWeapons,
                StartingMoney = _config.HumanConfig.StartingMoney
            };
    }

    private void ScheduleHumanLoadout(CCSPlayerController player, HumanClass humanClass)
    {
        Server.NextFrame(() => ApplyHumanLoadout(player, humanClass, _config, resetHealth: true));

        _ = Task.Run(async () =>
        {
            await Task.Delay(150);
            Server.NextFrame(() => ApplyHumanLoadout(player, humanClass, _config, resetHealth: true));

            await Task.Delay(350);
            Server.NextFrame(() => ApplyHumanLoadout(player, humanClass, _config, resetHealth: true));
        });
    }

    public void EnforceHumanAppearance(CCSPlayerController player, PlayerState playerState)
    {
        var humanClass = playerState.SelectedHumanClass ?? GetDefaultHumanClass();
        playerState.SelectedHumanClass = humanClass;
        ApplyHumanLoadout(player, humanClass, _config, resetHealth: false);
    }

    private static void ApplyHumanLoadout(CCSPlayerController player, HumanClass humanClass, BaseConfig config, bool resetHealth)
    {
        if (!player.IsValid)
            return;

        if (player.Team != CsTeam.CounterTerrorist)
            player.SwitchTeam(CsTeam.CounterTerrorist);

        player.ForceTeamState(CsTeam.CounterTerrorist);

        if (!player.PawnIsAlive)
        {
            player.Respawn();
            return;
        }

        player.RemoveWeapons();
        foreach (var weapon in GetWeapons(humanClass, config))
            player.GiveNamedItem(weapon);

        var money = Math.Max(0, humanClass.StartingMoney ?? config.HumanConfig.StartingMoney);
        if (player.InGameMoneyServices != null)
        {
            player.InGameMoneyServices.Account = money;
            player.InGameMoneyServices.StartAccount = money;
        }

        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid)
            return;

        if (pawn.WeaponServices != null)
            pawn.WeaponServices.PreventWeaponPickup = false;

        var model = !string.IsNullOrWhiteSpace(humanClass.PlayerModel)
            ? humanClass.PlayerModel
            : config.HumanConfig.PlayerModel;

        if (!string.IsNullOrWhiteSpace(model))
            pawn.SetModel(model);

        var health = Math.Max(1, humanClass.Health);
        pawn.Render = Color.FromArgb(255, 255, 255, 255);
        pawn.MaxHealth = health;
        pawn.Health = resetHealth
            ? health
            : Math.Clamp(pawn.Health, 1, health);
        pawn.VelocityModifier = Math.Clamp(humanClass.SpeedModifier, 0.1f, 3.0f);
        pawn.GravityScale = Math.Clamp(humanClass.Gravity, 0.1f, 3.0f);
        pawn.MarkPlayerStatsStateChanged();
    }

    private static IEnumerable<string> GetWeapons(HumanClass humanClass, BaseConfig config)
    {
        var configuredWeapons = humanClass.DefaultWeapons.Length > 0
            ? humanClass.DefaultWeapons
            : config.HumanConfig.DefaultWeapons;

        var weapons = configuredWeapons
            .Where(weapon => !string.IsNullOrWhiteSpace(weapon))
            .Select(weapon => weapon.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!weapons.Any(weapon => weapon.Contains("knife", StringComparison.OrdinalIgnoreCase)))
            weapons.Insert(0, "weapon_knife");

        return weapons;
    }
}
