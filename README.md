# CS2 Zombie Mod

CounterStrikeSharp plugin for a local Counter-Strike 2 zombie mod prototype.

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

### 3. Start the local server

```bat
start-server.bat
```

By default this starts a LAN/local server on `de_dust2` at port `27015`.
The script copies and executes `server-config/zombiemod_server.cfg`, which
disables default bots, warmup, freeze time, team balancing, and enables random
spawn behavior for the local Zombie Mod server.
From the CS2 console, connect with:

```text
connect 127.0.0.1:27015
```

You can override the local test map or port:

```bat
set CS2_MAP=de_mirage
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
bind mouse4 "css_zability pounce"
bind mouse5 "css_zability speed_boost"
bind v "css_zability invisibility"
bind x "css_zability_slot 1"
```

Use `!abilities` or `css_abilities` while playing as a zombie to list your
current usable abilities and unlockable abilities. Use `!abilities <ability_id>`
to unlock an ability for your current zombie type when you have enough XP.

## Admin Test Commands

The plugin includes local testing commands so you can try zombie classes without
waiting for a full lobby. By default these are enabled for local development. Set
`AdminTestConfig.RequireAdminPermissions = true` if you want CounterStrikeSharp
admin permissions to be required.

```text
css_zadmin
css_zclass brute
css_zclass runner
css_zclass stalker
css_zclass medic
css_zhuman
css_zbots round
css_zbots add 3 ct
css_zbots kick
css_zround restart
css_zround status
```

`css_zadmin` prints a numbered test menu. For fast class testing, use
`css_zclass <id>`; this pauses the zombie round loop in admin test mode and
forces your player into that zombie class. Use `css_zround restart` when you want
to leave test mode, kick test bots, and run the normal player-only zombie loop.

`css_zbots round` cancels any existing test loop, clears old bots, enables bot
participation, adds the default number of CT bots, and restarts the zombie loop.
This is useful when you are the only real player but want the countdown and
infection flow to run against bot humans.

## Round Loop

At round start the plugin resets connected players to human, optionally scatters
alive players across available map spawn points, shows a center HTML infection
countdown, and then infects a configurable number of random players.

Important config values live under `GeneralConfig`:

```text
FirstInfectionDelaySeconds = 15
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
```

The round waits until `MinimumPlayersToStart` playable players are connected
before the infection countdown begins. During the active round, zombie kills
infect humans and respawn the victim as a zombie. Zombies gain XP for infections,
level up using `ZombieConfig.XPPerLevel`, and the round ends when all humans are
infected, all zombies are eliminated, or humans survive until the round timer
expires.

The local server config disables CS2 warmup, freeze time, bots, team balancing,
and native round-win handling. The plugin owns the zombie round loop, shows the
win message, waits `PostRoundDelaySeconds`, then resets players into the next
infection countdown without CS2 team-income/restart behavior.
