using System.Globalization;
using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Utils;
using ZombieModPlugin.Abilities;
using ZombieModPlugin.Blockades;
using ZombieModPlugin.Configs;
using ZombieModPlugin.Extensions;
using ZombieModPlugin.Formatting;
using ZombieModPlugin.Humans.Handlers;
using ZombieModPlugin.Progression.Models;
using ZombieModPlugin.Progression.Services;
using ZombieModPlugin.Sounds;
using ZombieModPlugin.States;
using ZombieModPlugin.Zombies.Handlers;
using ZombieModPlugin.Zombies.Services;

namespace ZombieModPlugin.Rounds;

public class ZombieRoundManager
{
    private const uint NoDrawEffect = (uint)EntityEffects_t.EF_NODRAW;
    private const uint NoDrawButTransmitEffect = (uint)EntityEffects_t.EF_NODRAW_BUT_TRANSMIT;

    private readonly Dictionary<ulong, PlayerState> _playerStates;
    private readonly BaseConfig _config;
    private readonly ZombieHandler _zombieHandler;
    private readonly HumanHandler _humanHandler;
    private readonly ZombieMeleeVisualService _zombieMeleeVisualService;
    private readonly ProgressionService _progressionService;
    private readonly BlockadeService _blockadeService;
    private readonly Random _random = new();

    private CancellationTokenSource? _roundCancellation;
    private RoundPhase _phase = RoundPhase.Waiting;
    private DateTime _activeRoundEndsAtUtc;
    private readonly HashSet<ulong> _scatteredThisRound = [];
    private int _lifecycleId;
    private bool _includeBotsForTesting;
    private int _testBotQuota;
    private int _currentWorkshopMapIndex = -1;
    private int _completedRoundsOnCurrentWorkshopMap;
    private DateTime _nextZombieIdleSoundAtUtc = DateTime.MinValue;
    private CBaseEntity? _infectionCountdownWorldSound;

    public ZombieRoundManager(
        Dictionary<ulong, PlayerState> playerStates,
        BaseConfig config,
        ZombieHandler zombieHandler,
        HumanHandler humanHandler,
        ZombieMeleeVisualService zombieMeleeVisualService,
        ProgressionService progressionService,
        BlockadeService blockadeService)
    {
        _playerStates = playerStates;
        _config = config;
        _zombieHandler = zombieHandler;
        _humanHandler = humanHandler;
        _zombieMeleeVisualService = zombieMeleeVisualService;
        _progressionService = progressionService;
        _blockadeService = blockadeService;
    }

    public bool IsBlockadePlacementAllowed => _phase is RoundPhase.Active or RoundPhase.Testing;

    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo gameEventInfo)
    {
        StartRoundLifecycle();
        return HookResult.Continue;
    }

    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo gameEventInfo)
    {
        CancelRoundLifecycle();
        ZombieSounds.StopAllTrackedSounds();
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
        var zombieMeleeWeaponName = ZombieMeleeVisualService.ResolveZombieMeleeWeaponName(_config);
        var airAccelerate = Math.Max(0.0f, _config.GeneralConfig.AirAccelerate)
            .ToString(CultureInfo.InvariantCulture);
        var buyAnywhere = _config.HumanConfig.BuyAnywhereAnytime;
        var buyTimeMinutes = buyAnywhere
            ? Math.Max(1, _config.HumanConfig.BuyTimeMinutes)
            : 0;
        var maxMoney = _progressionService.GetNativeMoneyDisplayCap();
        var startMoney = _progressionService.GetNativeMoneyForBalance(_config.HumanConfig.StartingMoney);

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
        Server.ExecuteCommand("sv_cheats 1");
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
        Server.ExecuteCommand("mp_solid_teammates 0");
        Server.ExecuteCommand($"mp_buytime {buyTimeMinutes}");
        Server.ExecuteCommand($"mp_buy_anywhere {(buyAnywhere ? 1 : 0)}");
        Server.ExecuteCommand($"mp_maxmoney {maxMoney}");
        Server.ExecuteCommand($"mp_startmoney {startMoney}");
        Server.ExecuteCommand("mp_afterroundmoney 0");
        Server.ExecuteCommand("mp_playercashawards 0");
        Server.ExecuteCommand("mp_teamcashawards 0");
        Server.ExecuteCommand("mp_give_player_c4 0");
        Server.ExecuteCommand("mp_t_default_primary \"\"");
        Server.ExecuteCommand("mp_t_default_secondary \"\"");
        Server.ExecuteCommand($"mp_t_default_melee {zombieMeleeWeaponName}");
        Server.ExecuteCommand("mp_ct_default_primary \"\"");
        Server.ExecuteCommand("mp_ct_default_secondary weapon_usp_silencer");
        Server.ExecuteCommand("mp_ct_default_melee weapon_knife");
        Server.ExecuteCommand("mp_death_drop_gun 0");
        Server.ExecuteCommand("mp_death_drop_grenade 0");
        Server.ExecuteCommand("mp_death_drop_defuser 0");
        Server.ExecuteCommand("mp_randomspawn 1");
        Server.ExecuteCommand("mp_randomspawn_los 0");
        Server.ExecuteCommand("bot_stop 0");
        Server.ExecuteCommand("bot_dont_shoot 0");
        Server.ExecuteCommand($"sv_airaccelerate {airAccelerate}");
    }

    public void OnMapStarted(string mapName)
    {
        _blockadeService.ClearAll();

        var mapNames = GetConfiguredWorkshopMapNames();
        var currentIndex = Array.FindIndex(
            mapNames,
            name => string.Equals(name, mapName, StringComparison.OrdinalIgnoreCase));

        if (currentIndex >= 0)
            _currentWorkshopMapIndex = currentIndex;

        _completedRoundsOnCurrentWorkshopMap = 0;
    }

    public void EnterAdminTestMode()
    {
        CancelRoundLifecycle();
        _blockadeService.ClearAll();
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
                var pawnTeam = player.PlayerPawn.Value is { IsValid: true } pawn
                    ? ((CsTeam)pawn.TeamNum).ToString()
                    : "no-pawn";
                var aliveState = player.PawnIsAlive ? "alive" : "dead";

                return $"{player.PlayerName}({kind},{side},ctrl:{team},pawn:{pawnTeam},{aliveState})";
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

        ZombieSounds.StopPlayerSounds(victim);

        if (!IsPlayablePlayer(victim) || !IsPlayablePlayer(attacker) || victim == attacker)
            return HookResult.Continue;

        var victimState = victim.GetState(_playerStates);
        var attackerState = attacker.GetState(_playerStates);

        if (victimState.IsZombie)
        {
            if (!attackerState.IsZombie)
            {
                _progressionService.AwardReward(attacker, attackerState, ProgressionRewardType.ZombieKill, "zombie_kills");
                _progressionService.AwardHumanMoney(attacker, attackerState, _config.HumanConfig.MoneyPerKill, "zombie kill");
            }

            EmitPlayerSoundUntracked(victim, _config.SoundConfig.ZombieDeathSound, _config.SoundConfig.ExtraZombieDeathSounds);
            ShowActiveHud();
            CheckWinConditions();
            return HookResult.Continue;
        }

        if (!attackerState.IsZombie || victimState.IsZombie)
            return HookResult.Continue;

        InfectPlayer(victim, attacker, isInitialInfection: false);
        _progressionService.AwardReward(attacker, attackerState, ProgressionRewardType.HumanKill, "infections");
        _progressionService.AwardMoney(attacker, attackerState, _config.HumanConfig.MoneyPerInfection, "infecting a human");
        ShowActiveHud();
        CheckWinConditions();

        return HookResult.Continue;
    }

    public HookResult OnPlayerTakeDamagePre(CCSPlayerPawn victimPawn, CTakeDamageInfo damageInfo)
    {
        if (IsFallDamage(damageInfo))
            return HookResult.Handled;

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

        if (AreSameFaction(victim, attacker, victimState, attackerState))
            return HookResult.Handled;

        if (!attackerState.IsZombie && victimState.IsZombie)
        {
            ApplyZombieKnockback(victimPawn, attacker, attackerState);
            TryEmitZombiePain(victim, victimState);
            return HookResult.Continue;
        }

        if (!attackerState.IsZombie || victimState.IsZombie)
            return HookResult.Continue;

        _zombieMeleeVisualService.OnZombieKnifeHit(attacker, attackerState, victim);

        var requiredHits = GetRequiredInfectionHits(victimState);
        victimState.InfectionHitsTaken = Math.Min(requiredHits, victimState.InfectionHitsTaken + 1);
        victimState.InfectionAssistCredits[attacker.GetStateKey()] = DateTime.UtcNow;

        ShowInfectionProgress(victim, attacker, victimState.InfectionHitsTaken, requiredHits);
        EmitPlayerSound(attacker, _config.SoundConfig.InfectionHitSound, _config.SoundConfig.ExtraInfectionHitSounds);

        if (victimState.InfectionHitsTaken >= requiredHits)
        {
            InfectPlayer(victim, attacker, isInitialInfection: false);
            _progressionService.AwardReward(attacker, attackerState, ProgressionRewardType.Infection, "infections");
            _progressionService.AwardMoney(attacker, attackerState, _config.HumanConfig.MoneyPerInfection, "infecting a human");
            ShowActiveHud();
            CheckWinConditions();
        }

        return HookResult.Handled;
    }

    private static bool IsFallDamage(CTakeDamageInfo damageInfo)
    {
        return (damageInfo.BitsDamageType & DamageTypes_t.DMG_FALL) == DamageTypes_t.DMG_FALL;
    }

    public HookResult OnWeaponFire(EventWeaponFire @event, GameEventInfo gameEventInfo)
    {
        var player = @event.Userid;
        if (player == null || !IsPlayablePlayer(player))
            return HookResult.Continue;

        var state = player.GetState(_playerStates);
        _zombieMeleeVisualService.OnZombieKnifeSlash(player, state, @event.Weapon);

        return HookResult.Continue;
    }

    public HookResult OnItemPickup(EventItemPickup @event, GameEventInfo gameEventInfo)
    {
        var player = @event.Userid;
        if (player == null || !IsPlayablePlayer(player))
            return HookResult.Continue;

        var state = player.GetState(_playerStates);
        if (!state.IsZombie)
            return HookResult.Continue;

        if (IsKnifePickup(@event))
            return HookResult.Continue;

        Server.NextFrame(() => EnforcePlayerRole(player));
        return HookResult.Handled;
    }

    public HookResult OnPlayerSpawned(EventPlayerSpawned @event, GameEventInfo gameEventInfo)
    {
        var player = @event.Userid;
        if (!IsPlayablePlayer(player) || player == null)
            return HookResult.Continue;

        ZombieSounds.StopPlayerSounds(player);
        Server.NextFrame(() => EnforcePlayerRole(player));

        if (_config.GeneralConfig.RandomizePlayerSpawns
            && _phase is RoundPhase.Preparing or RoundPhase.InfectionCountdown)
        {
            Server.NextFrame(() => ScatterPlayerToRandomMapSpawn(player));
        }

        return HookResult.Continue;
    }

    public void OnTick()
    {
        var now = DateTime.UtcNow;

        foreach (var player in GetConnectedPlayers())
        {
            if (!player.PawnIsAlive)
                continue;

            var state = player.GetState(_playerStates);
            if (!state.IsZombie && now >= state.NextNativeMoneySyncAtUtc)
            {
                state.NextNativeMoneySyncAtUtc = now.AddSeconds(1);
                _progressionService.SyncNativeMoney(player, state, save: true);
            }

            UpdateWallCling(player, state);
            ResetAirJumpsIfGrounded(player, state);

            if (_phase is RoundPhase.Active or RoundPhase.Testing)
                UpdateLurkerCloak(player, state, now);
        }

        TryEmitZombieIdleSound(now);
        _blockadeService.OnTick();
    }

    public void OnPlayerButtonsChanged(CCSPlayerController player, PlayerButtons pressed, PlayerButtons released)
    {
        if (!IsPlayablePlayer(player) || !player.PawnIsAlive)
            return;

        var state = player.GetState(_playerStates);
        if (state.IsWallClinging && pressed.HasFlag(PlayerButtons.Jump))
        {
            CancelWallCling(player, state, notify: true);
            return;
        }

        _zombieMeleeVisualService.OnZombieAttackButtonsChanged(player, state, pressed);
        _blockadeService.OnPlayerButtonsChanged(player, state, pressed);

        if (!HasClassAbility(state, AbilityType.MultiJump))
            return;

        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid)
            return;

        if (IsOnGround(pawn))
        {
            state.AirJumpsUsed = 0;
            state.AirJumpReady = false;
            return;
        }

        if (released.HasFlag(PlayerButtons.Jump))
        {
            state.AirJumpReady = true;
            return;
        }

        if (!pressed.HasFlag(PlayerButtons.Jump) || !state.AirJumpReady)
            return;

        var config = _config.AbilityConfig.MultiJump;
        var allowedAirJumps = Math.Max(0, state.IsZombie
            ? config.ZombieAdditionalJumps
            : config.HumanAdditionalJumps);

        if (state.AirJumpsUsed >= allowedAirJumps)
            return;

        state.AirJumpsUsed++;
        state.AirJumpReady = false;

        var forward = pawn.EyeAngles.ToForwardVector();
        var currentVelocity = pawn.AbsVelocity;
        var velocity = new Vector(
            currentVelocity.X + forward.X * Math.Clamp(config.ForwardForce, 0.0f, 800.0f),
            currentVelocity.Y + forward.Y * Math.Clamp(config.ForwardForce, 0.0f, 800.0f),
            Math.Clamp(config.UpForce, 120.0f, 1000.0f));

        pawn.Teleport(velocity: velocity);

        if (state.IsZombie)
            EmitPlayerSound(player, _config.SoundConfig.ZombieIdleSound);
    }

    private void UpdateWallCling(CCSPlayerController player, PlayerState state)
    {
        if (!state.IsWallClinging)
            return;

        if (!state.IsZombie || !HasClassAbility(state, AbilityType.WallClimb))
        {
            CancelWallCling(player, state, notify: false);
            return;
        }

        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid)
        {
            CancelWallCling(player, state, notify: false);
            return;
        }

        if (!state.WallClingAnchorX.HasValue || !state.WallClingAnchorY.HasValue || !state.WallClingAnchorZ.HasValue)
        {
            CancelWallCling(player, state, notify: false);
            return;
        }

        var anchor = new Vector(
            state.WallClingAnchorX.Value,
            state.WallClingAnchorY.Value,
            state.WallClingAnchorZ.Value);

        pawn.Teleport(anchor, null, new Vector(0.0f, 0.0f, 0.0f));
    }

    private void CancelWallCling(CCSPlayerController player, PlayerState state, bool notify)
    {
        state.ResetWallClingState();

        if (notify && player.IsValid)
            player.PrintToCenter(_config.AbilityConfig.WallClimb.CancelMessage);
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
        _nextZombieIdleSoundAtUtc = DateTime.MinValue;

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
                        BeginInfectionCountdown();
                });

                var countdownSeconds = Math.Max(1, (int)Math.Ceiling(_config.GeneralConfig.FirstInfectionDelaySeconds));

                for (var remaining = countdownSeconds; remaining > 0; remaining--)
                {
                    if (!IsLifecycleCurrent(lifecycleId, token))
                        return;

                    var capturedRemaining = remaining;
                    Server.NextFrame(() =>
                    {
                        if (!IsLifecycleCurrent(lifecycleId, token) || _phase != RoundPhase.InfectionCountdown)
                            return;

                        if (capturedRemaining == 5)
                            EmitPlayerOnlySoundToAll(_config.SoundConfig.PrepareForInfectionSound);

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
            var connectedPlayers = await CountConnectedPlayersNextFrame(lifecycleId, token);
            if (connectedPlayers >= _config.GeneralConfig.MinimumPlayersToStart)
                return;

            Server.NextFrame(() =>
            {
                if (IsLifecycleCurrent(lifecycleId, token) && _phase == RoundPhase.Waiting)
                    ShowWaitingHud(connectedPlayers);
            });

            await Task.Delay(TimeSpan.FromSeconds(_config.GeneralConfig.WaitingHudIntervalSeconds), token);
        }
    }

    private Task<int> CountConnectedPlayersNextFrame(int lifecycleId, CancellationToken token)
    {
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        Server.NextFrame(() =>
        {
            try
            {
                if (!IsLifecycleCurrent(lifecycleId, token))
                {
                    completion.TrySetCanceled(token);
                    return;
                }

                completion.TrySetResult(GetConnectedPlayers().Count());
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        return completion.Task;
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
        StopInfectionCountdownWorldSound();
        _activeRoundEndsAtUtc = DateTime.UtcNow.AddSeconds(_config.GeneralConfig.RoundDurationSeconds);
        ScheduleNextZombieIdleSound();

        var initialZombieCount = CalculateInitialZombieCount(candidates.Count);
        var initialZombies = candidates
            .OrderBy(_ => _random.Next())
            .Take(initialZombieCount)
            .ToList();

        foreach (var zombie in initialZombies)
            InfectPlayer(zombie, infector: null, isInitialInfection: true);

        EmitPlayerOnlySoundToAll(_config.SoundConfig.FirstInfectionBegunSound);
        BroadcastChat(ChatText.Zombie($"{ChatColors.Gold}Infection has begun.{ChatColors.Default} Survive or spread the infection."));
        ShowActiveHud();
        StartActiveHudLoop(lifecycleId, _roundCancellation?.Token ?? CancellationToken.None);
        CheckWinConditions();
    }

    private void BeginInfectionCountdown()
    {
        _phase = RoundPhase.InfectionCountdown;
        EmitPlayerOnlySoundToAll(_config.SoundConfig.InfectionCountdownStartSound);
        StartInfectionCountdownWorldSound();
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
            state.SelectedHumanClass = null;
            state.ResetRoleRuntimeState();
            state.GlobalCooldowns.Clear();
            state.ActiveAbilities.Clear();

            _humanHandler.OnBecomeHuman(player, state);
            player.PrintToChat(ChatText.Human(_config.MessagesConfig.StartingAsHuman));
        }
    }

    private void ResetWaitingPlayerStates()
    {
        foreach (var player in GetConnectedPlayers())
        {
            var state = player.GetState(_playerStates);
            state.IsZombie = false;
            state.SelectedZombieType = null;
            state.SelectedHumanClass = null;
            state.ResetRoleRuntimeState();
            state.GlobalCooldowns.Clear();
            state.ActiveAbilities.Clear();
        }
    }

    private void ResetRoundState()
    {
        ZombieSounds.StopAllTrackedSounds();
        StopInfectionCountdownWorldSound();

        foreach (var state in _playerStates.Values)
        {
            state.IsZombie = false;
            state.SelectedZombieType = null;
            state.SelectedHumanClass = null;
            state.ResetRoleRuntimeState();
            state.GlobalCooldowns.Clear();
            state.ActiveAbilities.Clear();
        }

        _scatteredThisRound.Clear();
        _blockadeService.ClearAll();
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

        if (infector != null)
            AwardInfectionAssists(player, infector);

        state.InfectionHitsTaken = 0;
        state.IsZombie = true;
        _zombieHandler.OnBecomeZombie(player, state);
        ResetBotAiAfterTeamChange(player, infector);
        EmitPlayerSound(
            player,
            isInitialInfection ? _config.SoundConfig.FirstInfectionSound : _config.SoundConfig.InfectionSound,
            isInitialInfection ? _config.SoundConfig.ExtraFirstInfectionSounds : _config.SoundConfig.ExtraInfectionSounds);

        if (isInitialInfection)
        {
            player.PrintToChat(ChatText.Zombie($"{ChatColors.Gold}You are one of the first infected.{ChatColors.Default}"));
            player.PrintToCenterHtml("<font color='#ff3d3d'>YOU ARE INFECTED</font><br><font color='#ffffff'>Hunt the humans.</font>", 4);
            Console.WriteLine($"[ZombieMod] {player.PlayerName} selected as an initial zombie.");
        }
        else
        {
            player.PrintToChat(ChatText.Zombie($"You were infected by {ChatText.Name(infector?.PlayerName ?? "a zombie")}."));
            player.PrintToCenterHtml("<font color='#ff3d3d'>INFECTED</font><br><font color='#ffffff'>You will respawn as a zombie.</font>", 4);
            infector?.PrintToChat(ChatText.Zombie($"You infected {ChatText.Name(player.PlayerName)}."));
            Console.WriteLine($"[ZombieMod] {player.PlayerName} infected by {infector?.PlayerName ?? "unknown"}.");
        }
    }

    private void AwardInfectionAssists(CCSPlayerController victim, CCSPlayerController infector)
    {
        var victimState = victim.GetState(_playerStates);
        var now = DateTime.UtcNow;
        var infectorKey = infector.GetStateKey();
        var assistKeys = victimState.InfectionAssistCredits
            .Where(entry => entry.Key != infectorKey && (now - entry.Value).TotalSeconds <= 12)
            .Select(entry => entry.Key)
            .Distinct()
            .ToList();

        victimState.InfectionAssistCredits.Clear();

        if (assistKeys.Count == 0)
            return;

        foreach (var assister in GetConnectedPlayers())
        {
            if (!assistKeys.Contains(assister.GetStateKey()))
                continue;

            var assisterState = assister.GetState(_playerStates);
            if (!assisterState.IsZombie)
                continue;

            _progressionService.AwardReward(assister, assisterState, ProgressionRewardType.Assist, "assists");
        }
    }

    private int GetRequiredInfectionHits(PlayerState victimState)
    {
        var classOverride = victimState.SelectedHumanClass?.InfectionHitsRequired;
        if (classOverride.HasValue)
            return Math.Max(1, classOverride.Value);

        return Math.Max(1, _config.HumanConfig.InfectionHitsRequired > 0
            ? _config.HumanConfig.InfectionHitsRequired
            : _config.ZombieConfig.InfectionHitsRequired);
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

    private void EnforcePlayerRole(CCSPlayerController player)
    {
        if (!IsPlayablePlayer(player))
            return;

        var state = player.GetState(_playerStates);
        if (state.IsZombie)
            _zombieHandler.EnforceZombieEquipment(player, state);
        else
            _humanHandler.EnforceHumanAppearance(player, state);
    }

    private void ResetAirJumpsIfGrounded(CCSPlayerController player, PlayerState state)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid)
            return;

        if (IsOnGround(pawn))
        {
            state.AirJumpsUsed = 0;
            state.AirJumpReady = false;
        }
        else if (!player.Buttons.HasFlag(PlayerButtons.Jump))
        {
            state.AirJumpReady = true;
        }
    }

    private void UpdateLurkerCloak(CCSPlayerController player, PlayerState state, DateTime now)
    {
        if (!state.IsZombie || !HasClassAbility(state, AbilityType.LurkerCloak))
        {
            RevealLurkerNow(player, state);
            return;
        }

        var config = _config.AbilityConfig.LurkerCloak;
        if ((now - state.LastLurkerCloakCheckUtc).TotalSeconds < Math.Max(0.01f, config.TickIntervalSeconds))
            return;

        state.LastLurkerCloakCheckUtc = now;

        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null)
            return;

        var origin = pawn.AbsOrigin;
        if (!state.LastCloakX.HasValue || !state.LastCloakY.HasValue || !state.LastCloakZ.HasValue)
        {
            state.LastCloakX = origin.X;
            state.LastCloakY = origin.Y;
            state.LastCloakZ = origin.Z;
            state.LurkerStationarySinceUtc = now;
            FadeLurkerTowardVisible(player, state, pawn, config);
            return;
        }

        var dx = origin.X - state.LastCloakX.Value;
        var dy = origin.Y - state.LastCloakY.Value;
        var dz = origin.Z - state.LastCloakZ.Value;
        var movedDistance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        var speed2d = MathF.Sqrt(pawn.AbsVelocity.X * pawn.AbsVelocity.X + pawn.AbsVelocity.Y * pawn.AbsVelocity.Y);
        var movementThreshold = Math.Max(0.1f, config.MovementThreshold);

        state.LastCloakX = origin.X;
        state.LastCloakY = origin.Y;
        state.LastCloakZ = origin.Z;

        if (movedDistance > movementThreshold || speed2d > movementThreshold)
        {
            state.LurkerStationarySinceUtc = now;
            FadeLurkerTowardVisible(player, state, pawn, config);
            return;
        }

        state.LurkerStationarySinceUtc ??= now;
        var cloakReadyAt = state.LurkerStationarySinceUtc.Value.AddSeconds(Math.Max(0.0f, config.StationaryDelaySeconds));
        if (now < cloakReadyAt)
        {
            FadeLurkerTowardVisible(player, state, pawn, config);
            return;
        }

        var fadeInSeconds = Math.Max(0.0f, config.FadeInSeconds);
        var fadeProgress = fadeInSeconds <= 0.0f
            ? 1.0f
            : (float)Math.Clamp((now - cloakReadyAt).TotalSeconds / fadeInSeconds, 0.0, 1.0);
        var targetAlpha = Math.Clamp(config.Alpha, 0, 255);
        var alpha = (int)MathF.Round(255 - ((255 - targetAlpha) * fadeProgress));

        ApplyLurkerAlpha(player, state, pawn, alpha, config);
    }

    private static void FadeLurkerTowardVisible(
        CCSPlayerController player,
        PlayerState state,
        CCSPlayerPawn pawn,
        LurkerCloakAbilityConfig config)
    {
        if (state.LurkerCurrentAlpha >= 255)
            return;

        var fadeOutSeconds = Math.Max(0.0f, config.FadeOutSeconds);
        var minAlpha = Math.Clamp(config.Alpha, 0, 255);
        var step = fadeOutSeconds <= 0.0f
            ? 255
            : Math.Max(1, (int)MathF.Ceiling((255 - minAlpha) * Math.Max(0.01f, config.TickIntervalSeconds) / fadeOutSeconds));

        ApplyLurkerAlpha(player, state, pawn, Math.Min(255, state.LurkerCurrentAlpha + step), config);
    }

    private static void RevealLurkerNow(CCSPlayerController player, PlayerState state)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn != null && pawn.IsValid)
        {
            ApplyRenderAlpha(pawn, 255);
            ApplyWeaponAlpha(player, pawn, 255);
            TryApplyOwnedModelAlpha(player, pawn, 255, includeViewModels: true);
        }

        state.ResetLurkerCloakTracking();
    }

    private static void ApplyLurkerAlpha(
        CCSPlayerController player,
        PlayerState state,
        CCSPlayerPawn pawn,
        int alpha,
        LurkerCloakAbilityConfig config)
    {
        alpha = Math.Clamp(alpha, 0, 255);
        if (state.LurkerCurrentAlpha != alpha)
            ApplyRenderAlpha(pawn, alpha);

        if (config.ApplyToWeapons)
            ApplyWeaponAlpha(player, pawn, alpha);

        if (config.ApplyToViewModel)
            TryApplyOwnedModelAlpha(player, pawn, alpha, includeViewModels: true);

        state.LurkerCurrentAlpha = alpha;
        state.IsLurkerCloaked = alpha < 255;
    }

    private static void ApplyWeaponAlpha(CCSPlayerController player, CCSPlayerPawn pawn, int alpha)
    {
        var weaponServices = pawn.WeaponServices;
        if (weaponServices == null)
            return;

        var worldWeaponAlpha = alpha < 255 ? 0 : 255;

        ApplyWeaponHandleAlpha(weaponServices.ActiveWeapon, worldWeaponAlpha);
        foreach (var weaponHandle in weaponServices.MyWeapons)
            ApplyWeaponHandleAlpha(weaponHandle, worldWeaponAlpha);

        TryApplyOwnedModelAlpha(player, pawn, worldWeaponAlpha, includeViewModels: false);
    }

    private static void ApplyWeaponHandleAlpha(CHandle<CBasePlayerWeapon> weaponHandle, int alpha)
    {
        var weapon = weaponHandle.Value;
        if (weapon == null || !weapon.IsValid)
            return;

        ApplyWorldWeaponRenderAlpha(weapon, alpha);
    }

    private static void TryApplyOwnedModelAlpha(CCSPlayerController player, CCSPlayerPawn pawn, int alpha, bool includeViewModels)
    {
        if (includeViewModels)
        {
            foreach (var designerName in new[] { "predicted_viewmodel", "viewmodel" })
            {
                foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CBaseModelEntity>(designerName))
                {
                    if (entity == null || !entity.IsValid)
                        continue;

                    if (!IsOwnedByPlayerOrEquipment(entity, player, pawn))
                        continue;

                    ApplyEquipmentRenderAlpha(entity, alpha);
                }
            }

            return;
        }

        foreach (var entity in Utilities.GetAllEntities())
        {
            if (!entity.IsValid || !ShouldFadeOwnedEquipmentEntity(entity.DesignerName, includeViewModels))
                continue;

            CBaseModelEntity modelEntity;
            try
            {
                modelEntity = entity.As<CBaseModelEntity>();
            }
            catch
            {
                continue;
            }

            if (modelEntity == null || !modelEntity.IsValid)
                continue;

            if (!IsOwnedByPlayerOrEquipment(modelEntity, player, pawn))
                continue;

            ApplyWorldWeaponRenderAlpha(modelEntity, alpha);
        }
    }

    private static bool ShouldFadeOwnedEquipmentEntity(string designerName, bool includeViewModels)
    {
        if (string.IsNullOrWhiteSpace(designerName))
            return false;

        return (includeViewModels && designerName.Contains("viewmodel", StringComparison.OrdinalIgnoreCase))
            || designerName.Contains("weapon", StringComparison.OrdinalIgnoreCase)
            || designerName.Contains("knife", StringComparison.OrdinalIgnoreCase)
            || designerName.Contains("c4", StringComparison.OrdinalIgnoreCase)
            || designerName.Contains("bomb", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOwnedByPlayerOrEquipment(CBaseModelEntity entity, CCSPlayerController player, CCSPlayerPawn pawn)
    {
        var owner = entity.OwnerEntity.Value;
        if (owner != null && owner.IsValid && IsPlayerOrOwnedWeapon(owner, player, pawn))
            return true;

        var effectEntity = entity.EffectEntity.Value;
        return effectEntity != null && effectEntity.IsValid && IsPlayerOrOwnedWeapon(effectEntity, player, pawn);
    }

    private static bool IsPlayerOrOwnedWeapon(CBaseEntity entity, CCSPlayerController player, CCSPlayerPawn pawn)
    {
        if (entity.EntityHandle == pawn.EntityHandle || entity.EntityHandle == player.EntityHandle)
            return true;

        var weaponServices = pawn.WeaponServices;
        if (weaponServices == null)
            return false;

        var activeWeapon = weaponServices.ActiveWeapon.Value;
        if (activeWeapon != null && activeWeapon.IsValid && entity.EntityHandle == activeWeapon.EntityHandle)
            return true;

        foreach (var weaponHandle in weaponServices.MyWeapons)
        {
            var weapon = weaponHandle.Value;
            if (weapon != null && weapon.IsValid && entity.EntityHandle == weapon.EntityHandle)
                return true;
        }

        return false;
    }

    private static void ApplyRenderAlpha(CBaseModelEntity entity, int alpha)
    {
        alpha = Math.Clamp(alpha, 0, 255);
        entity.RenderMode = alpha < 255 ? RenderMode_t.kRenderTransAlpha : RenderMode_t.kRenderNormal;
        entity.RenderFX = RenderFx_t.kRenderFxNone;
        entity.Render = Color.FromArgb(alpha, 255, 255, 255);
        entity.MarkRenderStateChanged();
    }

    private static void ApplyWorldWeaponRenderAlpha(CBaseModelEntity entity, int alpha)
    {
        alpha = alpha < 255 ? 0 : 255;
        entity.RenderMode = alpha < 255 ? RenderMode_t.kRenderTransAlpha : RenderMode_t.kRenderNormal;
        entity.RenderFX = RenderFx_t.kRenderFxNone;
        entity.Render = Color.FromArgb(alpha, 255, 255, 255);
        entity.Effects &= ~(NoDrawEffect | NoDrawButTransmitEffect);

        Utilities.SetStateChanged(entity, "CBaseModelEntity", "m_nRenderMode");
        Utilities.SetStateChanged(entity, "CBaseModelEntity", "m_nRenderFX");
        Utilities.SetStateChanged(entity, "CBaseModelEntity", "m_clrRender");
        Utilities.SetStateChanged(entity, "CBaseEntity", "m_fEffects");
    }

    private static void ApplyEquipmentRenderAlpha(CBaseModelEntity entity, int alpha)
    {
        ApplyRenderAlpha(entity, alpha);

        if (alpha < 255)
            entity.Effects |= NoDrawEffect;
        else
            entity.Effects &= ~(NoDrawEffect | NoDrawButTransmitEffect);

        entity.MarkEffectsStateChanged();
    }

    private bool HasClassAbility(PlayerState state, AbilityType type)
    {
        return _progressionService.HasAbilityAvailable(state, type);
    }

    private static bool IsOnGround(CCSPlayerPawn pawn)
    {
        const uint onGroundFlag = 1u;
        return pawn.OnGroundLastTick
            || (pawn.Flags & onGroundFlag) == onGroundFlag
            || pawn.GroundEntity.Value != null;
    }

    private void ResetBotAiAfterTeamChange(CCSPlayerController player, CCSPlayerController? infector)
    {
        if (!player.IsBot && infector?.IsBot != true)
            return;

        Server.ExecuteCommand("bot_stop 0");
        Server.ExecuteCommand("bot_dont_shoot 0");

        _ = Task.Run(async () =>
        {
            await Task.Delay(200);
            Server.NextFrame(() =>
            {
                Server.ExecuteCommand("bot_stop 0");
                Server.ExecuteCommand("bot_dont_shoot 0");
            });

            await Task.Delay(600);
            Server.NextFrame(() =>
            {
                Server.ExecuteCommand("bot_stop 0");
                Server.ExecuteCommand("bot_dont_shoot 0");
            });
        });
    }

    private static bool IsKnifePickup(EventItemPickup @event)
    {
        return ZombieMeleeVisualService.IsKnifeWeaponName(@event.Item);
    }

    private static bool AreSameFaction(
        CCSPlayerController victim,
        CCSPlayerController attacker,
        PlayerState victimState,
        PlayerState attackerState)
    {
        if (victimState.IsZombie == attackerState.IsZombie)
            return true;

        return victim.Team == attacker.Team
            && victim.Team is CsTeam.CounterTerrorist or CsTeam.Terrorist;
    }

    private void ApplyZombieKnockback(CCSPlayerPawn zombiePawn, CCSPlayerController attacker, PlayerState attackerState)
    {
        var humanClass = attackerState.SelectedHumanClass;
        var force = Math.Clamp(
            humanClass?.ZombieKnockbackForce ?? _config.HumanConfig.ZombieKnockbackForce,
            0.0f,
            1200.0f);
        force *= Math.Clamp(attackerState.ZombieKnockbackMultiplier, 0.0f, 1.0f);
        if (force <= 0.0f)
            return;

        var attackerPawn = attacker.PlayerPawn.Value;
        if (attackerPawn == null || !attackerPawn.IsValid || !zombiePawn.IsValid)
            return;

        var zombieOrigin = zombiePawn.AbsOrigin;
        var attackerOrigin = attackerPawn.AbsOrigin;

        Vector direction;
        if (zombieOrigin != null && attackerOrigin != null)
        {
            var dx = zombieOrigin.X - attackerOrigin.X;
            var dy = zombieOrigin.Y - attackerOrigin.Y;
            var length = MathF.Sqrt(dx * dx + dy * dy);

            direction = length > 0.001f
                ? new Vector(dx / length, dy / length, 0.0f)
                : attackerPawn.EyeAngles.ToForwardVector();
        }
        else
        {
            direction = attackerPawn.EyeAngles.ToForwardVector();
            direction.Z = 0.0f;
        }

        var upForce = Math.Clamp(
            humanClass?.ZombieKnockbackUpForce ?? _config.HumanConfig.ZombieKnockbackUpForce,
            0.0f,
            350.0f);
        upForce *= Math.Clamp(attackerState.ZombieKnockbackMultiplier, 0.0f, 1.0f);
        var velocity = new Vector(direction.X * force, direction.Y * force, upForce);
        zombiePawn.Teleport(velocity: velocity);
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

        var message = humansWon
            ? ChatText.Human($"{ChatColors.Gold}Humans win.{ChatColors.Default} {reason}")
            : ChatText.Zombie($"{ChatColors.Gold}Zombies win.{ChatColors.Default} {reason}");

        BroadcastChat(message);
        if (!humansWon)
            EmitSoundFromRandomAliveZombie(_config.SoundConfig.ZombiesWinSound, _config.SoundConfig.ExtraZombiesWinSounds);

        BroadcastCenterHtml(humansWon
            ? "<font color='#7fd7ff'>HUMANS WIN</font>"
            : "<font color='#ff3d3d'>ZOMBIES WIN</font>", 5);

        AwardRoundEndProgression(humansWon);

        Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(_config.GeneralConfig.PostRoundDelaySeconds));
            Server.NextFrame(() =>
            {
                if (Volatile.Read(ref _lifecycleId) != restartLifecycleId || _phase != RoundPhase.Ended)
                    return;

                if (TryRotateWorkshopMap())
                    return;

                if (_phase == RoundPhase.Ended)
                    StartRoundLifecycle();
            });
        });
    }

    private void AwardRoundEndProgression(bool humansWon)
    {
        foreach (var player in GetConnectedPlayers())
        {
            var state = player.GetState(_playerStates);
            if (!state.IsZombie)
                _progressionService.SyncNativeMoney(player, state, save: true);

            _progressionService.AwardMoney(player, state, _config.HumanConfig.MoneyPerRound, "finishing the round");
        }

        foreach (var player in GetAlivePlayers())
        {
            var state = player.GetState(_playerStates);
            if (humansWon && !state.IsZombie)
            {
                _progressionService.AwardReward(player, state, ProgressionRewardType.HumanSurvival, "survivals");
                _progressionService.AwardReward(player, state, ProgressionRewardType.RoundWin, "wins");
            }
            else if (!humansWon && state.IsZombie)
            {
                _progressionService.AwardReward(player, state, ProgressionRewardType.RoundWin, "wins");
            }
        }
    }

    private bool TryRotateWorkshopMap()
    {
        if (!_config.GeneralConfig.RotateWorkshopMaps)
            return false;

        var mapNames = GetConfiguredWorkshopMapNames();
        if (mapNames.Length == 0)
            return false;

        _completedRoundsOnCurrentWorkshopMap++;

        var roundsPerMap = Math.Max(1, _config.GeneralConfig.RoundsPerWorkshopMap);
        if (_completedRoundsOnCurrentWorkshopMap < roundsPerMap)
            return false;

        _completedRoundsOnCurrentWorkshopMap = 0;

        var nextIndex = _currentWorkshopMapIndex < 0
            ? 0
            : (_currentWorkshopMapIndex + 1) % mapNames.Length;
        var nextMapName = mapNames[nextIndex];
        _currentWorkshopMapIndex = nextIndex;
        ApplyWorkshopAddonOrder(nextIndex);

        BroadcastChat(ChatText.Zombie($"Loading next map: {ChatColors.Gold}{nextMapName}{ChatColors.Default}"));
        Console.WriteLine($"[ZombieMod] Loading map {nextMapName} from configured workshop rotation.");
        Server.ExecuteCommand($"map {nextMapName}");

        return true;
    }

    private void ApplyWorkshopAddonOrder(int activeMapIndex)
    {
        var addonIds = GetWorkshopAddonIdsForMapIndex(activeMapIndex);
        if (addonIds.Length == 0)
            return;

        var addonList = string.Join(",", addonIds);
        Server.ExecuteCommand($"mm_extra_addons \"{addonList}\"");
        Server.ExecuteCommand($"mm_client_extra_addons \"{addonList}\"");
        Server.ExecuteCommand("mm_cache_clients_with_addons 0");
    }

    private string[] GetWorkshopAddonIdsForMapIndex(int activeMapIndex)
    {
        var orderedIds = new List<string>();
        var mapIds = _config.GeneralConfig.WorkshopMapIds ?? [];

        if (activeMapIndex >= 0 && activeMapIndex < mapIds.Length)
            AddWorkshopAddonId(orderedIds, mapIds[activeMapIndex]);

        foreach (var addonId in _config.GeneralConfig.WorkshopAddonIds ?? [])
            AddWorkshopAddonId(orderedIds, addonId);

        foreach (var addonId in mapIds)
            AddWorkshopAddonId(orderedIds, addonId);

        return orderedIds.ToArray();
    }

    private static void AddWorkshopAddonId(List<string> addonIds, string? addonId)
    {
        if (string.IsNullOrWhiteSpace(addonId))
            return;

        var trimmedAddonId = addonId.Trim();
        if (!addonIds.Contains(trimmedAddonId, StringComparer.Ordinal))
            addonIds.Add(trimmedAddonId);
    }

    private string[] GetConfiguredWorkshopMapNames()
    {
        return (_config.GeneralConfig.WorkshopMapNames ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
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

    private void EmitPlayerSound(
        CCSPlayerController player,
        string? soundEventName,
        IEnumerable<string>? extraSoundEventNames = null)
    {
        ZombieSounds.EmitWithExtras(player, _config, soundEventName, extraSoundEventNames);
    }

    private void EmitPlayerSoundUntracked(
        CCSPlayerController player,
        string? soundEventName,
        IEnumerable<string>? extraSoundEventNames = null)
    {
        var pawn = player.PlayerPawn.Value;
        ZombieSounds.EmitWithExtras(pawn, _config, soundEventName, extraSoundEventNames);
    }

    private void TryEmitZombiePain(CCSPlayerController zombie, PlayerState state)
    {
        var now = DateTime.UtcNow;
        if (now < state.NextZombiePainSoundAtUtc)
            return;

        EmitPlayerSound(zombie, _config.SoundConfig.ZombiePainSound, _config.SoundConfig.ExtraZombiePainSounds);
        state.NextZombiePainSoundAtUtc = now.AddSeconds(Math.Max(0.1f, _config.SoundConfig.ZombiePainMinIntervalSeconds));
    }

    private void TryEmitZombieIdleSound(DateTime now)
    {
        if (_phase != RoundPhase.Active || now < _nextZombieIdleSoundAtUtc)
            return;

        EmitSoundFromRandomAliveZombie(_config.SoundConfig.ZombieIdleSound, _config.SoundConfig.ExtraZombieIdleSounds);
        ScheduleNextZombieIdleSound();
    }

    private void EmitSoundFromRandomAliveZombie(string? soundEventName, IEnumerable<string>? extraSoundEventNames = null)
    {
        var zombies = GetAlivePlayers()
            .Where(player => player.GetState(_playerStates).IsZombie)
            .ToList();

        if (zombies.Count == 0)
            return;

        EmitPlayerSound(zombies[_random.Next(zombies.Count)], soundEventName, extraSoundEventNames);
    }

    private void EmitPlayerOnlySoundToAll(string? soundEventName)
    {
        foreach (var player in GetConnectedPlayers())
            ZombieSounds.EmitToPlayerOnly(player, _config, soundEventName);
    }

    private void StartInfectionCountdownWorldSound()
    {
        StopInfectionCountdownWorldSound();

        var position = GetInfectionCountdownWorldSoundPosition();
        _infectionCountdownWorldSound = ZombieSounds.StartWorldSound(
            position,
            _config,
            _config.SoundConfig.InfectionCountdownWorldSound);

        Console.WriteLine($"[ZombieMod] Infection countdown world sound position: {FormatVector(position)}.");
    }

    private void StopInfectionCountdownWorldSound()
    {
        ZombieSounds.StopWorldSound(_infectionCountdownWorldSound);
        _infectionCountdownWorldSound = null;
    }

    private CounterStrikeSharp.API.Modules.Utils.Vector GetInfectionCountdownWorldSoundPosition()
    {
        var origins = GetAlivePlayers()
            .Select(player => player.PlayerPawn.Value?.AbsOrigin)
            .Where(origin => origin != null)
            .Select(origin => origin!)
            .ToList();

        if (origins.Count == 0)
        {
            origins = GetMapSpawnPoints()
                .Select(spawn => spawn.Position)
                .ToList();
        }

        if (origins.Count == 0)
            return new CounterStrikeSharp.API.Modules.Utils.Vector(0f, 0f, _config.SoundConfig.InfectionCountdownWorldSoundHeightOffset);

        var x = origins.Average(origin => origin.X);
        var y = origins.Average(origin => origin.Y);
        var z = origins.Average(origin => origin.Z) + _config.SoundConfig.InfectionCountdownWorldSoundHeightOffset;
        return new CounterStrikeSharp.API.Modules.Utils.Vector(x, y, z);
    }

    private void ScheduleNextZombieIdleSound()
    {
        var interval = Math.Max(4.0f, _config.SoundConfig.ZombieIdleIntervalSeconds);
        _nextZombieIdleSoundAtUtc = DateTime.UtcNow.AddSeconds(interval + _random.NextDouble() * Math.Min(interval, 6.0f));
    }

    private void CancelRoundLifecycle()
    {
        Interlocked.Increment(ref _lifecycleId);
        StopInfectionCountdownWorldSound();

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

    private static string FormatVector(CounterStrikeSharp.API.Modules.Utils.Vector vector)
    {
        return string.Join(
            " ",
            vector.X.ToString("0.##", CultureInfo.InvariantCulture),
            vector.Y.ToString("0.##", CultureInfo.InvariantCulture),
            vector.Z.ToString("0.##", CultureInfo.InvariantCulture));
    }

    private readonly record struct MapSpawnPoint(CounterStrikeSharp.API.Modules.Utils.Vector Position, CounterStrikeSharp.API.Modules.Utils.QAngle Angles);
}
