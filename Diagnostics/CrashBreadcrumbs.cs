using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace ZombieModPlugin.Diagnostics;

internal static class CrashBreadcrumbs
{
    private static readonly object Gate = new();
    private static string? _logPath;

    public static void Configure(string modulePath)
    {
        var moduleDirectory = Path.GetDirectoryName(modulePath);
        if (string.IsNullOrWhiteSpace(moduleDirectory))
            moduleDirectory = AppContext.BaseDirectory;

        var dataDirectory = Path.Combine(moduleDirectory, "data");
        Directory.CreateDirectory(dataDirectory);
        _logPath = Path.Combine(dataDirectory, "zombiemod-crash-breadcrumbs.log");

        Log($"configured path={_logPath}");
    }

    public static void SessionStart(string version, bool hotReload)
    {
        Log($"session start version={version} hotReload={hotReload}");
    }

    public static void Log(string message)
    {
        var line = $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}";

        try
        {
            var path = _logPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                Console.WriteLine($"[ZombieModBreadcrumb] {message}");
                return;
            }

            lock (Gate)
                File.AppendAllText(path, line);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ZombieModBreadcrumb] failed to write breadcrumb: {ex}");
            Console.WriteLine($"[ZombieModBreadcrumb] original message: {message}");
        }
    }

    public static void LogException(string label, Exception ex)
    {
        Log($"{label} exception={ex}");
    }

    public static string DescribePlayer(CCSPlayerController? player)
    {
        if (player == null)
            return "player=null";

        try
        {
            var name = SafeText(player.PlayerName);
            return $"player slot={player.Slot} steam={player.SteamID} bot={player.IsBot} valid={player.IsValid} connected={player.Connected} team={player.Team} alive={SafeBool(() => player.PawnIsAlive)} name=\"{name}\"";
        }
        catch (Exception ex)
        {
            return $"player describe failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public static void SafeNextFrame(string label, Action action)
    {
        Server.NextFrame(() =>
        {
            Log($"{label} next-frame start");
            try
            {
                action();
                Log($"{label} next-frame end");
            }
            catch (Exception ex)
            {
                LogException($"{label} next-frame", ex);
            }
        });
    }

    private static string SafeText(string value)
    {
        return value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('"', '\'');
    }

    private static string SafeBool(Func<bool> read)
    {
        try
        {
            return read().ToString();
        }
        catch (Exception ex)
        {
            return $"failed:{ex.GetType().Name}";
        }
    }
}
