using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using ZombieModPlugin.Configs;
using ZombieModPlugin.Extensions;
using ZombieModPlugin.Progression.Services;
using ZombieModPlugin.States;

namespace ZombieModPlugin.Blockades;

public sealed class BlockadeService
{
    private const string MainVariantName = "main";
    private const string SmallVariantName = "small";
    private const float MinimumFacingDot = 0.35f;

    private readonly Dictionary<ulong, PlayerState> _playerStates;
    private readonly BaseConfig _config;
    private readonly BasePlugin _plugin;
    private readonly ProgressionService _progressionService;
    private readonly Func<bool> _isPlacementRound;
    private readonly Dictionary<ulong, PreviewBlockade> _previews = [];
    private readonly List<PlacedBlockade> _placedBlockades = [];
    private readonly Dictionary<ulong, DateTime> _nextZombieHitAtUtc = [];

    public BlockadeService(
        Dictionary<ulong, PlayerState> playerStates,
        BaseConfig config,
        BasePlugin plugin,
        ProgressionService progressionService,
        Func<bool> isPlacementRound)
    {
        _playerStates = playerStates;
        _config = config;
        _plugin = plugin;
        _progressionService = progressionService;
        _isPlacementRound = isPlacementRound;
    }

    public void RegisterCommands()
    {
        if (!_config.BlockadeConfig.Enabled)
            return;

        var registered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddCommand(registered, "block");
        AddCommand(registered, _config.BlockadeConfig.Command);
    }

    public void OnTick()
    {
        if (!_config.BlockadeConfig.Enabled)
            return;

        PruneInvalidPlacedBlockades();

        foreach (var ownerKey in _previews.Keys.ToArray())
        {
            if (!_previews.TryGetValue(ownerKey, out var preview))
                continue;

            if (!TryFindPlayer(ownerKey, out var player)
                || !CanUseBlockades(player, notify: false, out _))
            {
                RemovePreview(ownerKey);
                continue;
            }

            UpdatePreviewTransform(player, preview);
        }
    }

    public void OnPlayerButtonsChanged(CCSPlayerController player, PlayerState state, PlayerButtons pressed)
    {
        if (!_config.BlockadeConfig.Enabled || !_isPlacementRound() || !player.PawnIsAlive)
            return;

        if (!state.IsZombie)
        {
            if (pressed.HasFlag(PlayerButtons.Attack2))
                TryConfirmPlacement(player, state);

            return;
        }

        if (pressed.HasFlag(PlayerButtons.Attack) || pressed.HasFlag(PlayerButtons.Attack2))
            TryDamagePlacedBlockade(player, state);
    }

    public void ClearAll()
    {
        foreach (var preview in _previews.Values)
            SafeRemove(preview.Entity);

        foreach (var blockade in _placedBlockades)
            SafeRemove(blockade.Entity);

        _previews.Clear();
        _placedBlockades.Clear();
        _nextZombieHitAtUtc.Clear();
    }

    private void AddCommand(HashSet<string> registered, string? configuredCommand)
    {
        var commandName = NormalizeCommandName(configuredCommand, "block");
        if (!registered.Add(commandName))
            return;

        _plugin.AddCommand($"css_{commandName}", "Buy and place a zombie blockade.", OnBlockCommand);
    }

    private void OnBlockCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
        {
            command.ReplyToCommand("This command can only be used by a connected player.");
            return;
        }

        if (!_config.BlockadeConfig.Enabled)
        {
            command.ReplyToCommand("Blockades are disabled.");
            return;
        }

        var action = command.ArgCount >= 2
            ? NormalizeArgument(command.GetArg(1))
            : string.Empty;

        if (action is "cancel" or "stop")
        {
            CancelPreview(player, notify: true);
            return;
        }

        var smallArgument = NormalizeArgument(_config.BlockadeConfig.SmallArgument);
        var variant = !string.IsNullOrWhiteSpace(action)
            && action == smallArgument
                ? BlockadeVariant.Small
                : BlockadeVariant.Main;

        StartPreview(player, variant);
    }

    private void StartPreview(CCSPlayerController player, BlockadeVariant variant)
    {
        if (!CanUseBlockades(player, notify: true, out var state))
            return;

        PruneInvalidPlacedBlockades();

        var ownerKey = player.GetStateKey();
        var maxPlaced = Math.Max(0, _config.BlockadeConfig.MaxPlacedPerPlayer);
        if (maxPlaced > 0 && CountPlacedByOwner(ownerKey) >= maxPlaced)
        {
            TellHuman(player, $"Maximum blockades reached ({maxPlaced}).");
            return;
        }

        var definition = GetDefinition(variant);
        if (string.IsNullOrWhiteSpace(definition.Model))
        {
            TellHuman(player, "Blockade model is not configured.");
            return;
        }

        RemovePreview(ownerKey, notify: false);

        var entity = SpawnPreviewEntity(definition.Model);
        if (entity == null)
        {
            TellHuman(player, "Could not create blockade preview.");
            return;
        }

        var preview = new PreviewBlockade(entity, definition);
        _previews[ownerKey] = preview;
        UpdatePreviewTransform(player, preview);

        var label = definition.Variant == BlockadeVariant.Small ? "small blockade" : "blockade";
        TellHuman(player, $"Preview started for {label}. Right-click to place. Cost: ${definition.Cost}.");
    }

    private void CancelPreview(CCSPlayerController player, bool notify)
    {
        var ownerKey = player.GetStateKey();
        if (!RemovePreview(ownerKey, notify: false))
        {
            if (notify)
                TellHuman(player, "No blockade preview to cancel.", center: false);

            return;
        }

        if (notify)
            TellHuman(player, "Blockade placement canceled.");
    }

    private void TryConfirmPlacement(CCSPlayerController player, PlayerState state)
    {
        var ownerKey = player.GetStateKey();
        if (!_previews.TryGetValue(ownerKey, out var preview))
            return;

        if (!CanUseBlockades(player, notify: true, out _))
        {
            RemovePreview(ownerKey);
            return;
        }

        PruneInvalidPlacedBlockades();

        var maxPlaced = Math.Max(0, _config.BlockadeConfig.MaxPlacedPerPlayer);
        if (maxPlaced > 0 && CountPlacedByOwner(ownerKey) >= maxPlaced)
        {
            TellHuman(player, $"Maximum blockades reached ({maxPlaced}).");
            return;
        }

        UpdatePreviewTransform(player, preview);

        if (!_progressionService.TrySpendMoney(player, state, preview.Definition.Cost, out var moneyError))
        {
            TellHuman(player, $"{_config.MessagesConfig.NotEnoughMoney} {moneyError}");
            return;
        }

        var entity = SpawnPlacedEntity(preview.Definition.Model, preview.Position, preview.Angles);
        if (entity == null)
        {
            TellHuman(player, "Could not place blockade here.");
            return;
        }

        _placedBlockades.Add(new PlacedBlockade(
            entity,
            ownerKey,
            preview.Definition,
            preview.Position,
            preview.Angles));

        RemovePreview(ownerKey, notify: false);

        var label = preview.Definition.Variant == BlockadeVariant.Small ? "Small blockade" : "Blockade";
        TellHuman(player, $"{label} placed. ${state.Money} remaining.");
        _ = state;
    }

    private bool CanUseBlockades(CCSPlayerController player, bool notify, out PlayerState state)
    {
        state = null!;

        if (!_isPlacementRound())
        {
            if (notify)
                TellHuman(player, "Blockades can only be used during active rounds.");

            return false;
        }

        if (!player.IsValid || !player.PawnIsAlive)
        {
            if (notify)
                TellHuman(player, "You must be alive to place a blockade.");

            return false;
        }

        state = player.GetState(_playerStates);
        if (state.IsZombie)
        {
            if (notify)
                player.PrintToChat($"{_config.ChatConfig.ZombiePrefix} Zombies cannot place blockades.");

            return false;
        }

        return true;
    }

    private CBaseModelEntity? SpawnPreviewEntity(string model)
    {
        var entity = TryCreateModelEntity("prop_dynamic_override", model, solid: false);
        if (entity == null)
            return null;

        ApplyPreviewVisuals(entity);
        ConfigurePreviewCollision(entity);
        return entity;
    }

    private CBaseModelEntity? SpawnPlacedEntity(string model, Vector position, QAngle angles)
    {
        var entity = TryCreateModelEntity("prop_physics_override", model, solid: true, position, angles)
            ?? TryCreateModelEntity("prop_dynamic_override", model, solid: true, position, angles);

        if (entity == null)
            return null;

        ApplyPlacedVisuals(entity);
        ConfigurePlacedCollision(entity);
        return entity;
    }

    private CBaseModelEntity? TryCreateModelEntity(
        string designerName,
        string model,
        bool solid,
        Vector? position = null,
        QAngle? angles = null)
    {
        var entity = Utilities.CreateEntityByName<CBaseModelEntity>(designerName);
        if (entity == null || !entity.IsValid)
            return null;

        try
        {
            entity.SetModel(model);

            using var keyValues = new CEntityKeyValues();
            keyValues.SetString("model", model);
            keyValues.SetInt("solid", solid ? 6 : 0);
            keyValues.SetBool("disableshadows", true);
            if (!solid)
                keyValues.SetBool("CreateNonSolid", true);

            entity.DispatchSpawn(keyValues);

            if (position != null || angles != null)
                entity.Teleport(position, angles, null);

            entity.SetModel(model);
            return entity;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ZombieMod] Failed to spawn blockade entity '{designerName}' with model '{model}': {ex.Message}");
            SafeRemove(entity);
            return null;
        }
    }

    private void UpdatePreviewTransform(CCSPlayerController player, PreviewBlockade preview)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null)
            return;

        var origin = pawn.AbsOrigin;
        var forward = GetHorizontalForward(pawn.EyeAngles.ToForwardVector());
        var distance = Math.Clamp(_config.BlockadeConfig.PlacementDistance, 32.0f, 280.0f);
        var heightOffset = Math.Clamp(_config.BlockadeConfig.PlacementHeightOffset, -96.0f, 128.0f);
        var position = new Vector(
            origin.X + forward.X * distance,
            origin.Y + forward.Y * distance,
            origin.Z + heightOffset);
        var angles = new QAngle(0.0f, pawn.EyeAngles.Y, 0.0f);

        preview.Position = position;
        preview.Angles = angles;

        if (preview.Entity.IsValid)
            preview.Entity.Teleport(position, angles, null);
    }

    private void TryDamagePlacedBlockade(CCSPlayerController zombie, PlayerState state)
    {
        if (!state.IsZombie || !zombie.PawnIsAlive)
            return;

        var zombieKey = zombie.GetStateKey();
        var now = DateTime.UtcNow;
        if (_nextZombieHitAtUtc.TryGetValue(zombieKey, out var nextHitAt) && now < nextHitAt)
            return;

        var target = FindTargetBlockade(zombie);
        if (target == null)
            return;

        _nextZombieHitAtUtc[zombieKey] = now.AddSeconds(Math.Max(0.05f, _config.BlockadeConfig.ZombieHitCooldownSeconds));
        target.Hits++;

        if (target.Hits >= target.Definition.MaxHits)
        {
            DestroyBlockade(target, zombie);
            return;
        }

        var remainingHits = Math.Max(0, target.Definition.MaxHits - target.Hits);
        zombie.PrintToCenter($"Blockade damaged. {remainingHits} hit(s) left.");
        NotifyOwner(target.OwnerKey, $"Your blockade is being damaged ({target.Hits}/{target.Definition.MaxHits}).");
    }

    private PlacedBlockade? FindTargetBlockade(CCSPlayerController zombie)
    {
        PruneInvalidPlacedBlockades();

        var pawn = zombie.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null)
            return null;

        var origin = pawn.AbsOrigin;
        var forward = GetHorizontalForward(pawn.EyeAngles.ToForwardVector());
        var hitRange = Math.Clamp(_config.BlockadeConfig.ZombieHitRange, 32.0f, 260.0f);
        PlacedBlockade? best = null;
        var bestScore = float.NegativeInfinity;

        foreach (var blockade in _placedBlockades)
        {
            var targetPosition = blockade.Entity.AbsOrigin ?? blockade.Position;
            var toTarget = new Vector(
                targetPosition.X - origin.X,
                targetPosition.Y - origin.Y,
                0.0f);
            var distance = toTarget.Length2D();
            if (distance > hitRange)
                continue;

            var direction = distance <= 1.0f
                ? forward
                : new Vector(toTarget.X / distance, toTarget.Y / distance, 0.0f);
            var dot = Dot2D(forward, direction);
            if (dot < MinimumFacingDot && distance > 36.0f)
                continue;

            var score = dot * 2.0f - distance / hitRange;
            if (score <= bestScore)
                continue;

            best = blockade;
            bestScore = score;
        }

        return best;
    }

    private void DestroyBlockade(PlacedBlockade blockade, CCSPlayerController zombie)
    {
        SafeRemove(blockade.Entity);
        _placedBlockades.Remove(blockade);

        zombie.PrintToCenter("Blockade destroyed.");
        zombie.PrintToChat($"{_config.ChatConfig.ZombiePrefix} Blockade destroyed.");
        NotifyOwner(blockade.OwnerKey, "Your blockade was destroyed.");

        foreach (var player in Utilities.GetPlayers().Where(player => player is { IsValid: true }))
        {
            var state = player.GetState(_playerStates);
            if (!state.IsZombie && player.PawnIsAlive)
                player.PrintToChat($"{_config.ChatConfig.HumanPrefix} A blockade was destroyed.");
        }
    }

    private bool RemovePreview(ulong ownerKey, bool notify = false)
    {
        if (!_previews.Remove(ownerKey, out var preview))
            return false;

        SafeRemove(preview.Entity);

        if (notify && TryFindPlayer(ownerKey, out var player))
            TellHuman(player, "Blockade placement canceled.");

        return true;
    }

    private int CountPlacedByOwner(ulong ownerKey)
    {
        return _placedBlockades.Count(blockade => blockade.OwnerKey == ownerKey && blockade.Entity.IsValid);
    }

    private void PruneInvalidPlacedBlockades()
    {
        for (var i = _placedBlockades.Count - 1; i >= 0; i--)
        {
            if (!_placedBlockades[i].Entity.IsValid)
                _placedBlockades.RemoveAt(i);
        }
    }

    private bool TryFindPlayer(ulong ownerKey, out CCSPlayerController player)
    {
        foreach (var candidate in Utilities.GetPlayers().Where(candidate => candidate is { IsValid: true }))
        {
            if (candidate.GetStateKey() == ownerKey)
            {
                player = candidate;
                return true;
            }
        }

        player = null!;
        return false;
    }

    private BlockadeDefinition GetDefinition(BlockadeVariant variant)
    {
        var config = _config.BlockadeConfig;
        return variant == BlockadeVariant.Small
            ? new BlockadeDefinition(
                BlockadeVariant.Small,
                SmallVariantName,
                (config.SmallModel ?? string.Empty).Trim(),
                Math.Max(0, config.SmallCost),
                Math.Max(1, config.SmallHits))
            : new BlockadeDefinition(
                BlockadeVariant.Main,
                MainVariantName,
                (config.MainModel ?? string.Empty).Trim(),
                Math.Max(0, config.MainCost),
                Math.Max(1, config.MainHits));
    }

    private void ApplyPreviewVisuals(CBaseModelEntity entity)
    {
        var alpha = Math.Clamp(_config.BlockadeConfig.PreviewAlpha, 25, 220);
        entity.RenderMode = RenderMode_t.kRenderTransAlpha;
        entity.RenderFX = RenderFx_t.kRenderFxNone;
        entity.Render = Color.FromArgb(alpha, 255, 225, 32);
        entity.MarkRenderStateChanged();

        TryApplyDynamicGlow(entity);
    }

    private static void ApplyPlacedVisuals(CBaseModelEntity entity)
    {
        entity.RenderMode = RenderMode_t.kRenderNormal;
        entity.RenderFX = RenderFx_t.kRenderFxNone;
        entity.Render = Color.FromArgb(255, 255, 255, 255);
        entity.MarkRenderStateChanged();
    }

    private static void TryApplyDynamicGlow(CBaseModelEntity entity)
    {
        try
        {
            var dynamicProp = entity.As<CDynamicProp>();
            dynamicProp.InitialGlowState = 1;
            dynamicProp.GlowRange = 800;
            dynamicProp.GlowRangeMin = 16;
            dynamicProp.GlowColor = Color.FromArgb(255, 255, 225, 32);
            dynamicProp.GlowTeam = -1;
        }
        catch
        {
        }
    }

    private static void ConfigurePreviewCollision(CBaseModelEntity entity)
    {
        try
        {
            entity.Collision.SolidType = SolidType_t.SOLID_NONE;
            entity.Collision.SolidFlags = 0;
            entity.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_NEVER;
            entity.Collision.EnablePhysics = 0;
            entity.MoveType = MoveType_t.MOVETYPE_NONE;
            entity.ActualMoveType = MoveType_t.MOVETYPE_NONE;
            entity.AcceptInput("DisableCollision");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ZombieMod] Failed to configure blockade preview collision: {ex.Message}");
        }
    }

    private static void ConfigurePlacedCollision(CBaseModelEntity entity)
    {
        try
        {
            entity.Collision.SolidType = SolidType_t.SOLID_VPHYSICS;
            entity.Collision.SolidFlags = 0;
            entity.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_PROPS;
            entity.Collision.EnablePhysics = 1;
            entity.MoveType = MoveType_t.MOVETYPE_VPHYSICS;
            entity.ActualMoveType = MoveType_t.MOVETYPE_VPHYSICS;
            entity.AcceptInput("EnableCollision");
            entity.AcceptInput("DisableMotion");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ZombieMod] Failed to configure placed blockade collision: {ex.Message}");
        }
    }

    private static Vector GetHorizontalForward(Vector forward)
    {
        var length = MathF.Sqrt(forward.X * forward.X + forward.Y * forward.Y);
        if (length <= 0.001f)
            return new Vector(1.0f, 0.0f, 0.0f);

        return new Vector(forward.X / length, forward.Y / length, 0.0f);
    }

    private static float Dot2D(Vector left, Vector right)
    {
        return left.X * right.X + left.Y * right.Y;
    }

    private void NotifyOwner(ulong ownerKey, string message)
    {
        if (!TryFindPlayer(ownerKey, out var owner))
            return;

        owner.PrintToCenter(message);
        owner.PrintToChat($"{_config.ChatConfig.HumanPrefix} {message}");
    }

    private void TellHuman(CCSPlayerController player, string message, bool center = true)
    {
        player.PrintToChat($"{_config.ChatConfig.HumanPrefix} {message}");
        if (center)
            player.PrintToCenter(message);
    }

    private static void SafeRemove(CEntityInstance? entity)
    {
        if (entity == null || !entity.IsValid)
            return;

        try
        {
            entity.Remove();
        }
        catch
        {
            try
            {
                entity.AcceptInput("Kill");
            }
            catch
            {
            }
        }
    }

    private static string NormalizeCommandName(string? configuredCommand, string fallback)
    {
        var command = string.IsNullOrWhiteSpace(configuredCommand)
            ? fallback
            : configuredCommand.Trim();

        command = command.TrimStart('!', '/');
        if (command.StartsWith("css_", StringComparison.OrdinalIgnoreCase))
            command = command[4..];

        return command.ToLowerInvariant();
    }

    private static string NormalizeArgument(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().TrimStart('!', '/').ToLowerInvariant();
    }

    private enum BlockadeVariant
    {
        Main,
        Small
    }

    private readonly record struct BlockadeDefinition(
        BlockadeVariant Variant,
        string Name,
        string Model,
        int Cost,
        int MaxHits);

    private sealed class PreviewBlockade
    {
        public PreviewBlockade(CBaseModelEntity entity, BlockadeDefinition definition)
        {
            Entity = entity;
            Definition = definition;
        }

        public CBaseModelEntity Entity { get; }
        public BlockadeDefinition Definition { get; }
        public Vector Position { get; set; } = new();
        public QAngle Angles { get; set; } = new();
    }

    private sealed class PlacedBlockade
    {
        public PlacedBlockade(
            CBaseModelEntity entity,
            ulong ownerKey,
            BlockadeDefinition definition,
            Vector position,
            QAngle angles)
        {
            Entity = entity;
            OwnerKey = ownerKey;
            Definition = definition;
            Position = position;
            Angles = angles;
        }

        public CBaseModelEntity Entity { get; }
        public ulong OwnerKey { get; }
        public BlockadeDefinition Definition { get; }
        public Vector Position { get; set; }
        public QAngle Angles { get; }
        public int Hits { get; set; }
    }
}
