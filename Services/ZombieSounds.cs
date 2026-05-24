using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using ZombieModPlugin.Configs;
using ZombieModPlugin.Extensions;

namespace ZombieModPlugin.Sounds;

public static class ZombieSounds
{
    private static readonly object TrackedSoundsLock = new();
    private static readonly Dictionary<ulong, List<TrackedPlayerSound>> TrackedPlayerSounds = [];

    public static bool Emit(CBaseEntity? entity, BaseConfig config, string? soundEventName)
    {
        if (!config.SoundConfig.Enabled
            || string.IsNullOrWhiteSpace(soundEventName)
            || entity == null
            || !entity.IsValid)
        {
            return false;
        }

        try
        {
            entity.EmitSound(
                soundEventName.Trim(),
                volume: Math.Clamp(config.SoundConfig.EmitVolume, 0.0f, 2.0f),
                pitch: Math.Clamp(config.SoundConfig.EmitPitch, 0.1f, 3.0f));
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ZombieMod] Failed to emit sound '{soundEventName}': {ex.Message}");
            return false;
        }
    }

    public static bool EmitToPlayerOnly(CCSPlayerController? player, BaseConfig config, string? soundEventName)
    {
        var sound = NormalizeSingle(soundEventName);
        if (!config.SoundConfig.Enabled || sound == null || player == null || !player.IsValid)
            return false;

        CBaseEntity? emitter = player.PlayerPawn.Value;
        if (emitter == null || !emitter.IsValid)
            emitter = player;

        try
        {
            emitter.EmitSound(
                sound,
                recipients: new RecipientFilter(player),
                volume: Math.Clamp(config.SoundConfig.EmitVolume, 0.0f, 2.0f),
                pitch: Math.Clamp(config.SoundConfig.EmitPitch, 0.1f, 3.0f));
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ZombieMod] Failed to emit player-only sound '{sound}': {ex.Message}");
            return false;
        }
    }

    public static CBaseEntity? StartWorldSound(Vector position, BaseConfig config, string? soundEventName)
    {
        var sound = NormalizeSingle(soundEventName);
        if (!config.SoundConfig.Enabled || sound == null)
            return null;

        var soundPoint = Utilities.CreateEntityByName<CBaseEntity>("snd_event_point")
            ?? Utilities.CreateEntityByName<CBaseEntity>("point_soundevent");
        if (soundPoint == null || !soundPoint.IsValid)
            return null;

        try
        {
            soundPoint.Teleport(position, null, null);
            soundPoint.DispatchSpawn();
            soundPoint.AcceptInput("SetSoundEventName", value: sound);
            soundPoint.AcceptInput("StartSound");
            return soundPoint;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ZombieMod] Failed to start world sound '{sound}': {ex.Message}");
            StopSoundEntity(soundPoint);
            return null;
        }
    }

    public static void StopWorldSound(CBaseEntity? soundPoint)
    {
        StopSoundEntity(soundPoint);
    }

    public static bool Emit(CCSPlayerController? player, BaseConfig config, string? soundEventName)
    {
        if (!config.SoundConfig.UseTrackedPlayerSounds)
            return Emit(player?.PlayerPawn.Value, config, soundEventName);

        return EmitTracked(player, config, soundEventName);
    }

    public static bool EmitWithExtras(
        CBaseEntity? entity,
        BaseConfig config,
        string? primarySoundEventName,
        IEnumerable<string>? extraSoundEventNames)
    {
        return EmitFromPool(entity, config, primarySoundEventName, extraSoundEventNames, configuredSoundEventNames: null);
    }

    public static bool EmitWithExtras(
        CCSPlayerController? player,
        BaseConfig config,
        string? primarySoundEventName,
        IEnumerable<string>? extraSoundEventNames)
    {
        return EmitFromPool(player, config, primarySoundEventName, extraSoundEventNames, configuredSoundEventNames: null);
    }

    public static bool EmitAbilityActivation(CBaseEntity? entity, BaseConfig config, AbilitySettingsConfig abilityConfig)
    {
        return EmitFromPool(
            entity,
            config,
            abilityConfig.ActivationSound,
            abilityConfig.ExtraActivationSounds,
            abilityConfig.ActivationSounds);
    }

    public static bool EmitAbilityActivation(CCSPlayerController? player, BaseConfig config, AbilitySettingsConfig abilityConfig)
    {
        return EmitFromPool(
            player,
            config,
            abilityConfig.ActivationSound,
            abilityConfig.ExtraActivationSounds,
            abilityConfig.ActivationSounds);
    }

    public static bool EmitFromPool(
        CBaseEntity? entity,
        BaseConfig config,
        string? primarySoundEventName,
        IEnumerable<string>? fallbackSoundEventNames,
        IEnumerable<string>? configuredSoundEventNames)
    {
        var configuredChoices = Normalize(configuredSoundEventNames);
        if (configuredChoices.Length > 0)
            return EmitRandom(entity, config, configuredChoices);

        return EmitRandom(entity, config, Merge(primarySoundEventName, fallbackSoundEventNames));
    }

    public static bool EmitFromPool(
        CCSPlayerController? player,
        BaseConfig config,
        string? primarySoundEventName,
        IEnumerable<string>? fallbackSoundEventNames,
        IEnumerable<string>? configuredSoundEventNames)
    {
        var configuredChoices = Normalize(configuredSoundEventNames);
        if (configuredChoices.Length > 0)
            return EmitRandom(player, config, configuredChoices);

        return EmitRandom(player, config, Merge(primarySoundEventName, fallbackSoundEventNames));
    }

    public static bool EmitRandom(CBaseEntity? entity, BaseConfig config, IEnumerable<string?>? soundEventNames)
    {
        if (!config.SoundConfig.Enabled || entity == null || !entity.IsValid || soundEventNames == null)
            return false;

        var choices = Normalize(soundEventNames);
        if (choices.Length == 0)
            return false;

        return Emit(entity, config, choices[Random.Shared.Next(choices.Length)]);
    }

    public static bool EmitRandom(CCSPlayerController? player, BaseConfig config, IEnumerable<string?>? soundEventNames)
    {
        if (!config.SoundConfig.Enabled || player == null || !player.IsValid || soundEventNames == null)
            return false;

        var choices = Normalize(soundEventNames);
        if (choices.Length == 0)
            return false;

        return Emit(player, config, choices[Random.Shared.Next(choices.Length)]);
    }

    public static void StopPlayerSounds(CCSPlayerController? player)
    {
        if (player == null || !player.IsValid)
            return;

        StopPlayerSounds(player.GetStateKey());
    }

    public static void StopAllTrackedSounds()
    {
        List<TrackedPlayerSound> sounds;
        lock (TrackedSoundsLock)
        {
            sounds = TrackedPlayerSounds.Values.SelectMany(playerSounds => playerSounds).ToList();
            TrackedPlayerSounds.Clear();
        }

        foreach (var sound in sounds)
            StopSoundEntity(sound.Entity);
    }

    private static bool EmitTracked(CCSPlayerController? player, BaseConfig config, string? soundEventName)
    {
        var sound = NormalizeSingle(soundEventName);
        if (sound == null || player == null || !player.IsValid)
            return false;

        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid)
            return false;

        var soundPoint = Utilities.CreateEntityByName<CBaseEntity>("snd_event_point")
            ?? Utilities.CreateEntityByName<CBaseEntity>("point_soundevent");
        if (soundPoint == null || !soundPoint.IsValid)
            return Emit(pawn, config, sound);

        try
        {
            if (pawn.AbsOrigin != null)
                soundPoint.Teleport(pawn.AbsOrigin, null, null);

            soundPoint.DispatchSpawn();
            soundPoint.AcceptInput("SetSoundEventName", value: sound);
            soundPoint.AcceptInput("SetSourceEntity", pawn, soundPoint, "!activator");
            soundPoint.AcceptInput("SetParent", pawn, soundPoint, "!activator");
            soundPoint.AcceptInput("StartSound");

            var ownerKey = player.GetStateKey();
            TrackPlayerSound(ownerKey, soundPoint);
            ScheduleTrackedSoundMaintenance(ownerKey, player, soundPoint, config);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ZombieMod] Failed to emit tracked sound '{sound}': {ex.Message}");
            StopSoundEntity(soundPoint);
            return Emit(pawn, config, sound);
        }
    }

    private static void TrackPlayerSound(ulong ownerKey, CBaseEntity soundPoint)
    {
        lock (TrackedSoundsLock)
        {
            if (!TrackedPlayerSounds.TryGetValue(ownerKey, out var sounds))
            {
                sounds = [];
                TrackedPlayerSounds[ownerKey] = sounds;
            }

            sounds.Add(new TrackedPlayerSound(soundPoint));
        }
    }

    private static void ScheduleTrackedSoundMaintenance(
        ulong ownerKey,
        CCSPlayerController owner,
        CBaseEntity soundPoint,
        BaseConfig config)
    {
        var maxLifetimeSeconds = Math.Clamp(config.SoundConfig.PlayerSoundMaxDurationSeconds, 0.1f, 30.0f);
        var followIntervalSeconds = Math.Clamp(config.SoundConfig.PlayerSoundFollowIntervalSeconds, 0.05f, 1.0f);
        var expiresAt = DateTime.UtcNow.AddSeconds(maxLifetimeSeconds);

        _ = Task.Run(async () =>
        {
            while (DateTime.UtcNow < expiresAt)
            {
                await Task.Delay(TimeSpan.FromSeconds(followIntervalSeconds));

                Server.NextFrame(() =>
                {
                    if (!soundPoint.IsValid)
                    {
                        RemoveTrackedSound(ownerKey, soundPoint);
                        return;
                    }

                    if (!owner.IsValid || !owner.PawnIsAlive)
                    {
                        StopTrackedSound(ownerKey, soundPoint);
                        return;
                    }

                    var pawn = owner.PlayerPawn.Value;
                    if (pawn?.AbsOrigin != null)
                        soundPoint.Teleport(pawn.AbsOrigin, null, null);
                });
            }

            Server.NextFrame(() => StopTrackedSound(ownerKey, soundPoint));
        });
    }

    private static void StopPlayerSounds(ulong ownerKey)
    {
        List<TrackedPlayerSound>? sounds;
        lock (TrackedSoundsLock)
        {
            if (!TrackedPlayerSounds.Remove(ownerKey, out sounds))
                return;
        }

        foreach (var sound in sounds)
            StopSoundEntity(sound.Entity);
    }

    private static void StopTrackedSound(ulong ownerKey, CBaseEntity soundPoint)
    {
        RemoveTrackedSound(ownerKey, soundPoint);
        StopSoundEntity(soundPoint);
    }

    private static void RemoveTrackedSound(ulong ownerKey, CBaseEntity soundPoint)
    {
        lock (TrackedSoundsLock)
        {
            if (!TrackedPlayerSounds.TryGetValue(ownerKey, out var sounds))
                return;

            if (!soundPoint.IsValid)
            {
                sounds.RemoveAll(sound => !sound.Entity.IsValid);
            }
            else
            {
                var targetHandle = soundPoint.EntityHandle;
                sounds.RemoveAll(sound => !sound.Entity.IsValid || sound.Entity.EntityHandle == targetHandle);
            }

            if (sounds.Count == 0)
                TrackedPlayerSounds.Remove(ownerKey);
        }
    }

    private static void StopSoundEntity(CBaseEntity? soundPoint)
    {
        if (soundPoint == null || !soundPoint.IsValid)
            return;

        try
        {
            soundPoint.AcceptInput("StopSound");
            soundPoint.AcceptInput("Kill");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ZombieMod] Failed to stop tracked sound: {ex.Message}");
        }
    }

    private static IEnumerable<string?> Merge(string? primarySoundEventName, IEnumerable<string>? fallbackSoundEventNames)
    {
        yield return primarySoundEventName;

        if (fallbackSoundEventNames == null)
            yield break;

        foreach (var soundEventName in fallbackSoundEventNames)
            yield return soundEventName;
    }

    private static string[] Normalize(IEnumerable<string?>? soundEventNames)
    {
        if (soundEventNames == null)
            return [];

        return soundEventNames
            .Where(soundEventName => !string.IsNullOrWhiteSpace(soundEventName))
            .Select(soundEventName => soundEventName!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? NormalizeSingle(string? soundEventName)
    {
        return string.IsNullOrWhiteSpace(soundEventName)
            ? null
            : soundEventName.Trim();
    }

    private sealed record TrackedPlayerSound(CBaseEntity Entity);
}
