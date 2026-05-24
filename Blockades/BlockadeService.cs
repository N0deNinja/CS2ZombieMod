using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using ZombieModPlugin.Configs;
using ZombieModPlugin.Extensions;
using ZombieModPlugin.Formatting;
using ZombieModPlugin.Progression.Services;
using ZombieModPlugin.States;

namespace ZombieModPlugin.Blockades;

public sealed class BlockadeService
{
    private const string LogPrefix = "[ZombieMod][Blockade]";
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

            if (!TryFindPlayer(ownerKey, out var player))
            {
                LogDebug($"OnTick removing preview ownerKey={ownerKey} reason=\"player not found\" {DescribeEntity(preview.Entity)}");
                RemovePreview(ownerKey, reason: "tick player not found");
                continue;
            }

            if (!CanUseBlockades(player, notify: false, out _, out var failureReason))
            {
                LogDebug($"OnTick removing preview ownerKey={ownerKey} reason=\"CanUseBlockades failed: {failureReason}\" {DescribePlayer(player)} {DescribeEntity(preview.Entity)}");
                RemovePreview(ownerKey, reason: $"tick CanUseBlockades failed: {failureReason}");
                continue;
            }

            UpdatePreviewTransform(player, preview, context: "tick", logDetails: false);
        }

        DamageBlockadesFromHeldHeavyAttacks();
    }

    public void OnPlayerButtonsChanged(CCSPlayerController player, PlayerState state, PlayerButtons pressed)
    {
        if (!_config.BlockadeConfig.Enabled || !_isPlacementRound() || !player.PawnIsAlive)
            return;

        if (!state.IsZombie)
        {
            if (pressed.HasFlag(PlayerButtons.Cancel) && _previews.ContainsKey(player.GetStateKey()))
            {
                CancelPreview(player, notify: true);
                return;
            }

            if (pressed.HasFlag(PlayerButtons.Attack2))
                TryConfirmPlacement(player, state);

            return;
        }

        if (pressed.HasFlag(PlayerButtons.Attack2))
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
            command.ReplyToCommand(ChatText.Error("This command can only be used by a connected player."));
            return;
        }

        var rawAction = command.ArgCount >= 2
            ? command.GetArg(1)
            : string.Empty;
        LogDebug($"Command start argCount={command.ArgCount} rawAction={Quote(rawAction)} enabled={_config.BlockadeConfig.Enabled} placementRound={_isPlacementRound()} {DescribePlayer(player)}");

        if (!_config.BlockadeConfig.Enabled)
        {
            LogDebug($"Command denied reason=\"blockades disabled\" {DescribePlayer(player)}");
            command.ReplyToCommand(ChatText.Error("Blockades are disabled."));
            return;
        }

        var action = NormalizeArgument(rawAction);

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
        var ownerKey = player.GetStateKey();
        var definition = GetDefinition(variant);
        _playerStates.TryGetValue(ownerKey, out var existingState);
        LogDebug($"StartPreview requested ownerKey={ownerKey} variant={definition.Name} cost={definition.Cost} model={Quote(definition.Model)} {DescribePlayer(player, existingState)}");

        if (!CanUseBlockades(player, notify: true, out var state, out var failureReason))
        {
            LogDebug($"StartPreview denied ownerKey={ownerKey} reason={Quote(failureReason)} variant={definition.Name} cost={definition.Cost} model={Quote(definition.Model)} {DescribePlayer(player, state)}");
            return;
        }

        LogDebug($"StartPreview CanUseBlockades allowed ownerKey={ownerKey} variant={definition.Name} cost={definition.Cost} model={Quote(definition.Model)} {DescribePlayer(player, state)}");

        PruneInvalidPlacedBlockades();

        var maxPlaced = Math.Max(0, _config.BlockadeConfig.MaxPlacedPerPlayer);
        var placedCount = CountPlacedByOwner(ownerKey);
        if (maxPlaced > 0 && placedCount >= maxPlaced)
        {
            LogDebug($"StartPreview denied ownerKey={ownerKey} reason=\"max placed reached\" placed={placedCount} maxPlaced={maxPlaced}");
            TellHuman(player, $"Maximum blockades reached ({maxPlaced}).");
            return;
        }

        if (string.IsNullOrWhiteSpace(definition.Model))
        {
            LogDebug($"StartPreview denied ownerKey={ownerKey} reason=\"model not configured\" variant={definition.Name}");
            TellHuman(player, "Blockade model is not configured.");
            return;
        }

        if (state.Money < definition.Cost)
        {
            LogDebug($"StartPreview denied ownerKey={ownerKey} reason=\"not enough money\" money={state.Money} cost={definition.Cost} variant={definition.Name} model={Quote(definition.Model)}");
            _progressionService.ApplyInGameMoney(player, state);
            TellHuman(player, $"{_config.MessagesConfig.NotEnoughMoney} Need {ChatText.Money(definition.Cost)}. You have {ChatText.Money(state.Money)}.", center: false);
            return;
        }

        if (!TryGetPreviewTransform(player, out var position, out var angles, context: "start", logDetails: true))
        {
            LogDebug($"StartPreview denied ownerKey={ownerKey} reason=\"preview transform unavailable\" variant={definition.Name} model={Quote(definition.Model)}");
            TellHuman(player, "Could not find a valid preview position.");
            return;
        }

        RemovePreview(ownerKey, notify: false, reason: "start replacing existing preview");

        var entity = SpawnPreviewEntity(definition.Model, position, angles);
        if (entity == null)
        {
            LogDebug($"StartPreview failed ownerKey={ownerKey} reason=\"preview entity creation failed\" variant={definition.Name} model={Quote(definition.Model)} position={FormatVectorForLog(position)} angles={FormatAnglesForLog(angles)}");
            TellHuman(player, "Could not create blockade preview.");
            return;
        }

        var preview = new PreviewBlockade(entity, definition)
        {
            Position = position,
            Angles = angles
        };
        _previews[ownerKey] = preview;
        LogDebug($"StartPreview stored ownerKey={ownerKey} variant={definition.Name} position={FormatVectorForLog(position)} angles={FormatAnglesForLog(angles)} {DescribeEntity(entity)}");
        RefreshPreview(ownerKey);
        Server.NextFrame(() => RefreshPreview(ownerKey));

        var label = definition.Variant == BlockadeVariant.Small ? "small blockade" : "blockade";
        LogDebug($"StartPreview chat notification ownerKey={ownerKey} label={Quote(label)} cost={definition.Cost}");
        TellHuman(player, $"Preview started for {ChatColors.Gold}{label}{ChatColors.Default}. Right-click to place, Escape to cancel. Cost: {ChatText.Money(definition.Cost)}.");
    }

    private void CancelPreview(CCSPlayerController player, bool notify)
    {
        var ownerKey = player.GetStateKey();
        LogDebug($"CancelPreview requested ownerKey={ownerKey} notify={notify} {DescribePlayer(player)}");
        if (!RemovePreview(ownerKey, notify: false, reason: "player cancel requested"))
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
        {
            LogDebug($"Placement attempt ownerKey={ownerKey} preview=missing {DescribePlayer(player, state)}");
            return;
        }

        LogDebug($"Placement attempt ownerKey={ownerKey} preview=exists variant={preview.Definition.Name} cost={preview.Definition.Cost} model={Quote(preview.Definition.Model)} currentPosition={FormatVectorForLog(preview.Position)} currentAngles={FormatAnglesForLog(preview.Angles)} {DescribePlayer(player, state)} {DescribeEntity(preview.Entity)}");

        if (!CanUseBlockades(player, notify: true, out _, out var failureReason))
        {
            LogDebug($"Placement denied ownerKey={ownerKey} reason=\"CanUseBlockades failed: {failureReason}\" {DescribePlayer(player, state)}");
            RemovePreview(ownerKey, reason: $"placement CanUseBlockades failed: {failureReason}");
            return;
        }

        PruneInvalidPlacedBlockades();

        var maxPlaced = Math.Max(0, _config.BlockadeConfig.MaxPlacedPerPlayer);
        var placedCount = CountPlacedByOwner(ownerKey);
        if (maxPlaced > 0 && placedCount >= maxPlaced)
        {
            LogDebug($"Placement denied ownerKey={ownerKey} reason=\"max placed reached\" placed={placedCount} maxPlaced={maxPlaced}");
            TellHuman(player, $"Maximum blockades reached ({maxPlaced}).");
            return;
        }

        UpdatePreviewTransform(player, preview, context: "placement", logDetails: true);
        LogDebug($"Placement transformed ownerKey={ownerKey} position={FormatVectorForLog(preview.Position)} angles={FormatAnglesForLog(preview.Angles)} {DescribeEntity(preview.Entity)}");

        var isClearOfPlayers = IsPlacementClearOfPlayers(preview.Position, out var clearanceDetails);
        LogDebug($"Placement player-clearance ownerKey={ownerKey} clear={isClearOfPlayers} position={FormatVectorForLog(preview.Position)} {clearanceDetails}");
        if (!isClearOfPlayers)
        {
            TellHuman(player, "Cannot place a blockade on a player. Move the preview away first.");
            return;
        }

        var moneyBeforeSpend = state.Money;
        var spendSucceeded = _progressionService.TrySpendMoney(player, state, preview.Definition.Cost, out var moneyError);
        LogDebug($"Placement spend-money ownerKey={ownerKey} success={spendSucceeded} moneyBefore={moneyBeforeSpend} moneyAfter={state.Money} cost={preview.Definition.Cost} error={Quote(moneyError)}");
        if (!spendSucceeded)
        {
            RemovePreview(ownerKey, notify: false, reason: $"placement spend money failed: {moneyError}");
            TellHuman(player, $"{_config.MessagesConfig.NotEnoughMoney} {ChatColors.Yellow}{moneyError}{ChatColors.Default} Preview canceled.", center: false);
            return;
        }

        var entity = SpawnPlacedEntity(preview.Definition.Model, preview.Position, preview.Angles);
        LogDebug($"Placement placed-entity ownerKey={ownerKey} success={entity != null} model={Quote(preview.Definition.Model)} position={FormatVectorForLog(preview.Position)} angles={FormatAnglesForLog(preview.Angles)} {DescribeEntity(entity)}");
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

        RemovePreview(ownerKey, notify: false, reason: "placement succeeded");

        var label = preview.Definition.Variant == BlockadeVariant.Small ? "Small blockade" : "Blockade";
        LogDebug($"Placement succeeded ownerKey={ownerKey} label={Quote(label)} remainingMoney={state.Money} placedCount={CountPlacedByOwner(ownerKey)}");
        TellHuman(player, $"{ChatColors.Gold}{label}{ChatColors.Default} placed. {ChatText.Money(state.Money)} remaining.");
        _ = state;
    }

    private bool CanUseBlockades(CCSPlayerController player, bool notify, out PlayerState state)
    {
        return CanUseBlockades(player, notify, out state, out _);
    }

    private bool CanUseBlockades(CCSPlayerController player, bool notify, out PlayerState state, out string failureReason)
    {
        state = null!;
        failureReason = string.Empty;

        if (!_isPlacementRound())
        {
            failureReason = "placement round is not active";
            if (notify)
                TellHuman(player, "Blockades can only be used during active rounds.");

            return false;
        }

        if (!player.IsValid || !player.PawnIsAlive)
        {
            failureReason = !player.IsValid
                ? "player is invalid"
                : "player is not alive";
            if (notify)
                TellHuman(player, "You must be alive to place a blockade.");

            return false;
        }

        state = player.GetState(_playerStates);
        if (state.IsZombie)
        {
            failureReason = "player is zombie";
            if (notify)
                player.PrintToChat(ChatText.Zombie("Zombies cannot place blockades."));

            return false;
        }

        failureReason = "allowed";
        return true;
    }

    private CBaseModelEntity? SpawnPreviewEntity(string model, Vector position, QAngle angles)
    {
        LogDebug($"SpawnPreviewEntity requested model={Quote(model)} position={FormatVectorForLog(position)} angles={FormatAnglesForLog(angles)}");
        var entity = TryCreateModelEntity("prop_dynamic_override", model, solid: false, position, angles);
        if (entity == null)
        {
            LogDebug($"SpawnPreviewEntity failed model={Quote(model)} position={FormatVectorForLog(position)} angles={FormatAnglesForLog(angles)}");
            return null;
        }

        ApplyPreviewVisuals(entity);
        ConfigurePreviewCollision(entity);
        LogDebug($"SpawnPreviewEntity configured model={Quote(model)} {DescribeEntity(entity)}");
        return entity;
    }

    private CBaseModelEntity? SpawnPlacedEntity(string model, Vector position, QAngle angles)
    {
        LogDebug($"SpawnPlacedEntity requested model={Quote(model)} position={FormatVectorForLog(position)} angles={FormatAnglesForLog(angles)}");
        var entity = TryCreateModelEntity("prop_dynamic_override", model, solid: true, position, angles)
            ?? TryCreateModelEntity("prop_physics_override", model, solid: true, position, angles);

        if (entity == null)
        {
            LogDebug($"SpawnPlacedEntity failed model={Quote(model)} position={FormatVectorForLog(position)} angles={FormatAnglesForLog(angles)}");
            return null;
        }

        ApplyPlacedVisuals(entity);
        ConfigurePlacedCollision(entity);
        LogDebug($"SpawnPlacedEntity configured model={Quote(model)} {DescribeEntity(entity)}");
        return entity;
    }

    private CBaseModelEntity? TryCreateModelEntity(
        string designerName,
        string model,
        bool solid,
        Vector? position = null,
        QAngle? angles = null)
    {
        LogDebug($"CreateEntity requested designer={Quote(designerName)} model={Quote(model)} solid={solid} requestedPosition={FormatVectorForLog(position)} requestedAngles={FormatAnglesForLog(angles)}");
        var entity = Utilities.CreateEntityByName<CBaseModelEntity>(designerName);
        if (entity == null || !entity.IsValid)
        {
            LogDebug($"CreateEntity result designer={Quote(designerName)} model={Quote(model)} solid={solid} success=false {DescribeEntity(entity)}");
            return null;
        }

        var stage = "configure keyvalues";
        try
        {
            using var keyValues = new CEntityKeyValues();
            keyValues.SetString("model", model);
            keyValues.SetInt("solid", solid ? 6 : 0);
            keyValues.SetBool("disableshadows", true);
            if (position != null)
                keyValues.SetString("origin", FormatVector(position));
            if (angles != null)
                keyValues.SetString("angles", FormatAngles(angles));

            if (!solid)
            {
                keyValues.SetBool("CreateNonSolid", true);
                keyValues.SetBool("force_transmit_to_client", true);
            }

            LogDebug($"CreateEntity result designer={Quote(designerName)} model={Quote(model)} solid={solid} success=true {DescribeEntity(entity)}");
            stage = "DispatchSpawn";
            entity.DispatchSpawn(keyValues);
            LogDebug($"DispatchSpawn success designer={Quote(designerName)} model={Quote(model)} solid={solid} {DescribeEntity(entity)}");

            stage = "Teleport";
            if (position != null || angles != null)
            {
                entity.Teleport(position, angles, null);
                LogDebug($"PostTeleport designer={Quote(designerName)} model={Quote(model)} solid={solid} {DescribeEntity(entity)}");
            }
            else
            {
                LogDebug($"PostTeleport skipped designer={Quote(designerName)} model={Quote(model)} solid={solid} {DescribeEntity(entity)}");
            }

            stage = "SetModel";
            entity.SetModel(model);
            LogDebug($"PostSetModel designer={Quote(designerName)} model={Quote(model)} solid={solid} {DescribeEntity(entity)}");
            return entity;
        }
        catch (Exception ex)
        {
            LogWarning($"Entity spawn failed stage={Quote(stage)} designer={Quote(designerName)} model={Quote(model)} solid={solid} exception={Quote(ex.Message)} {DescribeEntity(entity)}");
            SafeRemove(entity);
            return null;
        }
    }

    private void UpdatePreviewTransform(
        CCSPlayerController player,
        PreviewBlockade preview,
        string context,
        bool logDetails)
    {
        if (!TryGetPreviewTransform(player, out var position, out var angles, context, logDetails))
        {
            if (logDetails)
                LogDebug($"UpdatePreviewTransform failed context={Quote(context)} {DescribePlayer(player)} {DescribeEntity(preview.Entity)}");

            return;
        }

        preview.Position = position;
        preview.Angles = angles;

        if (preview.Entity.IsValid)
        {
            preview.Entity.Teleport(position, angles, null);
            if (logDetails)
                LogDebug($"UpdatePreviewTransform teleported context={Quote(context)} position={FormatVectorForLog(position)} angles={FormatAnglesForLog(angles)} {DescribeEntity(preview.Entity)}");
        }
        else if (logDetails)
        {
            LogDebug($"UpdatePreviewTransform skipped teleport context={Quote(context)} reason=\"preview entity invalid\" position={FormatVectorForLog(position)} angles={FormatAnglesForLog(angles)} {DescribeEntity(preview.Entity)}");
        }
    }

    private bool TryGetPreviewTransform(
        CCSPlayerController player,
        out Vector position,
        out QAngle angles,
        string context,
        bool logDetails)
    {
        position = new Vector();
        angles = new QAngle();

        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null)
        {
            if (logDetails)
                LogDebug($"Preview transform failed context={Quote(context)} reason=\"pawn missing, invalid, or missing origin\" pawnNull={FormatBool(pawn == null)} pawnValid={(pawn == null ? "unknown" : FormatBool(pawn.IsValid))} {DescribePlayer(player)}");

            return false;
        }

        var origin = pawn.AbsOrigin;
        var forward = GetHorizontalForward(pawn.EyeAngles.ToForwardVector());
        var distance = Math.Clamp(_config.BlockadeConfig.PlacementDistance, 32.0f, 280.0f);
        var heightOffset = Math.Clamp(_config.BlockadeConfig.PlacementHeightOffset, -96.0f, 128.0f);
        position = new Vector(
            origin.X + forward.X * distance,
            origin.Y + forward.Y * distance,
            origin.Z + heightOffset);
        angles = new QAngle(0.0f, pawn.EyeAngles.Y, 0.0f);
        if (logDetails)
        {
            LogDebug($"Preview transform context={Quote(context)} origin={FormatVectorForLog(origin)} eyeAngles={FormatAnglesForLog(pawn.EyeAngles)} forward={FormatVectorForLog(forward)} distance={FormatFloat(distance)} heightOffset={FormatFloat(heightOffset)} resultPosition={FormatVectorForLog(position)} resultAngles={FormatAnglesForLog(angles)} {DescribePlayer(player)}");
        }

        return true;
    }

    private static string FormatVector(Vector vector) =>
        FormattableString.Invariant($"{vector.X} {vector.Y} {vector.Z}");

    private static string FormatAngles(QAngle angles) =>
        FormattableString.Invariant($"{angles.X} {angles.Y} {angles.Z}");

    private void LogDebug(string message)
    {
        if (!_config.BlockadeConfig.DebugLogging)
            return;

        Console.WriteLine($"{LogPrefix} {message}");
    }

    private static void LogWarning(string message)
    {
        Console.WriteLine($"{LogPrefix} {message}");
    }

    private static string DescribePlayer(CCSPlayerController player, PlayerState? state = null)
    {
        var name = SafeValue(() => player.PlayerName);
        var stateKey = SafeValue(() => player.IsValid ? player.GetStateKey().ToString() : "<invalid>");
        var steamId = SafeValue(() => player.SteamID.ToString());
        var team = SafeValue(() => player.TeamNum.ToString());
        var alive = SafeValue(() => FormatBool(player.PawnIsAlive));

        return $"player={Quote(name)} steamId={steamId} stateKey={stateKey} team={team} alive={alive} zombie={FormatBool(state?.IsZombie)} money={FormatInt(state?.Money)}";
    }

    private static string DescribeEntity(CBaseModelEntity? entity)
    {
        if (entity == null)
            return "entity=null";

        var index = SafeValue(() => entity.Index.ToString());
        var handle = SafeValue(() => entity.EntityHandle.ToString());
        var isValid = SafeValue(() => FormatBool(entity.IsValid));
        var origin = SafeValue(() => FormatVectorForLog(entity.AbsOrigin));

        return $"entityIndex={index} entityHandle={handle} isValid={isValid} origin={origin}";
    }

    private static string FormatVectorForLog(Vector? vector)
    {
        if (vector == null)
            return "<null>";

        return FormattableString.Invariant($"({vector.X:0.###}, {vector.Y:0.###}, {vector.Z:0.###})");
    }

    private static string FormatAnglesForLog(QAngle? angles)
    {
        if (angles == null)
            return "<null>";

        return FormattableString.Invariant($"({angles.X:0.###}, {angles.Y:0.###}, {angles.Z:0.###})");
    }

    private static string FormatFloat(float value) =>
        FormattableString.Invariant($"{value:0.###}");

    private static string FormatBool(bool value) =>
        value ? "true" : "false";

    private static string FormatBool(bool? value) =>
        value.HasValue ? FormatBool(value.Value) : "unknown";

    private static string FormatInt(int? value) =>
        value.HasValue ? value.Value.ToString() : "unknown";

    private static string Quote(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("\r", " ").Replace("\n", " ").Replace("\"", "'");

        return $"\"{text}\"";
    }

    private static string SafeValue(Func<object?> valueFactory)
    {
        try
        {
            return valueFactory()?.ToString() ?? "<null>";
        }
        catch (Exception ex)
        {
            return $"<error:{ex.GetType().Name}>";
        }
    }

    private void RefreshPreview(ulong ownerKey)
    {
        if (!_previews.TryGetValue(ownerKey, out var preview))
        {
            LogDebug($"RefreshPreview ownerKey={ownerKey} preview=not-found player=not-checked entity=not-checked");
            return;
        }

        var entityValid = preview.Entity.IsValid;
        var playerFound = TryFindPlayer(ownerKey, out var player);
        LogDebug($"RefreshPreview ownerKey={ownerKey} preview=found player={(playerFound ? "found" : "not-found")} entityValid={entityValid} position={FormatVectorForLog(preview.Position)} angles={FormatAnglesForLog(preview.Angles)} {DescribeEntity(preview.Entity)}");

        if (!entityValid || !playerFound)
            return;

        UpdatePreviewTransform(player, preview, context: "refresh", logDetails: true);
        ApplyPreviewVisuals(preview.Entity);
        ConfigurePreviewCollision(preview.Entity);
        LogDebug($"RefreshPreview applied visuals/collision ownerKey={ownerKey} position={FormatVectorForLog(preview.Position)} angles={FormatAnglesForLog(preview.Angles)} {DescribeEntity(preview.Entity)}");
    }

    private bool IsPlacementClearOfPlayers(Vector position, out string details)
    {
        var clearance = Math.Clamp(_config.BlockadeConfig.PlacementPlayerClearance, 32.0f, 160.0f);
        var verticalClearance = Math.Max(48.0f, clearance);
        details = $"clearance={FormatFloat(clearance)} verticalClearance={FormatFloat(verticalClearance)}";

        foreach (var player in Utilities.GetPlayers().Where(player => player is { IsValid: true } && player.PawnIsAlive))
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null)
                continue;

            var origin = pawn.AbsOrigin;
            var dx = origin.X - position.X;
            var dy = origin.Y - position.Y;
            var dz = MathF.Abs(origin.Z - position.Z);
            var distance2d = MathF.Sqrt(dx * dx + dy * dy);

            if (distance2d < clearance && dz < verticalClearance)
            {
                details = $"blockedBy={DescribePlayer(player)} playerOrigin={FormatVectorForLog(origin)} distance2d={FormatFloat(distance2d)} dz={FormatFloat(dz)} clearance={FormatFloat(clearance)} verticalClearance={FormatFloat(verticalClearance)}";
                return false;
            }
        }

        details = $"clear clearance={FormatFloat(clearance)} verticalClearance={FormatFloat(verticalClearance)}";
        return true;
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

    private void DamageBlockadesFromHeldHeavyAttacks()
    {
        if (!_isPlacementRound())
            return;

        foreach (var player in Utilities.GetPlayers().Where(player => player is { IsValid: true, PawnIsAlive: true }))
        {
            var state = player.GetState(_playerStates);
            if (!state.IsZombie || !player.Buttons.HasFlag(PlayerButtons.Attack2))
                continue;

            TryDamagePlacedBlockade(player, state);
        }
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
        zombie.PrintToChat(ChatText.Zombie($"{ChatColors.Gold}Blockade destroyed.{ChatColors.Default}"));
        NotifyOwner(blockade.OwnerKey, "Your blockade was destroyed.");

        foreach (var player in Utilities.GetPlayers().Where(player => player is { IsValid: true }))
        {
            var state = player.GetState(_playerStates);
            if (!state.IsZombie && player.PawnIsAlive)
                player.PrintToChat(ChatText.Human($"{ChatColors.Gold}A blockade was destroyed.{ChatColors.Default}"));
        }
    }

    private bool RemovePreview(ulong ownerKey, bool notify = false, string reason = "unspecified")
    {
        if (!_previews.Remove(ownerKey, out var preview))
        {
            LogDebug($"RemovePreview ownerKey={ownerKey} found=false notify={notify} reason={Quote(reason)}");
            return false;
        }

        LogDebug($"RemovePreview ownerKey={ownerKey} found=true notify={notify} reason={Quote(reason)} position={FormatVectorForLog(preview.Position)} angles={FormatAnglesForLog(preview.Angles)} {DescribeEntity(preview.Entity)}");
        SafeRemove(preview.Entity);
        LogDebug($"RemovePreview removed entity ownerKey={ownerKey} reason={Quote(reason)} {DescribeEntity(preview.Entity)}");

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
            LogWarning($"Failed to configure preview collision exception={Quote(ex.Message)} {DescribeEntity(entity)}");
        }
    }

    private static void ConfigurePlacedCollision(CBaseModelEntity entity)
    {
        try
        {
            entity.Collision.SolidType = SolidType_t.SOLID_VPHYSICS;
            entity.Collision.SolidFlags = 0;
            entity.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_PROPS;
            entity.Collision.EnablePhysics = 0;
            entity.MoveType = MoveType_t.MOVETYPE_NONE;
            entity.ActualMoveType = MoveType_t.MOVETYPE_NONE;
            entity.AcceptInput("EnableCollision");
            entity.AcceptInput("DisableMotion");
        }
        catch (Exception ex)
        {
            LogWarning($"Failed to configure placed collision exception={Quote(ex.Message)} {DescribeEntity(entity)}");
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
        owner.PrintToChat(ChatText.Human(message));
    }

    private void TellHuman(CCSPlayerController player, string message, bool center = true)
    {
        player.PrintToChat(ChatText.Human(message));
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
