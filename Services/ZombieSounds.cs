using CounterStrikeSharp.API.Core;
using ZombieModPlugin.Configs;

namespace ZombieModPlugin.Sounds;

public static class ZombieSounds
{
    public static void Emit(CBaseEntity? entity, BaseConfig config, string? soundEventName)
    {
        if (!config.SoundConfig.Enabled
            || string.IsNullOrWhiteSpace(soundEventName)
            || entity == null
            || !entity.IsValid)
        {
            return;
        }

        try
        {
            entity.EmitSound(soundEventName.Trim());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ZombieMod] Failed to emit sound '{soundEventName}': {ex.Message}");
        }
    }

    public static void EmitWithExtras(
        CBaseEntity? entity,
        BaseConfig config,
        string? primarySoundEventName,
        IEnumerable<string>? extraSoundEventNames)
    {
        Emit(entity, config, primarySoundEventName);
        EmitRandom(entity, config, extraSoundEventNames);
    }

    public static void EmitRandom(CBaseEntity? entity, BaseConfig config, IEnumerable<string>? soundEventNames)
    {
        if (!config.SoundConfig.Enabled || entity == null || !entity.IsValid || soundEventNames == null)
            return;

        var choices = soundEventNames
            .Where(soundEventName => !string.IsNullOrWhiteSpace(soundEventName))
            .Select(soundEventName => soundEventName.Trim())
            .ToArray();

        if (choices.Length == 0)
            return;

        Emit(entity, config, choices[Random.Shared.Next(choices.Length)]);
    }
}
