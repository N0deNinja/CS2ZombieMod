using Microsoft.Data.Sqlite;
using ReclaimCS.Shared.Persistence;
using ZombieModPlugin.Abilities;
using ZombieModPlugin.Progression.Models;

namespace ZombieModPlugin.Progression.Persistence;

public sealed class SqlitePlayerProgressionRepository : IPlayerProgressionRepository
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _databaseLock = new(1, 1);

    public SqlitePlayerProgressionRepository(string databasePath)
    {
        _connectionString = SqliteConnectionFactory.CreateConnectionString(databasePath);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _databaseLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await ExecuteNonQueryAsync(connection, """
                PRAGMA journal_mode=WAL;
                PRAGMA foreign_keys=ON;
                """, cancellationToken);

            await ExecuteNonQueryAsync(connection, """
                CREATE TABLE IF NOT EXISTS zm_players (
                    steam_id INTEGER PRIMARY KEY NOT NULL,
                    player_name TEXT NOT NULL DEFAULT '',
                    global_level INTEGER NOT NULL DEFAULT 1,
                    global_xp INTEGER NOT NULL DEFAULT 0,
                    money INTEGER NOT NULL DEFAULT 0,
                    selected_zombie_class_id TEXT NOT NULL DEFAULT '',
                    selected_human_class_id TEXT NOT NULL DEFAULT '',
                    created_at_utc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    updated_at_utc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE IF NOT EXISTS zm_class_progress (
                    steam_id INTEGER NOT NULL,
                    role TEXT NOT NULL,
                    class_id TEXT NOT NULL,
                    level INTEGER NOT NULL DEFAULT 1,
                    xp INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (steam_id, role, class_id),
                    FOREIGN KEY (steam_id) REFERENCES zm_players(steam_id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS zm_unlocks (
                    steam_id INTEGER NOT NULL,
                    unlock_type TEXT NOT NULL,
                    role TEXT NOT NULL,
                    class_id TEXT NOT NULL,
                    item_id TEXT NOT NULL,
                    unlocked_at_utc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    PRIMARY KEY (steam_id, unlock_type, role, class_id, item_id),
                    FOREIGN KEY (steam_id) REFERENCES zm_players(steam_id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS zm_equipped_abilities (
                    steam_id INTEGER NOT NULL,
                    role TEXT NOT NULL,
                    class_id TEXT NOT NULL,
                    ability_id TEXT NOT NULL,
                    slot INTEGER NOT NULL,
                    PRIMARY KEY (steam_id, role, class_id, slot),
                    FOREIGN KEY (steam_id) REFERENCES zm_players(steam_id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS zm_ability_binds (
                    steam_id INTEGER NOT NULL,
                    slot INTEGER NOT NULL,
                    key_name TEXT NOT NULL,
                    PRIMARY KEY (steam_id, slot),
                    FOREIGN KEY (steam_id) REFERENCES zm_players(steam_id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS zm_stats (
                    steam_id INTEGER NOT NULL,
                    stat_key TEXT NOT NULL,
                    value INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (steam_id, stat_key),
                    FOREIGN KEY (steam_id) REFERENCES zm_players(steam_id) ON DELETE CASCADE
                );
                """, cancellationToken);

            await EnsureColumnAsync(connection, "zm_players", "money", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        }
        finally
        {
            _databaseLock.Release();
        }
    }

    public async Task<PlayerProgressionData?> LoadAsync(ulong steamId, CancellationToken cancellationToken = default)
    {
        await _databaseLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);

            var data = await LoadPlayerAsync(connection, steamId, cancellationToken);
            if (data == null)
                return null;

            await LoadClassProgressionAsync(connection, data, cancellationToken);
            await LoadUnlocksAsync(connection, data, cancellationToken);
            await LoadEquippedAbilitiesAsync(connection, data, cancellationToken);
            await LoadAbilityBindsAsync(connection, data, cancellationToken);
            await LoadStatsAsync(connection, data, cancellationToken);

            return data;
        }
        finally
        {
            _databaseLock.Release();
        }
    }

    public async Task SaveAsync(PlayerProgressionData data, CancellationToken cancellationToken = default)
    {
        await _databaseLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

            await UpsertPlayerAsync(connection, transaction, data, cancellationToken);
            await ReplaceClassProgressionAsync(connection, transaction, data, cancellationToken);
            await ReplaceUnlocksAsync(connection, transaction, data, cancellationToken);
            await ReplaceEquippedAbilitiesAsync(connection, transaction, data, cancellationToken);
            await ReplaceAbilityBindsAsync(connection, transaction, data, cancellationToken);
            await ReplaceStatsAsync(connection, transaction, data, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _databaseLock.Release();
        }
    }

    public async Task DeleteAsync(ulong steamId, CancellationToken cancellationToken = default)
    {
        await _databaseLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM zm_players WHERE steam_id = $steamId;";
            command.Parameters.AddWithValue("$steamId", ToSigned(steamId));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _databaseLock.Release();
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        return await SqliteConnectionFactory.OpenAsync(_connectionString, cancellationToken);
    }

    private static async Task<PlayerProgressionData?> LoadPlayerAsync(
        SqliteConnection connection,
        ulong steamId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT player_name, global_level, global_xp, money, selected_zombie_class_id, selected_human_class_id
            FROM zm_players
            WHERE steam_id = $steamId;
            """;
        command.Parameters.AddWithValue("$steamId", ToSigned(steamId));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new PlayerProgressionData
        {
            SteamId = steamId,
            PlayerName = reader.GetString(0),
            GlobalLevel = Math.Max(1, reader.GetInt32(1)),
            GlobalXp = Math.Max(0, reader.GetInt32(2)),
            Money = Math.Max(0, reader.GetInt32(3)),
            SelectedZombieClassId = reader.GetString(4),
            SelectedHumanClassId = reader.GetString(5)
        };
    }

    private static async Task LoadClassProgressionAsync(
        SqliteConnection connection,
        PlayerProgressionData data,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT role, class_id, level, xp
            FROM zm_class_progress
            WHERE steam_id = $steamId;
            """;
        command.Parameters.AddWithValue("$steamId", ToSigned(data.SteamId));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!Enum.TryParse<ProgressionClassRole>(reader.GetString(0), out var role))
                continue;

            var key = new ProgressionClassKey(role, reader.GetString(1));
            data.ClassProgression[key] = new ClassProgressionData
            {
                Level = Math.Max(1, reader.GetInt32(2)),
                Xp = Math.Max(0, reader.GetInt32(3))
            };
        }
    }

    private static async Task LoadUnlocksAsync(
        SqliteConnection connection,
        PlayerProgressionData data,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT unlock_type, role, class_id, item_id
            FROM zm_unlocks
            WHERE steam_id = $steamId;
            """;
        command.Parameters.AddWithValue("$steamId", ToSigned(data.SteamId));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var unlockType = reader.GetString(0);
            if (!Enum.TryParse<ProgressionClassRole>(reader.GetString(1), out var role))
                continue;

            var classId = reader.GetString(2);
            var itemId = reader.GetString(3);

            if (string.Equals(unlockType, UnlockKind.ZombieClass.ToString(), StringComparison.OrdinalIgnoreCase))
                data.UnlockedZombieClassIds.Add(itemId);
            else if (string.Equals(unlockType, UnlockKind.HumanClass.ToString(), StringComparison.OrdinalIgnoreCase))
                data.UnlockedHumanClassIds.Add(itemId);
            else if (string.Equals(unlockType, UnlockKind.Ability.ToString(), StringComparison.OrdinalIgnoreCase)
                     && Enum.TryParse<AbilityType>(itemId, out var ability))
            {
                var key = new ProgressionClassKey(role, classId);
                if (!data.ClassProgression.TryGetValue(key, out var progression))
                {
                    progression = new ClassProgressionData();
                    data.ClassProgression[key] = progression;
                }

                progression.UnlockedAbilities.Add(ability);
            }
        }
    }

    private static async Task LoadEquippedAbilitiesAsync(
        SqliteConnection connection,
        PlayerProgressionData data,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT role, class_id, ability_id, slot
            FROM zm_equipped_abilities
            WHERE steam_id = $steamId
            ORDER BY role, class_id, slot;
            """;
        command.Parameters.AddWithValue("$steamId", ToSigned(data.SteamId));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!Enum.TryParse<ProgressionClassRole>(reader.GetString(0), out var role)
                || !Enum.TryParse<AbilityType>(reader.GetString(2), out var ability))
            {
                continue;
            }

            data.EquippedAbilities.Add(new EquippedAbilityRecord(
                role,
                reader.GetString(1),
                ability,
                Math.Max(1, reader.GetInt32(3))));
        }
    }

    private static async Task LoadStatsAsync(
        SqliteConnection connection,
        PlayerProgressionData data,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT stat_key, value
            FROM zm_stats
            WHERE steam_id = $steamId;
            """;
        command.Parameters.AddWithValue("$steamId", ToSigned(data.SteamId));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            data.Statistics[reader.GetString(0)] = reader.GetInt64(1);
    }

    private static async Task LoadAbilityBindsAsync(
        SqliteConnection connection,
        PlayerProgressionData data,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT slot, key_name
            FROM zm_ability_binds
            WHERE steam_id = $steamId;
            """;
        command.Parameters.AddWithValue("$steamId", ToSigned(data.SteamId));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var slot = Math.Max(1, reader.GetInt32(0));
            var keyName = reader.GetString(1);
            if (!string.IsNullOrWhiteSpace(keyName))
                data.AbilitySlotBinds[slot] = keyName;
        }
    }

    private static async Task UpsertPlayerAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PlayerProgressionData data,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO zm_players (
                steam_id,
                player_name,
                global_level,
                global_xp,
                money,
                selected_zombie_class_id,
                selected_human_class_id,
                created_at_utc,
                updated_at_utc
            )
            VALUES ($steamId, $playerName, $globalLevel, $globalXp, $money, $zombieClass, $humanClass, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
            ON CONFLICT(steam_id) DO UPDATE SET
                player_name = excluded.player_name,
                global_level = excluded.global_level,
                global_xp = excluded.global_xp,
                money = excluded.money,
                selected_zombie_class_id = excluded.selected_zombie_class_id,
                selected_human_class_id = excluded.selected_human_class_id,
                updated_at_utc = CURRENT_TIMESTAMP;
            """;
        command.Parameters.AddWithValue("$steamId", ToSigned(data.SteamId));
        command.Parameters.AddWithValue("$playerName", data.PlayerName);
        command.Parameters.AddWithValue("$globalLevel", Math.Max(1, data.GlobalLevel));
        command.Parameters.AddWithValue("$globalXp", Math.Max(0, data.GlobalXp));
        command.Parameters.AddWithValue("$money", Math.Max(0, data.Money));
        command.Parameters.AddWithValue("$zombieClass", data.SelectedZombieClassId);
        command.Parameters.AddWithValue("$humanClass", data.SelectedHumanClassId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ReplaceClassProgressionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PlayerProgressionData data,
        CancellationToken cancellationToken)
    {
        await DeleteChildRowsAsync(connection, transaction, "zm_class_progress", data.SteamId, cancellationToken);

        foreach (var (key, progression) in data.ClassProgression)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO zm_class_progress (steam_id, role, class_id, level, xp)
                VALUES ($steamId, $role, $classId, $level, $xp);
                """;
            command.Parameters.AddWithValue("$steamId", ToSigned(data.SteamId));
            command.Parameters.AddWithValue("$role", key.Role.ToString());
            command.Parameters.AddWithValue("$classId", key.ClassId);
            command.Parameters.AddWithValue("$level", Math.Max(1, progression.Level));
            command.Parameters.AddWithValue("$xp", Math.Max(0, progression.Xp));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task ReplaceUnlocksAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PlayerProgressionData data,
        CancellationToken cancellationToken)
    {
        await DeleteChildRowsAsync(connection, transaction, "zm_unlocks", data.SteamId, cancellationToken);

        foreach (var classId in data.UnlockedZombieClassIds)
        {
            await InsertUnlockAsync(connection, transaction, data.SteamId, UnlockKind.ZombieClass, ProgressionClassRole.Zombie, classId, classId, cancellationToken);
        }

        foreach (var classId in data.UnlockedHumanClassIds)
        {
            await InsertUnlockAsync(connection, transaction, data.SteamId, UnlockKind.HumanClass, ProgressionClassRole.Human, classId, classId, cancellationToken);
        }

        foreach (var (key, progression) in data.ClassProgression)
        {
            foreach (var ability in progression.UnlockedAbilities)
            {
                await InsertUnlockAsync(connection, transaction, data.SteamId, UnlockKind.Ability, key.Role, key.ClassId, ability.ToString(), cancellationToken);
            }
        }
    }

    private static async Task ReplaceEquippedAbilitiesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PlayerProgressionData data,
        CancellationToken cancellationToken)
    {
        await DeleteChildRowsAsync(connection, transaction, "zm_equipped_abilities", data.SteamId, cancellationToken);

        foreach (var equipped in data.EquippedAbilities.OrderBy(record => record.Role).ThenBy(record => record.ClassId).ThenBy(record => record.Slot))
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO zm_equipped_abilities (steam_id, role, class_id, ability_id, slot)
                VALUES ($steamId, $role, $classId, $abilityId, $slot);
                """;
            command.Parameters.AddWithValue("$steamId", ToSigned(data.SteamId));
            command.Parameters.AddWithValue("$role", equipped.Role.ToString());
            command.Parameters.AddWithValue("$classId", equipped.ClassId);
            command.Parameters.AddWithValue("$abilityId", equipped.Ability.ToString());
            command.Parameters.AddWithValue("$slot", Math.Max(1, equipped.Slot));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task ReplaceStatsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PlayerProgressionData data,
        CancellationToken cancellationToken)
    {
        await DeleteChildRowsAsync(connection, transaction, "zm_stats", data.SteamId, cancellationToken);

        foreach (var (statKey, value) in data.Statistics)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO zm_stats (steam_id, stat_key, value)
                VALUES ($steamId, $statKey, $value);
                """;
            command.Parameters.AddWithValue("$steamId", ToSigned(data.SteamId));
            command.Parameters.AddWithValue("$statKey", statKey);
            command.Parameters.AddWithValue("$value", value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task ReplaceAbilityBindsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PlayerProgressionData data,
        CancellationToken cancellationToken)
    {
        await DeleteChildRowsAsync(connection, transaction, "zm_ability_binds", data.SteamId, cancellationToken);

        foreach (var (slot, keyName) in data.AbilitySlotBinds.OrderBy(bind => bind.Key))
        {
            if (slot < 1 || string.IsNullOrWhiteSpace(keyName))
                continue;

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO zm_ability_binds (steam_id, slot, key_name)
                VALUES ($steamId, $slot, $keyName);
                """;
            command.Parameters.AddWithValue("$steamId", ToSigned(data.SteamId));
            command.Parameters.AddWithValue("$slot", slot);
            command.Parameters.AddWithValue("$keyName", keyName.Trim());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertUnlockAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ulong steamId,
        UnlockKind kind,
        ProgressionClassRole role,
        string classId,
        string itemId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO zm_unlocks (steam_id, unlock_type, role, class_id, item_id, unlocked_at_utc)
            VALUES ($steamId, $unlockType, $role, $classId, $itemId, CURRENT_TIMESTAMP);
            """;
        command.Parameters.AddWithValue("$steamId", ToSigned(steamId));
        command.Parameters.AddWithValue("$unlockType", kind.ToString());
        command.Parameters.AddWithValue("$role", role.ToString());
        command.Parameters.AddWithValue("$classId", classId);
        command.Parameters.AddWithValue("$itemId", itemId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeleteChildRowsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        ulong steamId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DELETE FROM {tableName} WHERE steam_id = $steamId;";
        command.Parameters.AddWithValue("$steamId", ToSigned(steamId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await SqliteSchema.ExecuteNonQueryAsync(connection, commandText, cancellationToken);
    }

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        await SqliteSchema.AddColumnIfMissingAsync(connection, null, tableName, columnName, columnDefinition, cancellationToken);
    }

    private static long ToSigned(ulong value)
    {
        return SqliteSteamId.ToSigned(value);
    }
}
