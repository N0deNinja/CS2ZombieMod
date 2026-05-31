# CS2 Zombie Mod

CounterStrikeSharp plugin for a local Counter-Strike 2 zombie mod prototype.

## Unified Workspace

This repo is intended to sit beside the COD plugin and shared library:

```text
C:\Users\hoppi\ReclaimCS
  reclaimcs-cod
  reclaimcs-zombie
  reclaimcs-shared
```

`ZombieModPlugin.csproj` references `..\reclaimcs-shared\src\ReclaimCS.Shared\ReclaimCS.Shared.csproj` for shared chat formatting, CounterStrikeSharp player/pawn helpers, and SQLite primitives. Zombie-specific infection, round, ability, shop, and progression logic stays in this repo.

## Local CS2 Server Tooling

These scripts install and use one repo-local CS2 dedicated server under `server/`.
Downloaded server files, SteamCMD files, and runtime payloads are ignored by git.

### 1. Initialize or update the local server

```bat
init-server.bat
```

This creates `server/`, downloads SteamCMD into `steamcmd/`, installs or updates the
CS2 dedicated server with Steam app `730`, installs Metamod:Source, installs the
latest CounterStrikeSharp `with-runtime` Windows release, and ensures Metamod is
referenced from `server/game/csgo/gameinfo.gi`.

### 2. Build and deploy the plugin

```bat
build-and-deploy.bat
```

The deploy script builds the solution with `dotnet build` and copies the build
output into:

```text
server/game/csgo/addons/counterstrikesharp/plugins/ZombieModPlugin/
```

Set `BUILD_CONFIGURATION=Release` before running the script if you want to deploy
the release build instead of the default debug build.

To build Release, refresh the local plugin folder, and copy it to the Ubuntu
server over SSH, run:

```bat
build-and-deploy-ssh.bat
```

The SSH deploy script defaults to:

```text
root@178.105.156.187:/root/server/game/csgo/addons/counterstrikesharp/plugins/
```

Override defaults with `ZM_SSH_KEY`, `ZM_SSH_USER`, `ZM_SSH_HOST`,
`ZM_REMOTE_PLUGINS_DIR`, or use `ZM_DEPLOY_DRY_RUN=1` to test without copying.
The deploy scripts preserve plugin SQLite data by excluding `data/` and
`*.db`, `*.db-wal`, `*.db-shm` files from replacement.

### 3. Start the local server

```bat
start-server.bat
```

By default this starts a LAN/local server on `zm_vents_remake_m` at port
`27015`. Workshop maps are loaded through `+host_workshop_map` at startup and
`host_workshop_map` during rotation. Do not put workshop map IDs in
MultiAddonManager; use it only for extra non-map model or sound packs.
The script copies and executes `server-config/zombiemod_server.cfg`, which
disables default bots, warmup, freeze time, team balancing, and enables random
spawn behavior for the local Zombie Mod server.
From the CS2 console, connect with:

```text
connect 127.0.0.1:27015
```

You can override the local test map or port:

```bat
set CS2_MAP=zm_silent_village
set CS2_PORT=27016
start-server.bat
```

For a server that should use a Steam Game Server Login Token, set `CS2_GSLT`:

```bat
set CS2_GSLT=your_token_here
start-server.bat
```

If the server window closes immediately, run `init-server.bat` again. The start
script also checks that the Steam runtime DLLs from `steamcmd/` are present next
to `cs2.exe` and pauses on startup failure so the error remains visible.

## Ability Commands

Ability commands are registered as CounterStrikeSharp `css_` commands, so they
can be used from the console and bound to keys. They also work as chat commands
without the `css_` prefix.

```text
bind mouse4 "css_zability 1"
bind mouse5 "css_zability 2"
bind v "css_zability 3"
bind x "css_zability_slot 1"
```

Use `!abilities` or `css_abilities` while playing as a zombie to list your
current usable abilities and unlockable abilities. Use `!abilities <ability_id>`
to unlock an ability for your current zombie type when you have enough XP.
Ability slots are loadout-based: bind `css_zability 1`, `css_zability 2`, and
`css_zability 3`, then use `!abilities` on your current zombie to see which
ability is in each slot. For Lurker, Wall Cling is a slotted active ability
that starts only while airborne beside a wall and is released with Space;
Lurker Cloak remains passive and does not need a bind.

### Ability sounds

Ability sounds are configured under `AbilityConfig` in the plugin JSON. Use
`ActivationSounds` for most abilities; if the list has more than one entry, the
plugin randomly plays one sound from the list. It does not stack the sounds.

```json
"Pounce": {
  "ActivationSounds": [
    "zr.zombie_attack_1"
  ]
}
```

`FrostBolt` has separate pools for casting and hitting:

```json
"FrostBolt": {
  "CastSounds": [ "zr.zombie_attack_10" ],
  "HitSounds": [ "zr.zombie_attack_6" ]
}
```

Older `ActivationSound` and `ExtraActivationSounds` fields still work, but they
are now treated as one random pool for backwards compatibility. For
`SelfDestruct`, set `ExplosionSound` to an empty string if `ActivationSounds`
should be the only sound.

General zombie sound behavior lives under `SoundConfig`. `EmitVolume` raises the
server-side volume used for emitted sounds. `UseTrackedPlayerSounds` is disabled
by default because CS2 point sound entities can behave like fixed world sounds
after playback starts. Keep ability sound clips short, or enable tracked sounds
only for stationary effects where `StopSound` is more important than following
the player.

## Kill Feed Icons

Zombie Mod exposes the shared `KillFeedIcons` config section. Set
`KillFeedIcons.Enabled` to `true` and map death-event weapon keys to mounted CS2
equipment icon keys, for example:

```json
"KillFeedIcons": {
  "Enabled": true,
  "Icons": {
    "knife": { "Icon": "reclaim_claws" },
    "hegrenade": { "Icon": "reclaim_explosion" }
  }
}
```

Custom SVGs should be mounted by an addon under
`panorama/images/icons/equipment/<icon>.svg`.

## Player Chat Commands

Player commands are available in chat with `!command` or from console as
`css_command`.

```text
!help
!shop
!xp
!stats
!zombies
!zombies unlock runner
!zombie runner
!zdefault brute
!humans
!humans unlock hunter
!human hunter
!hdefault vip_heavy
!abilities
!abilities unlock berserk
!abilities equip berserk 2
!abilities unequip 2
```

`!help` prints the quick command list. `!shop` opens the progression hub with
links to class lists, ability unlocks, loadouts, and stats. `!xp` and `!stats`
show global account level, current class level, XP progress, and lifetime
statistics.

Use `!zombies` and `!humans` to preview class stats, lock state, unlock
requirements, and page through longer lists. Use `!zombies unlock <id>` or
`!humans unlock <id>` once requirements are met. Use `!zombie <id>` or
`!zdefault <id>` to choose your default zombie, and `!human <id>` or
`!hdefault <id>` to choose your default human. Defaults persist in SQLite and
are applied the next time the round spawns you into that role.

Every new player starts at global level 1 with the first configured zombie class
unlocked and selected. Other zombie and human classes unlock through the
requirements in `ProgressionConfig`.

## Progression and Persistence

Progression has two layers:

```text
Global player level: account-wide XP shared across all classes
Class level: separate XP and level per zombie and human class
```

SQLite storage starts automatically when the plugin loads. The default database
file is created under the deployed plugin folder:

```text
data/zombiemod_progression.db
```

Balance knobs live under `ProgressionConfig`:

```text
Database.FilePath
GlobalLevelCurve
ClassLevelCurve
XpRewards.Infection
XpRewards.ZombieKill
XpRewards.HumanKill
XpRewards.RoundWin
XpRewards.HumanSurvival
XpRewards.Assist
ZombieClassUnlocks
HumanClassUnlocks
AbilityUnlocks
MaxEquippedZombieAbilities
MaxEquippedHumanAbilities
```

Unlock requirements support global level, class level, future currency, future
achievement flags, and arbitrary stat thresholds. Class and ability unlocks are
saved permanently, as are equipped ability loadouts.

## Zombie Claws and Knife Viewmodel

Zombies keep `weapon_knife` internally for CS2 melee timing, hit detection, and
left-click/right-click attacks. The invisible replacement model hides the knife
mesh while preserving the working melee behavior. To avoid using the player's
owned knife animation, the plugin can force the knife econ item definition to
Shadow Daggers (`516`) while still spawning the safe `weapon_knife` entity.
Spawning `weapon_knife_push` directly was tested and caused knife drops plus a
broken zombie viewmodel/attack path.

The plugin strips non-knife weapons from zombies, forces slot 3, and routes
zombie knife fire/hit events through
`ZombieMeleeVisualService` so custom claw animations, sounds, and per-class claw
effects can be added without changing infection logic.

The visual target is zombie hands/claws instead of the default knife blade. The
CounterStrikeSharp API used by this plugin exposes the networked knife entity
and pawn viewmodel offsets, but it does not expose a separate first-person
weapon viewmodel entity or per-player viewmodel model path. The plugin hides the
networked knife model and keeps the internal knife active; any fuller claw
viewmodel replacement should be handled by mounted assets outside this plugin
path.

For the first asset-side experiment, this repo includes a tiny transparent mesh
source at `assets/zombiemod/invisible_knife/`. Compile it through CS2 Workshop
Tools / ModelDoc to `models/zombiemod/viewmodels/v_invisible_knife.vmdl` and mount
that compiled asset. The default config below keeps direct model replacement
disabled because a missing or mis-mounted `.vmdl` renders as CS2's giant
`ERROR` placeholder; only set it true for local asset testing.

Zombie melee visual settings live under `ZombieMeleeVisualConfig`:

```text
HideZombieKnifeModel = true
ZombieMeleeWeaponName = weapon_knife
ZombieMeleeItemDefinitionIndex = 516
EnableZombieKnifeReplacementModel = false
ZombieKnifeReplacementModelPath = models/zombiemod/viewmodels/v_invisible_knife.vmdl
ZombieClawSoundResources =
ZombieClawSlashSound =
ZombieClawHitSound =
```

`ZombieClawSlashSound` and `ZombieClawHitSound` should be sound event names that
can be passed to `EmitSound`, not placeholder asset paths. Add any required
`.vsndevts` or `.vsnd` resources to `ZombieClawSoundResources`. Leaving the
sound fields empty disables the extra claw sounds and avoids missing-resource
logs; the built-in knife sounds may still play because the plugin does not
suppress client-side weapon audio.

## Admin Test Commands

The plugin includes local testing commands so you can try zombie classes without
waiting for a full lobby. By default these are enabled for local development. Set
`AdminTestConfig.RequireAdminPermissions = true` if you want CounterStrikeSharp
admin permissions to be required.

```text
css_zadmin
css_zclass classic
css_zclass molong
css_zclass runner
css_zclass brute
css_zclass cultist
css_zclass frozen
css_zclass lurker
css_zhuman
css_zhuman hunter
css_hclass vip_heavy
css_zbots round
css_zbots add 3 ct
css_zbots kick
css_zround restart
css_zround status
css_givexp global 500
css_givexp zombie 200
css_giveclassxp zombie brute 500
css_setlevel global 10
css_setlevel zombie brute 5
css_unlockclass zombie brute
css_unlockability zombie brute selfdestruct
css_unlockall
css_resetprogress
```

`css_zadmin` prints a numbered test menu. For fast class testing, use
`css_zclass <id>`; this pauses the zombie round loop in admin test mode and
forces your player into that zombie class. Use `css_zround restart` when you want
to leave test mode, kick test bots, and run the normal player-only zombie loop.

`css_zbots round` cancels any existing test loop, clears old bots, enables bot
participation, adds the default number of CT bots, and restarts the zombie loop.
This is useful when you are the only real player but want the countdown and
infection flow to run against bot humans.

Progression admin commands target yourself by default when run in-game. Add a
player name or SteamID as the last argument when running from console or when
editing another connected player.

## Round Loop

At round start the plugin resets connected players to human, optionally scatters
alive players across available map spawn points, shows a center HTML infection
countdown, and then infects a configurable number of random players.

Important config values live under `GeneralConfig`:

```text
FirstInfectionDelaySeconds = 14
MinimumPlayersToStart = 2
RoundDurationSeconds = 300
ActiveHudIntervalSeconds = 1
WaitingHudIntervalSeconds = 1
PostRoundDelaySeconds = 5
MinimumInitialZombies = 1
InitialZombieRatio = 0.15
MaximumInitialZombies = 0
RandomizePlayerSpawns = true
SpawnScatterDelaySeconds = 0.3
IncludeBotsInRound = false
AirAccelerate = 100
RotateWorkshopMaps = true
RoundsPerWorkshopMap = 5
WorkshopMapIds = 3623739053, 3685437201, 3222984182, 3283778158
WorkshopMapNames = zm_vents_remake_m, zm_liquid_anomaly_s, zm_silent_village, zm_mediumzm
```

The round waits until `MinimumPlayersToStart` playable players are connected
before the infection countdown begins. During the active round, zombie kills
infect humans and respawn the victim as a zombie. Zombies gain XP for infections,
level up using `ZombieConfig.XPPerLevel`, and the round ends when all humans are
infected, all zombies are eliminated, or humans survive until the round timer
expires.

The local server config disables CS2 warmup, freeze time, bots, team balancing,
and native round-win handling. The plugin owns the zombie round loop, awards
configured global and class XP for infections, kills, assists, round wins, and
human survival, shows the win message, waits `PostRoundDelaySeconds`, then
resets players into the next infection countdown without CS2 team-income/restart
behavior.
