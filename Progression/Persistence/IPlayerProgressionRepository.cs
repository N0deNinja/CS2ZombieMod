using ZombieModPlugin.Progression.Models;

namespace ZombieModPlugin.Progression.Persistence;

public interface IPlayerProgressionRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<PlayerProgressionData?> LoadAsync(ulong steamId, CancellationToken cancellationToken = default);
    Task SaveAsync(PlayerProgressionData data, CancellationToken cancellationToken = default);
    Task DeleteAsync(ulong steamId, CancellationToken cancellationToken = default);
}
