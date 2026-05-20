using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Events;
using ZombieModPlugin.Configs;
using ZombieModPlugin.Extensions;
using ZombieModPlugin.Humans.Handlers;
using ZombieModPlugin.States;
using ZombieModPlugin.Zombies.Handlers;

namespace ZombieModPlugin.Rounds;

public class ZombieRoundManager
{
    private readonly Dictionary<ulong, PlayerState> _playerStates;
    private readonly BaseConfig _config;
    private readonly ZombieHandler _zombieHandler;
    private readonly HumanHandler _humanHandler;
    private readonly Random _random = new();

    private CancellationTokenSource? _roundCancellation;
    private RoundPhase _phase = RoundPhase.Waiting;
    private DateTime _activeRoundEndsAtUtc;
    private readonly HashSet<ulong> _scatteredThisRound = [];
    private int _lifecycleId;
    private bool _includeBotsForTesting;
    private int _testBotQuota;

    public ZombieRoundManager(
        Dictionary<ulong, PlayerState> playerStates,
        BaseConfig config,
        ZombieHandler zombieHandler,
        HumanHandler humanHandler)
    {
        _playerStates = playerStates;
        _config = config;
        _zombieHandler = zombieHandler;
        _humanHandler = humanHandler;
    }

    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo gameEventInfo)
    {
        StartRoundLifecycle();
        return HookResult.Continue;
    }

    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo gameEventInfo)
    {
        CancelRoundLifecycle();
        _phase = RoundPhase.Ended;
        ResetRoundState();

        Console.WriteLine("[ZombieMod] Round ended. Resetting zombie round state.");
        return HookResult.Continue;
    }

    public void EnsureRoundLifecycleRunning()
    {
        if (_phase == RoundPhase.Testing)
            return;

        if (_roundCancellation != null
            && _phase is RoundPhase.Waiting or RoundPhase.Preparing or RoundPhase.InfectionCountdown or RoundPhase.Active)
            return;

        StartRoundLifecycle();
    }

    public void OnPlayablePlayerConnected(CCSPlayerController player)
    {
        if (!IsPlayablePlayer(player))
            return;

        EnsureRoundLifecycleRunning();

        Server.NextFrame(() =>
        {
            if (_phase != RoundPhase.Waiting || !IsPlayablePlayer(player))
                return;

            ShowWaitingHud(GetConnectedPlayers().Count());
        });
    }

    public void ApplyZombieServerRules()
    {
        var nativeRoundMinutes = GetNativeRoundTimeMinutes();

        if (ShouldIncludeBots())
        {
            Server.ExecuteCommand("bot_quota_mode normal");
            Server.ExecuteCommand($"bot_quota {Math.Clamp(_testBotQuota, 1, 16)}");
        }
        else
        {
            Server.ExecuteCommand("bot_quota 0");
            Server.ExecuteCommand("bot_kick");
        }

        Server.ExecuteCommand("mp_do_warmup_period 0");
        Server.ExecuteCommand("mp_warmuptime 0");
        Server.ExecuteCommand("mp_warmuptime_all_players_connected 0");
        Server.ExecuteCommand("mp_warmup_end");
        Server.ExecuteCommand("mp_freezetime 0");
        Server.ExecuteCommand("mp_round_restart_delay 0");
        Server.ExecuteCommand($"mp_roundtime {nativeRoundMinutes}");
        Server.ExecuteCommand($"mp_roundtime_defuse {nativeRoundMinutes}");
        Server.ExecuteCommand($"mp_roundtime_hostage {nativeRoundMinutes}");
        Server.ExecuteCommand("mp_ignore_round_win_conditions 1");
        Server.ExecuteCommand("mp_timelimit 0");
        Server.ExecuteCommand("mp_teammates_are_enemies 0");
        Server.ExecuteCommand("mp_friendlyfire 0");
        Server.ExecuteCommand("mp_autoteambalance 0");
        Server.ExecuteCommand("mp_limitteams 0");
        Server.ExecuteCommand("mp_buytime 0");
        Server.ExecuteCommand("mp_buy_anywhere 0");
        Server.ExecuteCommand("mp_randomspawn 1");
        Server.ExecuteCommand("mp_randomspawn_los 0");
        Server.ExecuteCommand("bot_stop 0");
        Server.ExecuteCommand("bot_dont_shoot 0");
    }

    public void EnterAdminTestMode()
    {
        CancelRoundLifecycle();
        _phase = RoundPhase.Testing;
        ApplyZombieServerRules();
        Console.WriteLine("[ZombieMod] Admin test mode enabled.");
    }

    public void RestartRoundForTesting()
    {
        StartRoundLifecycle();
    }

    public void SetBotsInRoundForTesting(bool enabled, int botQuota = 0)
    {
        _includeBotsForTesting = enabled;
        _testBotQuota = enabled
            ? Math.Clamp(botQuota > 0 ? botQuota : _testBotQuota > 0 ? _testBotQuota : _config.AdminTestConfig.DefaultBotCount, 1, 16)
            : 0;

        ApplyZombieServerRules();

        if (!enabled)
            Server.ExecuteCommand("bot_kick");
    }

    public string GetDebugStatus()
    {
        var connected = GetConnectedPlayers().Count();
        var alive = GetAlivePlayers().Count();
        var humans = CountAlivePlayers(isZombie: false);
        var zombies = CountAlivePlayers(isZombie: true);
        var bots = Utilities.GetPlayers().Count(player => player is { IsValid: true, IsBot: true });
        var players = GetConnectedPlayers()
            .Select(player =>
            {
                var state = player.GetState(_playerStates);
                var kind = player.IsBot ? "bot" : "player";
                var side = state.IsZombie ? "Z" : "H";
                var team = player.Team.ToString();
                var aliveState = player.PawnIsAlive ? "alive" : "dead";

                return $"{player.PlayerName}({kind},{side},{team},{aliveState})";
            });

        return $"Phase: {_phase} | Players: {connected} alive: {alive} | Humans: {humans} | Zombies: {zombies} | Bots: {bots} | BotsInRound: {ShouldIncludeBots()} | {string.Join("; ", players)}";
    }

    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo gameEventInfo)
    {
        if (_phase != RoundPhase.Active)
            return HookResult.Continue;

        var victim = @event.Userid;
        var attacker = @event.Attacker;

        if (victim == null || attacker == null)
            return HookResult.Continue;

        if (!IsPlayablePlayer(victim) || !IsPlayablePlayer(attacker) || victim == attacker)
            return HookResult.Continue;

        var victimState = victim.GetState(_playerStates);
        var attackerState = attacker.GetState(_playerStates);

        if (!attackerState.IsZombie || victimState.IsZombie)
            return HookResult.Continue;

        InfectPlayer(victim, attacker, isInitialInfection: false);
        AwardZombieXp(attacker, attackerState, _config.ZombieConfig.XPPerKill);
        ShowActiveHud();
        CheckWinConditions();

        return HookResult.Continue;
    }

    public HookResult OnPlayerTakeDamagePre(CCSPlayerPawn victimPawn, CTakeDamageInfo damageInfo)
    {
        if (_phase != RoundPhase.Active)
            return HookResult.Continue;

        var victim = GetControllerFromPawn(victimPawn);
        var attacker = GetControllerFromDamageInfo(damageInfo);

        if (victim == null || attacker == null)
            return HookResult.Continue;

        if (!IsPlayablePlayer(victim) || !IsPlayablePlayer(attacker) || victim == attacker)
            return HookResult.Continue;

        var victimState = victim.GetState(_playerStates);
        var attackerState = attacker.GetState(_playerStates);

        if (!attackerState.IsZombie || victimState.IsZombie)
            return HookResult.Continue;

        var requiredHits = GetRequiredInfectionHits();
        victimState.InfectionHitsTaken = Math.Min(requiredHits, victimState.InfectionHitsTaken + 1);

        ShowInfectionProgress(victim, attacker, victimState.InfectionHitsTaken, requiredHits);

        if (victimState.InfectionHitsTaken >= requiredHits)
        {
            InfectPlayer(victim, attacker, isInitialInfection: false);
            AwardZombieXp(attacker, attackerState, _config.ZombieConfig.XPPerKill);
            ShowActiveHud();
            CheckWinConditions();
        }

        return HookResult.Handled;
    }

    public HookResult OnPlayerSpawned(EventPlayerSpawned @event, GameEventInfo gameEventInfo)
    {
        if (!_config.GeneralConfig.RandomizePlayerSpawns)
            return HookResult.Continue;

        if (_phase is not (RoundPhase.Preparing or RoundPhase.InfectionCountdown))
            return HookResult.Continue;

        var player = @event.Userid;
        if (!IsPlayablePlayer(player) || player == null)
            return HookResult.Continue;

        Server.NextFrame(() => ScatterPlayerToRandomMapSpawn(player));
        return HookResult.Continue;
    }

    private void StartRoundLifecycle()
    {
        CancelRoundLifecycle();
        _roundCancellation = new CancellationTokenSource();
        var token = _roundCancellation.Token;
        var lifecycleId = Interlocked.Increment(ref _lifecycleId);

        _phase = RoundPhase.Waiting;
        ApplyZombieServerRules();
        ResetRoundState();
        _scatteredThisRound.Clear();

        Server.NextFrame(() =>
        {
            if (!IsLifecycleCurrent(lifecycleId, token))
                return;

            ApplyZombieServerRules();
            ResetWaitingPlayerStates();
        });

        Console.WriteLine($"[ZombieMod] Round lifecycle started. Waiting for {_config.GeneralConfig.MinimumPlayersToStart} player(s).");

        Task.Run(async () =>
        {
            try
            {
                await WaitForEnoughPlayers(lifecycleId, token);

                Server.NextFrame(() =>
                {
                    if (!IsLifecycleCurrent(lifecycleId, token))
                        return;

                    _phase = RoundPhase.Preparing;
                    ApplyZombieServerRules();
                    ResetPlayersForRound();
                });

                await Task.Delay(TimeSpan.FromSeconds(_config.GeneralConfig.SpawnScatterDelaySeconds), token);

                Server.NextFrame(() =>
                {
                    if (!IsLifecycleCurrent(lifecycleId, token))
                        return;

                    if (_config.GeneralConfig.RandomizePlayerSpawns)
                        ScatterAlivePlayersAcrossMapSpawns();
                });

                Server.NextFrame(() =>
                {
                    if (IsLifecycleCurrent(lifecycleId, token))
                        _phase = RoundPhase.InfectionCountdown;
                });

                var countdownSeconds = Math.Max(1, (int)Math.Ceiling(_config.GeneralConfig.FirstInfectionDelaySeconds));

                for (var remaining = countdownSeconds; remaining > 0; remaining--)
                {
                    if (!IsLifecycleCurrent(lifecycleId, token))
                        return;

                    var capturedRemaining = remaining;
                    Server.NextFrame(() =>
                    {
                        if (IsLifecycleCurrent(lifecycleId, token) && _phase == RoundPhase.InfectionCountdown)
                            ShowCountdownHud(capturedRemaining);
                    });

                    await Task.Delay(TimeSpan.FromSeconds(1), token);
                }

                Server.NextFrame(() =>
                {
                    if (IsLifecycleCurrent(lifecycleId, token))
                        BeginActiveRound(lifecycleId);
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ZombieMod] ERROR in round lifecycle: {ex}");
            }
        }, token);
    }

    private async Task WaitForEnoughPlayers(int lifecycleId, CancellationToken token)
    {
        while (IsLifecycleCurrent(lifecycleId, token))
        {
            var connectedPlayers = GetConnectedPlayers().Count();
            if (connectedPlayers >= _config.GeneralConfig.MinimumPlayersToStart)
                return;

            Server.NextFrame(() =>
            {
                if (IsLifecycleCurrent(lifecycleId, token) && _phase == RoundPhase.Waiting)
                    ShowWaitingHud(GetConnectedPlayers().Count());
            });

            await Task.Delay(TimeSpan.FromSeconds(_config.GeneralConfig.WaitingHudIntervalSeconds), token);
        }
    }

    private void BeginActiveRound(int lifecycleId)
    {
        if (!IsLifecycleCurrent(lifecycleId, _roundCancellation?.Token ?? CancellationToken.None) || _phase == RoundPhase.Ended)
            return;

        var candidates = GetAlivePlayers().ToList();
        if (candidates.Count < _config.GeneralConfig.MinimumPlayersToStart)
        {
            Console.WriteLine("[ZombieMod] Not enough alive players for initial infection. Returning to waiting phase.");
            StartRoundLifecycle();
            return;
        }

        _phase = RoundPhase.Active;
        _activeRoundEndsAtUtc = DateTime.UtcNow.AddSeconds(_config.GeneralConfig.RoundDurationSeconds);

        var initialZombieCount = CalculateInitialZombieCount(candidates.Count);
        var initialZombies = candidates
            .OrderBy(_ => _random.Next())
            .Take(initialZombieCount)
            .ToList();

        foreach (var zombie in initialZombies)
            InfectPlayer(zombie, infector: null, isInitialInfection: true);

        BroadcastChat($"{_config.ChatConfig.ZombiePrefix} Infection has begun. Survive or spread the infection.");
        ShowActiveHud();
        StartActiveHudLoop(lifecycleId, _roundCancellation?.Token ?? CancellationToken.None);
        CheckWinConditions();
    }

    private void StartActiveHudLoop(int lifecycleId, CancellationToken token)
    {
        Task.Run(async () =>
        {
            try
            {
                while (IsLifecycleCurrent(lifecycleId, token) && _phase == RoundPhase.Active)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_config.GeneralConfig.ActiveHudIntervalSeconds), token);

                    Server.NextFrame(() =>
                    {
                        if (!IsLifecycleCurrent(lifecycleId, token) || _phase != RoundPhase.Active)
                            return;

                        if (DateTime.UtcNow >= _activeRoundEndsAtUtc)
                        {
                            EndRound(humansWon: true, "Humans survived until the timer expired.");
                            return;
                        }

                        ShowActiveHud();
                        CheckWinConditions();
                    });
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private void ResetPlayersForRound()
    {
        foreach (var player in GetConnectedPlayers())
        {
            var state = player.GetState(_playerStates);
            state.IsZombie = false;
            state.SelectedZombieType = null;
            state.InfectionHitsTaken = 0;
            state.GlobalCooldowns.Clear();
            state.ActiveAbilities.Clear();

            _humanHandler.OnBecomeHuman(player, state);
            player.PrintToChat(_config.MessagesConfig.StartingAsHuman);
        }
    }

    private void ResetWaitingPlayerStates()
    {
        foreach (var player in GetConnectedPlayers())
        {
            var state = player.GetState(_playerStates);
            state.IsZombie = false;
            state.SelectedZombieType = null;
            state.InfectionHitsTaken = 0;
            state.GlobalCooldowns.Clear();
            state.ActiveAbilities.Clear();
        }
    }

    private void ResetRoundState()
    {
        foreach (var state in _playerStates.Values)
        {
            state.IsZombie = false;
            state.SelectedZombieType = null;
            state.InfectionHitsTaken = 0;
            state.GlobalCooldowns.Clear();
            state.ActiveAbilities.Clear();
        }

        _scatteredThisRound.Clear();
    }

    private void ScatterAlivePlayersAcrossMapSpawns()
    {
        var players = GetAlivePlayers().ToList();
        var spawns = GetMapSpawnPoints().OrderBy(_ => _random.Next()).ToList();

        if (players.Count == 0 || spawns.Count == 0)
            return;

        for (var i = 0; i < players.Count; i++)
            ScatterPlayerToMapSpawn(players[i], spawns[i % spawns.Count]);

        Console.WriteLine($"[ZombieMod] Scattered {players.Count} player(s) across {spawns.Count} map spawn point(s).");
    }

    private void ScatterPlayerToRandomMapSpawn(CCSPlayerController player)
    {
        var playerKey = player.GetStateKey();
        if (_scatteredThisRound.Contains(playerKey))
            return;

        var spawns = GetMapSpawnPoints().ToList();
        if (spawns.Count == 0)
            return;

        ScatterPlayerToMapSpawn(player, spawns[_random.Next(spawns.Count)]);
    }

    private void ScatterPlayerToMapSpawn(CCSPlayerController player, MapSpawnPoint spawn)
    {
        if (!player.PawnIsAlive)
            return;

        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid)
            return;

        var position = new CounterStrikeSharp.API.Modules.Utils.Vector(
            spawn.Position.X,
            spawn.Position.Y,
            spawn.Position.Z + 16.0f);

        pawn.Teleport(position, spawn.Angles, velocity: null);
        _scatteredThisRound.Add(player.GetStateKey());
    }

    private IEnumerable<MapSpawnPoint> GetMapSpawnPoints()
    {
        foreach (var spawn in Utilities.FindAllEntitiesByDesignerName<CInfoPlayerCounterterrorist>("info_player_counterterrorist"))
        {
            if (spawn.AbsOrigin != null && spawn.AbsRotation != null)
                yield return new MapSpawnPoint(spawn.AbsOrigin, spawn.AbsRotation);
        }

        foreach (var spawn in Utilities.FindAllEntitiesByDesignerName<CInfoPlayerTerrorist>("info_player_terrorist"))
        {
            if (spawn.AbsOrigin != null && spawn.AbsRotation != null)
                yield return new MapSpawnPoint(spawn.AbsOrigin, spawn.AbsRotation);
        }

        foreach (var spawn in Utilities.FindAllEntitiesByDesignerName<CInfoDeathmatchSpawn>("info_deathmatch_spawn"))
        {
            if (spawn.AbsOrigin != null && spawn.AbsRotation != null)
                yield return new MapSpawnPoint(spawn.AbsOrigin, spawn.AbsRotation);
        }
    }

    private void InfectPlayer(CCSPlayerController player, CCSPlayerController? infector, bool isInitialInfection)
    {
        if (!IsPlayablePlayer(player))
            return;

        var state = player.GetState(_playerStates);
        if (state.IsZombie)
            return;

        state.InfectionHitsTaken = 0;
        state.IsZombie = true;
        _zombieHandler.OnBecomeZombie(player, state);

        if (isInitialInfection)
        {
            player.PrintToChat($"{_config.ChatConfig.ZombiePrefix} You are one of the first infected.");
            player.PrintToCenterHtml("<font color='#ff3d3d'>YOU ARE INFECTED</font><br><font color='#ffffff'>Hunt the humans.</font>", 4);
            Console.WriteLine($"[ZombieMod] {player.PlayerName} selected as an initial zombie.");
        }
        else
        {
            player.PrintToChat($"{_config.ChatConfig.ZombiePrefix} You were infected by {infector?.PlayerName ?? "a zombie"}.");
            player.PrintToCenterHtml("<font color='#ff3d3d'>INFECTED</font><br><font color='#ffffff'>You will respawn as a zombie.</font>", 4);
            infector?.PrintToChat($"{_config.ChatConfig.ZombiePrefix} You infected {player.PlayerName}.");
            Console.WriteLine($"[ZombieMod] {player.PlayerName} infected by {infector?.PlayerName ?? "unknown"}.");
        }
    }

    private void AwardZombieXp(CCSPlayerController player, PlayerState state, int xp)
    {
        if (xp <= 0 || state.SelectedZombieType == null)
            return;

        var zombie = state.SelectedZombieType;
        if (!state.ZombieProgression.TryGetValue(zombie.Id, out var progression))
        {
            progression = new ZombieProgression
            {
                Level = _config.ZombieConfig.StartingLevel
            };
            state.ZombieProgression[zombie.Id] = progression;
        }

        progression.XP += xp;
        player.PrintToChat($"{_config.ChatConfig.ZombiePrefix} +{xp} XP for {zombie.Name}.");

        while (progression.Level < _config.ZombieConfig.MaxLevel)
        {
            var requiredXp = GetRequiredXpForNextLevel(progression.Level);
            if (progression.XP < requiredXp)
                break;

            progression.XP -= requiredXp;
            progression.Level++;
            player.PrintToChat(string.Format(_config.MessagesConfig.LevelUp, progression.Level));
        }
    }

    private int GetRequiredXpForNextLevel(int currentLevel)
    {
        return Math.Max(1, _config.ZombieConfig.XPPerLevel * Math.Max(1, currentLevel));
    }

    private int GetRequiredInfectionHits()
    {
        return Math.Max(1, _config.ZombieConfig.InfectionHitsRequired);
    }

    private string GetNativeRoundTimeMinutes()
    {
        var seconds = Math.Max(
            60.0,
            _config.GeneralConfig.RoundDurationSeconds
            + Math.Max(0.0f, _config.GeneralConfig.FirstInfectionDelaySeconds)
            + Math.Max(0.0f, _config.GeneralConfig.SpawnScatterDelaySeconds));

        var minutes = Math.Clamp(seconds / 60.0, 1.0, 60.0);
        return minutes.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private void ShowInfectionProgress(CCSPlayerController victim, CCSPlayerController attacker, int hits, int requiredHits)
    {
        var message = $"Infection: {hits}/{requiredHits}";
        victim.PrintToCenter(message);
        attacker.PrintToCenter($"{victim.PlayerName} {message}");
    }

    private void CheckWinConditions()
    {
        if (_phase != RoundPhase.Active)
            return;

        var aliveHumans = CountAlivePlayers(isZombie: false);
        var aliveZombies = CountAlivePlayers(isZombie: true);

        if (aliveHumans == 0 && aliveZombies > 0)
        {
            EndRound(humansWon: false, "All humans have been infected.");
        }
        else if (aliveZombies == 0 && aliveHumans > 0)
        {
            EndRound(humansWon: true, "All zombies have been eliminated.");
        }
    }

    private void EndRound(bool humansWon, string reason)
    {
        if (_phase == RoundPhase.Ended)
            return;

        _phase = RoundPhase.Ended;
        CancelRoundLifecycle();
        var restartLifecycleId = Volatile.Read(ref _lifecycleId);

        var prefix = humansWon ? _config.ChatConfig.HumanPrefix : _config.ChatConfig.ZombiePrefix;
        var message = humansWon
            ? $"{prefix} Humans win. {reason}"
            : $"{prefix} Zombies win. {reason}";

        BroadcastChat(message);
        BroadcastCenterHtml(humansWon
            ? "<font color='#7fd7ff'>HUMANS WIN</font>"
            : "<font color='#ff3d3d'>ZOMBIES WIN</font>", 5);

        Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(_config.GeneralConfig.PostRoundDelaySeconds));
            Server.NextFrame(() =>
            {
                if (Volatile.Read(ref _lifecycleId) == restartLifecycleId && _phase == RoundPhase.Ended)
                    StartRoundLifecycle();
            });
        });
    }

    private void ShowWaitingHud(int connectedPlayers)
    {
        var needed = Math.Max(0, _config.GeneralConfig.MinimumPlayersToStart - connectedPlayers);
        var message =
            "Zombie Mod waiting\n" +
            $"Players: {connectedPlayers}/{_config.GeneralConfig.MinimumPlayersToStart}\n" +
            $"Need {needed} more player(s).";

        BroadcastCenterText(message);
    }

    private void ShowCountdownHud(int remainingSeconds)
    {
        var humans = CountAlivePlayers(isZombie: false);
        var players = GetAlivePlayers().Count();
        var message =
            $"Infection starts in {remainingSeconds}\n" +
            $"Players: {players} | Humans: {humans}\n" +
            "Spread out.";

        BroadcastCenterText(message);
    }

    private void ShowActiveHud()
    {
        var humans = CountAlivePlayers(isZombie: false);
        var zombies = CountAlivePlayers(isZombie: true);
        var secondsRemaining = Math.Max(0, (int)Math.Ceiling((_activeRoundEndsAtUtc - DateTime.UtcNow).TotalSeconds));
        var minutes = secondsRemaining / 60;
        var seconds = secondsRemaining % 60;

        var message =
            $"Humans: {humans} | Zombies: {zombies}\n" +
            $"Survive: {minutes:00}:{seconds:00}";

        BroadcastCenterText(message);
    }

    private int CalculateInitialZombieCount(int playerCount)
    {
        if (playerCount <= 0)
            return 0;

        var ratioCount = (int)Math.Ceiling(playerCount * _config.GeneralConfig.InitialZombieRatio);
        var count = Math.Max(_config.GeneralConfig.MinimumInitialZombies, ratioCount);

        if (_config.GeneralConfig.MaximumInitialZombies > 0)
            count = Math.Min(count, _config.GeneralConfig.MaximumInitialZombies);

        return Math.Clamp(count, 1, playerCount);
    }

    private int CountAlivePlayers(bool isZombie)
    {
        return GetAlivePlayers()
            .Count(player => player.GetState(_playerStates).IsZombie == isZombie);
    }

    private IEnumerable<CCSPlayerController> GetConnectedPlayers()
    {
        return Utilities.GetPlayers()
            .Where(IsPlayablePlayer);
    }

    private IEnumerable<CCSPlayerController> GetAlivePlayers()
    {
        return GetConnectedPlayers()
            .Where(player => player.PawnIsAlive);
    }

    private CCSPlayerController? GetControllerFromDamageInfo(CTakeDamageInfo damageInfo)
    {
        return GetControllerFromEntity(damageInfo.Attacker.Value)
            ?? GetControllerFromEntity(damageInfo.Inflictor.Value);
    }

    private CCSPlayerController? GetControllerFromEntity(CBaseEntity? entity)
    {
        if (entity == null || !entity.IsValid || !entity.IsPlayerPawn())
            return null;

        return GetControllerFromPawn(entity.As<CCSPlayerPawn>());
    }

    private CCSPlayerController? GetControllerFromPawn(CCSPlayerPawn? pawn)
    {
        if (pawn == null || !pawn.IsValid)
            return null;

        var controller = pawn.Controller.Value;
        if (controller == null || !controller.IsValid)
            return null;

        return controller.As<CCSPlayerController>();
    }

    private bool IsPlayablePlayer(CCSPlayerController? player)
    {
        if (player == null || !player.IsValid)
            return false;

        if (player.IsBot)
            return ShouldIncludeBots();

        return player.Connected == PlayerConnectedState.Connected;
    }

    private bool ShouldIncludeBots()
    {
        return _includeBotsForTesting || _config.GeneralConfig.IncludeBotsInRound;
    }

    private void BroadcastChat(string message)
    {
        foreach (var player in GetConnectedPlayers())
            player.PrintToChat(message);
    }

    private void BroadcastCenterHtml(string message, int durationSeconds)
    {
        foreach (var player in GetConnectedPlayers())
            player.PrintToCenterHtml(message, durationSeconds);
    }

    private void BroadcastCenterText(string message)
    {
        foreach (var player in GetConnectedPlayers())
            player.PrintToCenter(message);
    }

    private void BroadcastCenterAlert(string message)
    {
        foreach (var player in GetConnectedPlayers())
            player.PrintToCenterAlert(message);
    }

    private void CancelRoundLifecycle()
    {
        Interlocked.Increment(ref _lifecycleId);

        if (_roundCancellation == null)
            return;

        _roundCancellation.Cancel();
        _roundCancellation.Dispose();
        _roundCancellation = null;
    }

    private bool IsLifecycleCurrent(int lifecycleId, CancellationToken token)
    {
        return !token.IsCancellationRequested && Volatile.Read(ref _lifecycleId) == lifecycleId;
    }

    private readonly record struct MapSpawnPoint(CounterStrikeSharp.API.Modules.Utils.Vector Position, CounterStrikeSharp.API.Modules.Utils.QAngle Angles);
}
