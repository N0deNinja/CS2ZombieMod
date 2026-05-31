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
    private const string PreviewBeamMaterial = "materials/sprites/laserbeam.vtex";
    private const float PreviewBeamWidth = 0.8f;
    private const uint NoDrawEffect = (uint)EntityEffects_t.EF_NODRAW;
    private const uint NoDrawButTransmitEffect = (uint)EntityEffects_t.EF_NODRAW_BUT_TRANSMIT;
    private static readonly Color PreviewBeamColor = Color.FromArgb(185, 255, 224, 32);
    private static readonly (int Start, int End)[] PreviewBoxEdges =
    [
        (0, 1), (1, 2), (2, 3), (3, 0),
        (4, 5), (5, 6), (6, 7), (7, 4),
        (0, 4), (1, 5), (2, 6), (3, 7)
    ];

    private readonly Dictionary<ulong, PlayerState> _playerStates;
    private readonly BaseConfig _config;
    private readonly BasePlugin _plugin;
    private readonly ProgressionService _progressionService;
    private readonly Func<bool> _isPlacementRound;
    private readonly Func<string> _describePlacementGate;
    private readonly Dictionary<ulong, PreviewBlockade> _previews = [];
    private readonly List<PlacedBlockade> _placedBlockades = [];
    private readonly Dictionary<ulong, DateTime> _nextZombieHitAtUtc = [];
    private int _previewAuditSequence;

    public BlockadeService(
        Dictionary<ulong, PlayerState> playerStates,
        BaseConfig config,
        BasePlugin plugin,
        ProgressionService progressionService,
        Func<bool> isPlacementRound,
        Func<string>? describePlacementGate = null)
    {
        _playerStates = playerStates;
        _config = config;
        _plugin = plugin;
        _progressionService = progressionService;
        _isPlacementRound = isPlacementRound;
        _describePlacementGate = describePlacementGate ?? (() => $"allowed={_isPlacementRound()}");
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
                LogDebug($"OnTick removing preview ownerKey={ownerKey} reason=\"player not found\" placementGate={Quote(DescribePlacementGate())} {DescribePreview(preview)}");
                RemovePreview(ownerKey, reason: "tick player not found");
                continue;
            }

            if (!CanUseBlockades(player, notify: false, out _, out var failureReason))
            {
                LogDebug($"OnTick removing preview ownerKey={ownerKey} reason=\"CanUseBlockades failed: {failureReason}\" placementGate={Quote(DescribePlacementGate())} {DescribePreview(preview)} {DescribePlayer(player)}");
                TellPreviewUnavailable(player, ToPreviewUnavailableReason(failureReason));
                RemovePreview(ownerKey, reason: $"tick CanUseBlockades failed: {failureReason}");
                continue;
            }

            preview.TickCount++;
            UpdatePreviewTransform(player, preview, context: "tick", logDetails: false);
            UpdatePreviewOutline(preview, "tick");
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
            RemovePreviewOutline(preview);

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
        LogDebug($"Command start argCount={command.ArgCount} rawAction={Quote(rawAction)} enabled={_config.BlockadeConfig.Enabled} placementGate={Quote(DescribePlacementGate())} {DescribePlayer(player)}");

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
        var hadExistingPreview = _previews.TryGetValue(ownerKey, out var existingPreview);
        LogDebug($"StartPreview requested ownerKey={ownerKey} variant={definition.Name} cost={definition.Cost} model={Quote(definition.Model)} previewCount={_previews.Count} hadExistingPreview={FormatBool(hadExistingPreview)} existing={DescribePreview(existingPreview)} placed={DescribePlacedBlockades(ownerKey)} placementGate={Quote(DescribePlacementGate())} {DescribePlayer(player, existingState)}");

        if (!CanUseBlockades(player, notify: true, out var state, out var failureReason))
        {
            LogDebug($"StartPreview denied ownerKey={ownerKey} reason={Quote(failureReason)} variant={definition.Name} cost={definition.Cost} model={Quote(definition.Model)} previewCount={_previews.Count} placed={DescribePlacedBlockades(ownerKey)} placementGate={Quote(DescribePlacementGate())} {DescribePlayer(player, state)}");
            return;
        }

        LogDebug($"StartPreview CanUseBlockades allowed ownerKey={ownerKey} variant={definition.Name} cost={definition.Cost} model={Quote(definition.Model)} previewCount={_previews.Count} placed={DescribePlacedBlockades(ownerKey)} placementGate={Quote(DescribePlacementGate())} {DescribePlayer(player, state)}");

        PruneInvalidPlacedBlockades();

        var maxPlaced = Math.Max(0, _config.BlockadeConfig.MaxPlacedPerPlayer);
        var placedCount = CountPlacedByOwner(ownerKey);
        if (maxPlaced > 0 && placedCount >= maxPlaced)
        {
            LogDebug($"StartPreview denied ownerKey={ownerKey} reason=\"max placed reached\" placed={placedCount} maxPlaced={maxPlaced} previewCount={_previews.Count} placedDetails={DescribePlacedBlockades(ownerKey)}");
            TellPreviewUnavailable(player, $"maximum blockades reached ({maxPlaced}).");
            return;
        }

        if (string.IsNullOrWhiteSpace(definition.Model))
        {
            LogDebug($"StartPreview denied ownerKey={ownerKey} reason=\"model not configured\" variant={definition.Name}");
            TellPreviewUnavailable(player, "blockade model is not configured.");
            return;
        }

        if (state.Money < definition.Cost)
        {
            LogDebug($"StartPreview denied ownerKey={ownerKey} reason=\"not enough money\" money={state.Money} cost={definition.Cost} variant={definition.Name} model={Quote(definition.Model)}");
            _progressionService.ApplyInGameMoney(player, state);
            TellPreviewUnavailable(player, $"not enough money. Need {definition.Cost}, you have {state.Money}.", center: false);
            return;
        }

        if (!TryGetPreviewTransform(player, out var position, out var angles, context: "start", logDetails: true))
        {
            LogDebug($"StartPreview denied ownerKey={ownerKey} reason=\"preview transform unavailable\" variant={definition.Name} model={Quote(definition.Model)}");
            TellPreviewUnavailable(player, "could not find a valid preview position.");
            return;
        }

        LogDebug($"StartPreview transform-ready ownerKey={ownerKey} variant={definition.Name} position={FormatVectorForLog(position)} angles={FormatAnglesForLog(angles)} nearest={DescribeNearestPlacedBlockade(ownerKey, position)}");

        var removedExisting = RemovePreview(ownerKey, notify: false, reason: "start replacing existing preview");
        LogDebug($"StartPreview after-remove-existing ownerKey={ownerKey} removedExisting={FormatBool(removedExisting)} previewCount={_previews.Count} placed={DescribePlacedBlockades(ownerKey)}");

        var previewId = System.Threading.Interlocked.Increment(ref _previewAuditSequence);
        var previewBounds = GetPreviewLocalBounds(definition.Variant);
        var preview = new PreviewBlockade(previewId, definition, previewBounds.Mins, previewBounds.Maxs)
        {
            Position = position,
            Angles = angles
        };
        _previews[ownerKey] = preview;
        LogDebug($"StartPreview stored ownerKey={ownerKey} {DescribePreview(preview)} previewCount={_previews.Count} nearest={DescribeNearestPlacedBlockade(ownerKey, position)}");
        RefreshPreview(ownerKey, "start-immediate");
        SchedulePreviewAudit(ownerKey, previewId, "start-next-frame", TimeSpan.Zero);
        SchedulePreviewAudit(ownerKey, previewId, "start-150ms", TimeSpan.FromMilliseconds(150));
        SchedulePreviewAudit(ownerKey, previewId, "start-600ms", TimeSpan.FromMilliseconds(600));

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
            LogDebug($"Placement attempt ownerKey={ownerKey} preview=missing previewCount={_previews.Count} placed={DescribePlacedBlockades(ownerKey)} {DescribePlayer(player, state)}");
            return;
        }

        LogDebug($"Placement attempt ownerKey={ownerKey} preview=exists cost={preview.Definition.Cost} model={Quote(preview.Definition.Model)} {DescribePreview(preview)} nearest={DescribeNearestPlacedBlockade(ownerKey, preview.Position)} {DescribePlayer(player, state)}");

        if (!CanUseBlockades(player, notify: true, out _, out var failureReason))
        {
            LogDebug($"Placement denied ownerKey={ownerKey} reason=\"CanUseBlockades failed: {failureReason}\" {DescribePreview(preview)} placementGate={Quote(DescribePlacementGate())} {DescribePlayer(player, state)}");
            RemovePreview(ownerKey, reason: $"placement CanUseBlockades failed: {failureReason}");
            return;
        }

        PruneInvalidPlacedBlockades();

        var maxPlaced = Math.Max(0, _config.BlockadeConfig.MaxPlacedPerPlayer);
        var placedCount = CountPlacedByOwner(ownerKey);
        if (maxPlaced > 0 && placedCount >= maxPlaced)
        {
            LogDebug($"Placement denied ownerKey={ownerKey} reason=\"max placed reached\" placed={placedCount} maxPlaced={maxPlaced} {DescribePreview(preview)} placedDetails={DescribePlacedBlockades(ownerKey)}");
            TellHuman(player, $"Maximum blockades reached ({maxPlaced}).");
            return;
        }

        UpdatePreviewTransform(player, preview, context: "placement", logDetails: true);
        LogDebug($"Placement transformed ownerKey={ownerKey} {DescribePreview(preview)} nearest={DescribeNearestPlacedBlockade(ownerKey, preview.Position)}");

        var isClearOfPlayers = IsPlacementClearOfPlayers(preview.Position, out var clearanceDetails);
        LogDebug($"Placement player-clearance ownerKey={ownerKey} clear={isClearOfPlayers} {DescribePreview(preview)} {clearanceDetails}");
        if (!isClearOfPlayers)
        {
            TellHuman(player, "Cannot place a blockade on a player. Move the preview away first.");
            return;
        }

        var moneyBeforeSpend = state.Money;
        var spendSucceeded = _progressionService.TrySpendMoney(player, state, preview.Definition.Cost, out var moneyError);
        LogDebug($"Placement spend-money ownerKey={ownerKey} success={spendSucceeded} moneyBefore={moneyBeforeSpend} moneyAfter={state.Money} cost={preview.Definition.Cost} error={Quote(moneyError)} {DescribePreview(preview)}");
        if (!spendSucceeded)
        {
            RemovePreview(ownerKey, notify: false, reason: $"placement spend money failed: {moneyError}");
            TellHuman(player, $"{_config.MessagesConfig.NotEnoughMoney} {ChatColors.Yellow}{moneyError}{ChatColors.Default} Preview canceled.", center: false);
            return;
        }

        var entity = SpawnPlacedEntity(preview.Definition.Model, preview.Position, preview.Angles);
        LogDebug($"Placement placed-entity ownerKey={ownerKey} success={entity != null} model={Quote(preview.Definition.Model)} {DescribePreview(preview)} {DescribeEntity(entity)}");
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
        LogDebug($"Placement succeeded ownerKey={ownerKey} label={Quote(label)} remainingMoney={state.Money} placedCount={CountPlacedByOwner(ownerKey)} placed={DescribePlacedBlockades(ownerKey)}");
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
                TellPreviewUnavailable(player, ToPreviewUnavailableReason(failureReason));

            return false;
        }

        if (!player.IsValid || !player.PawnIsAlive)
        {
            failureReason = !player.IsValid
                ? "player is invalid"
                : "player is not alive";
            if (notify)
                TellPreviewUnavailable(player, ToPreviewUnavailableReason(failureReason));

            return false;
        }

        state = player.GetState(_playerStates);
        if (state.IsZombie)
        {
            failureReason = "player is zombie";
            if (notify)
                TellPreviewUnavailable(player, ToPreviewUnavailableReason(failureReason));

            return false;
        }

        failureReason = "allowed";
        return true;
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
                LogDebug($"UpdatePreviewTransform failed context={Quote(context)} {DescribePreview(preview)} {DescribePlayer(player)}");

            return;
        }

        preview.Position = position;
        preview.Angles = angles;

        if (logDetails)
            LogDebug($"UpdatePreviewTransform updated-outline-target context={Quote(context)} {DescribePreview(preview)} nearest={DescribeNearestPlacedBlockade(player.GetStateKey(), position)}");
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

        var designerName = SafeValue(() => entity.DesignerName);
        var index = SafeValue(() => entity.Index.ToString());
        var handle = SafeValue(() => entity.EntityHandle.ToString());
        var flags = SafeValue(() => entity.CBodyComponent?.SceneNode?.Owner?.Entity?.Flags.ToString() ?? "<null>");
        var isValid = SafeValue(() => FormatBool(entity.IsValid));
        var origin = SafeValue(() => FormatVectorForLog(entity.AbsOrigin));
        var effects = SafeValue(() => entity.Effects.ToString());
        var renderMode = SafeValue(() => entity.RenderMode.ToString());
        var renderFx = SafeValue(() => entity.RenderFX.ToString());
        var solidType = SafeValue(() => entity.Collision?.SolidType.ToString() ?? "<null>");
        var collisionGroup = SafeValue(() => entity.Collision?.CollisionGroup.ToString() ?? "<null>");
        var collisionMins = SafeValue(() => FormatVectorForLog(entity.Collision?.Mins));
        var collisionMaxs = SafeValue(() => FormatVectorForLog(entity.Collision?.Maxs));

        return $"designer={Quote(designerName)} entityIndex={index} entityHandle={handle} isValid={isValid} origin={origin} flags={flags} effects={effects} renderMode={renderMode} renderFx={renderFx} solidType={solidType} collisionGroup={collisionGroup} collisionMins={collisionMins} collisionMaxs={collisionMaxs}";
    }

    private static string DescribePreview(PreviewBlockade? preview)
    {
        if (preview == null)
            return "preview=null";

        var ageMs = (DateTime.UtcNow - preview.CreatedAtUtc).TotalMilliseconds;
        var outlineValid = preview.OutlineBeams.Count(beam => beam is { IsValid: true });
        return $"previewId={preview.PreviewId} previewAgeMs={FormatDouble(ageMs)} refreshCount={preview.RefreshCount} tickCount={preview.TickCount} outlineBeams={outlineValid}/{preview.OutlineBeams.Count} variant={preview.Definition.Name} position={FormatVectorForLog(preview.Position)} angles={FormatAnglesForLog(preview.Angles)} boundsMins={FormatVectorForLog(preview.BoundsMins)} boundsMaxs={FormatVectorForLog(preview.BoundsMaxs)}";
    }

    private string DescribePlacedBlockades(ulong ownerKey)
    {
        var validPlaced = _placedBlockades
            .Where(blockade => blockade.OwnerKey == ownerKey && blockade.Entity.IsValid)
            .Take(4)
            .Select(blockade =>
            {
                var position = blockade.Entity.AbsOrigin ?? blockade.Position;
                return $"{blockade.Definition.Name}@{FormatVectorForLog(position)} hits={blockade.Hits}/{blockade.Definition.MaxHits} entityIndex={SafeValue(() => blockade.Entity.Index.ToString())}";
            })
            .ToArray();

        var count = CountPlacedByOwner(ownerKey);
        return validPlaced.Length == 0
            ? $"placedCount={count} placedList=none"
            : $"placedCount={count} placedList={Quote(string.Join(" | ", validPlaced))}";
    }

    private string DescribeNearestPlacedBlockade(ulong ownerKey, Vector position)
    {
        PlacedBlockade? nearest = null;
        Vector? nearestPosition = null;
        var nearestDistance = float.MaxValue;
        var count = 0;

        foreach (var blockade in _placedBlockades)
        {
            if (blockade.OwnerKey != ownerKey || !blockade.Entity.IsValid)
                continue;

            count++;
            var blockadePosition = blockade.Entity.AbsOrigin ?? blockade.Position;
            var dx = blockadePosition.X - position.X;
            var dy = blockadePosition.Y - position.Y;
            var dz = blockadePosition.Z - position.Z;
            var distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            if (distance >= nearestDistance)
                continue;

            nearest = blockade;
            nearestPosition = blockadePosition;
            nearestDistance = distance;
        }

        if (nearest == null)
            return $"ownerPlaced={count} nearestPlaced=none";

        return $"ownerPlaced={count} nearestPlacedDistance={FormatFloat(nearestDistance)} nearestPlacedVariant={nearest.Definition.Name} nearestPlacedPosition={FormatVectorForLog(nearestPosition)} nearestPlacedEntityIndex={SafeValue(() => nearest.Entity.Index.ToString())}";
    }

    private string DescribePlacementGate() =>
        SafeValue(() => _describePlacementGate());

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

    private static string FormatDouble(double value) =>
        FormattableString.Invariant($"{value:0.#}");

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

    private void SchedulePreviewAudit(ulong ownerKey, int previewId, string context, TimeSpan delay)
    {
        _ = RunPreviewAuditAsync(ownerKey, previewId, context, delay);
    }

    private async Task RunPreviewAuditAsync(ulong ownerKey, int previewId, string context, TimeSpan delay)
    {
        try
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay);

            Server.NextFrame(() => AuditPreview(ownerKey, previewId, context));
        }
        catch (Exception ex)
        {
            LogWarning($"PreviewAudit schedule failed ownerKey={ownerKey} requestedPreviewId={previewId} context={Quote(context)} exception={Quote(ex.Message)}");
        }
    }

    private void AuditPreview(ulong ownerKey, int previewId, string context)
    {
        var found = _previews.TryGetValue(ownerKey, out var preview);
        var playerFound = TryFindPlayer(ownerKey, out var player);
        PlayerState? state = null;
        if (playerFound)
            _playerStates.TryGetValue(ownerKey, out state);

        var samePreview = found && preview!.PreviewId == previewId;
        var playerDetails = playerFound
            ? DescribePlayer(player, state)
            : "player=not-found";
        var nearestDetails = found
            ? DescribeNearestPlacedBlockade(ownerKey, preview!.Position)
            : "nearestPlaced=not-checked";

        LogDebug($"PreviewAudit context={Quote(context)} ownerKey={ownerKey} requestedPreviewId={previewId} found={FormatBool(found)} samePreview={FormatBool(samePreview)} previewCount={_previews.Count} {DescribePreview(preview)} placed={DescribePlacedBlockades(ownerKey)} nearest={nearestDetails} placementGate={Quote(DescribePlacementGate())} {playerDetails}");

        if (!samePreview || preview == null || !playerFound)
            return;

        UpdatePreviewTransform(player, preview, context: $"audit:{context}", logDetails: true);
        UpdatePreviewOutline(preview, $"audit:{context}");
        LogDebug($"PreviewAudit refreshed current preview context={Quote(context)} ownerKey={ownerKey} {DescribePreview(preview)} nearest={DescribeNearestPlacedBlockade(ownerKey, preview.Position)}");
    }

    private void RefreshPreview(ulong ownerKey, string context)
    {
        if (!_previews.TryGetValue(ownerKey, out var preview))
        {
            LogDebug($"RefreshPreview ownerKey={ownerKey} context={Quote(context)} preview=not-found player=not-checked entity=not-checked previewCount={_previews.Count}");
            return;
        }

        preview.RefreshCount++;
        var playerFound = TryFindPlayer(ownerKey, out var player);
        LogDebug($"RefreshPreview ownerKey={ownerKey} context={Quote(context)} preview=found player={(playerFound ? "found" : "not-found")} {DescribePreview(preview)} nearest={DescribeNearestPlacedBlockade(ownerKey, preview.Position)}");

        if (!playerFound)
            return;

        UpdatePreviewTransform(player, preview, context: "refresh", logDetails: true);
        UpdatePreviewOutline(preview, context);
        LogDebug($"RefreshPreview applied outline ownerKey={ownerKey} context={Quote(context)} {DescribePreview(preview)} nearest={DescribeNearestPlacedBlockade(ownerKey, preview.Position)}");
    }

    private void UpdatePreviewOutline(PreviewBlockade preview, string context)
    {
        try
        {
            if (preview.OutlineBeams.Count != PreviewBoxEdges.Length || preview.OutlineBeams.Any(beam => beam == null || !beam.IsValid))
            {
                RemovePreviewOutline(preview);
                if (!TryCreatePreviewOutline(preview, context))
                    return;
            }

            ApplyPreviewOutlinePositions(preview);
        }
        catch (Exception ex)
        {
            LogWarning($"Preview outline update failed context={Quote(context)} exception={Quote(ex.Message)} {DescribePreview(preview)}");
            RemovePreviewOutline(preview);
        }
    }

    private bool TryCreatePreviewOutline(PreviewBlockade preview, string context)
    {
        for (var i = 0; i < PreviewBoxEdges.Length; i++)
        {
            var beam = CreatePreviewBeam(preview.Position);
            if (beam == null)
            {
                LogWarning($"Preview outline beam creation failed context={Quote(context)} beamIndex={i} {DescribePreview(preview)}");
                RemovePreviewOutline(preview);
                return false;
            }

            preview.OutlineBeams.Add(beam);
        }

        LogDebug($"Preview outline created context={Quote(context)} beamCount={preview.OutlineBeams.Count} {DescribePreview(preview)}");
        return true;
    }

    private static CBeam? CreatePreviewBeam(Vector position)
    {
        var beam = Utilities.CreateEntityByName<CBeam>("env_beam");
        if (beam == null || !beam.IsValid)
            return null;

        beam.BeamType = BeamType_t.BEAM_POINTS;
        beam.NumBeamEnts = 2;
        beam.ClipStyle = BeamClipStyle_t.kNOCLIP;
        beam.TurnedOff = false;
        beam.Amplitude = 0.0f;
        beam.Speed = 0.0f;
        beam.FrameRate = 0.0f;
        beam.HDRColorScale = 1.0f;
        beam.FadeLength = 0.0f;
        beam.Width = PreviewBeamWidth;
        beam.EndWidth = PreviewBeamWidth;
        beam.RenderMode = RenderMode_t.kRenderTransAlpha;
        beam.RenderFX = RenderFx_t.kRenderFxNone;
        beam.Render = PreviewBeamColor;
        beam.SetModel(PreviewBeamMaterial);
        beam.Teleport(position, null, null);
        beam.DispatchSpawn();
        beam.EndPos.X = position.X;
        beam.EndPos.Y = position.Y;
        beam.EndPos.Z = position.Z;
        MarkPreviewBeamStateChanged(beam);
        beam.MarkRenderStateChanged();
        return beam;
    }

    private static void ApplyPreviewOutlinePositions(PreviewBlockade preview)
    {
        var corners = GetPreviewBoxCorners(preview);

        for (var i = 0; i < PreviewBoxEdges.Length; i++)
        {
            var beam = preview.OutlineBeams[i];
            if (!beam.IsValid)
                continue;

            var edge = PreviewBoxEdges[i];
            var start = corners[edge.Start];
            var end = corners[edge.End];
            beam.Teleport(start, null, null);
            beam.EndPos.X = end.X;
            beam.EndPos.Y = end.Y;
            beam.EndPos.Z = end.Z;
            MarkPreviewBeamStateChanged(beam);
            beam.MarkRenderStateChanged();
        }
    }

    private static Vector[] GetPreviewBoxCorners(PreviewBlockade preview)
    {
        var (mins, maxs) = (preview.BoundsMins, preview.BoundsMaxs);
        var yaw = preview.Angles.Y * MathF.PI / 180.0f;
        var forward = new Vector(MathF.Cos(yaw), MathF.Sin(yaw), 0.0f);
        var right = new Vector(-MathF.Sin(yaw), MathF.Cos(yaw), 0.0f);

        return
        [
            PreviewBoxCorner(preview.Position, forward, right, mins.X, mins.Y, mins.Z),
            PreviewBoxCorner(preview.Position, forward, right, maxs.X, mins.Y, mins.Z),
            PreviewBoxCorner(preview.Position, forward, right, maxs.X, maxs.Y, mins.Z),
            PreviewBoxCorner(preview.Position, forward, right, mins.X, maxs.Y, mins.Z),
            PreviewBoxCorner(preview.Position, forward, right, mins.X, mins.Y, maxs.Z),
            PreviewBoxCorner(preview.Position, forward, right, maxs.X, mins.Y, maxs.Z),
            PreviewBoxCorner(preview.Position, forward, right, maxs.X, maxs.Y, maxs.Z),
            PreviewBoxCorner(preview.Position, forward, right, mins.X, maxs.Y, maxs.Z)
        ];
    }

    private static (Vector Mins, Vector Maxs) GetPreviewLocalBounds(BlockadeVariant variant)
    {
        return variant == BlockadeVariant.Small
            ? (new Vector(-20.0f, -64.0f, 0.0f), new Vector(20.0f, 64.0f, 45.844f))
            : (new Vector(-29.05f, -31.093f, -0.075f), new Vector(27.982f, 31.093f, 50.857f));
    }

    private static Vector PreviewBoxCorner(Vector origin, Vector forward, Vector right, float forwardOffset, float rightOffset, float zOffset)
    {
        return new Vector(
            origin.X + forward.X * forwardOffset + right.X * rightOffset,
            origin.Y + forward.Y * forwardOffset + right.Y * rightOffset,
            origin.Z + zOffset);
    }

    private static void RemovePreviewOutline(PreviewBlockade preview)
    {
        foreach (var beam in preview.OutlineBeams)
            SafeRemove(beam);

        preview.OutlineBeams.Clear();
    }

    private static void MarkPreviewBeamStateChanged(CBeam beam)
    {
        Utilities.SetStateChanged(beam, "CBeam", "m_nBeamType");
        Utilities.SetStateChanged(beam, "CBeam", "m_nNumBeamEnts");
        Utilities.SetStateChanged(beam, "CBeam", "m_nClipStyle");
        Utilities.SetStateChanged(beam, "CBeam", "m_bTurnedOff");
        Utilities.SetStateChanged(beam, "CBeam", "m_fAmplitude");
        Utilities.SetStateChanged(beam, "CBeam", "m_fSpeed");
        Utilities.SetStateChanged(beam, "CBeam", "m_flFrameRate");
        Utilities.SetStateChanged(beam, "CBeam", "m_flHDRColorScale");
        Utilities.SetStateChanged(beam, "CBeam", "m_fFadeLength");
        Utilities.SetStateChanged(beam, "CBeam", "m_fWidth");
        Utilities.SetStateChanged(beam, "CBeam", "m_fEndWidth");
        Utilities.SetStateChanged(beam, "CBeam", "m_vecEndPos");
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
            LogDebug($"RemovePreview ownerKey={ownerKey} found=false notify={notify} reason={Quote(reason)} previewCount={_previews.Count}");
            return false;
        }

        LogDebug($"RemovePreview ownerKey={ownerKey} found=true notify={notify} reason={Quote(reason)} previewCountAfterRemove={_previews.Count} {DescribePreview(preview)} nearest={DescribeNearestPlacedBlockade(ownerKey, preview.Position)}");
        RemovePreviewOutline(preview);
        LogDebug($"RemovePreview removed outline ownerKey={ownerKey} reason={Quote(reason)} {DescribePreview(preview)}");

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

    private static void ApplyPlacedVisuals(CBaseModelEntity entity)
    {
        entity.RenderMode = RenderMode_t.kRenderNormal;
        entity.RenderFX = RenderFx_t.kRenderFxNone;
        entity.Render = Color.FromArgb(255, 255, 255, 255);
        entity.Effects &= ~(NoDrawEffect | NoDrawButTransmitEffect);
        entity.MarkRenderStateChanged();
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

    private static void TellPreviewUnavailable(CCSPlayerController player, string reason, bool center = true)
    {
        var message = $"Block preview unavailable: {reason}";
        player.PrintToChat(ChatText.Error(message));
        if (center)
            player.PrintToCenter(message);
    }

    private static string ToPreviewUnavailableReason(string failureReason)
    {
        return failureReason switch
        {
            "placement round is not active" => "placement phase is not active.",
            "player is invalid" => "player is no longer valid.",
            "player is not alive" => "you must be alive.",
            "player is zombie" => "zombies cannot place blockades.",
            _ => failureReason.EndsWith('.') ? failureReason : $"{failureReason}."
        };
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
        public PreviewBlockade(
            int previewId,
            BlockadeDefinition definition,
            Vector boundsMins,
            Vector boundsMaxs)
        {
            PreviewId = previewId;
            Definition = definition;
            BoundsMins = boundsMins;
            BoundsMaxs = boundsMaxs;
        }

        public int PreviewId { get; }
        public DateTime CreatedAtUtc { get; } = DateTime.UtcNow;
        public BlockadeDefinition Definition { get; }
        public Vector BoundsMins { get; }
        public Vector BoundsMaxs { get; }
        public List<CBeam> OutlineBeams { get; } = [];
        public Vector Position { get; set; } = new();
        public QAngle Angles { get; set; } = new();
        public int RefreshCount { get; set; }
        public int TickCount { get; set; }
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
