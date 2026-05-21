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
}
