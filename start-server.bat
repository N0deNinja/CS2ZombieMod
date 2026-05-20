@echo off
setlocal EnableExtensions

rem Starts the repo-local CS2 dedicated server with local testing defaults.
rem Set CS2_GSLT before running this script to attach a Steam Game Server Login Token.

set "ROOT=%~dp0"
set "SERVER_DIR=%ROOT%server"
set "STEAMCMD_DIR=%ROOT%steamcmd"
set "GAME_BIN_DIR=%SERVER_DIR%\game\bin\win64"
set "SERVER_CFG_SOURCE=%ROOT%server-config\zombiemod_server.cfg"
set "SERVER_CFG_TARGET=%SERVER_DIR%\game\csgo\cfg\zombiemod_server.cfg"
set "GAMEMODE_CFG_SOURCE=%ROOT%server-config\gamemode_casual_server.cfg"
set "GAMEMODE_CFG_TARGET=%SERVER_DIR%\game\csgo\cfg\gamemode_casual_server.cfg"
set "CS2_EXE=%GAME_BIN_DIR%\cs2.exe"
set "PORT=%CS2_PORT%"
set "MAP=%CS2_MAP%"
set "LAN_ARGS=+sv_lan 1"
set "TOKEN_ARGS="

if "%PORT%"=="" set "PORT=27015"
if "%MAP%"=="" set "MAP=de_dust2"

if not exist "%CS2_EXE%" (
    echo [error] CS2 server executable was not found: "%CS2_EXE%"
    echo [error] Run init-server.bat first.
    exit /b 1
)

rem CS2 dedicated server on Windows needs these Steam runtime DLLs near cs2.exe
rem unless the full Steam client is installed and discoverable globally.
for %%F in (steamclient64.dll tier0_s64.dll vstdlib_s64.dll) do (
    if not exist "%GAME_BIN_DIR%\%%F" (
        if exist "%STEAMCMD_DIR%\%%F" (
            echo [start] Installing missing Steam runtime DLL: %%F
            copy /Y "%STEAMCMD_DIR%\%%F" "%GAME_BIN_DIR%\%%F" >nul
        )
    )

    if not exist "%GAME_BIN_DIR%\%%F" (
        echo [error] Missing required Steam runtime DLL: "%GAME_BIN_DIR%\%%F"
        echo [error] Run init-server.bat again.
        pause
        exit /b 1
    )
)

if not exist "%SERVER_CFG_SOURCE%" (
    echo [error] Zombie Mod server config was not found: "%SERVER_CFG_SOURCE%"
    exit /b 1
)

if not exist "%GAMEMODE_CFG_SOURCE%" (
    echo [error] Zombie Mod gamemode override was not found: "%GAMEMODE_CFG_SOURCE%"
    exit /b 1
)

echo [start] Installing Zombie Mod server config...
copy /Y "%SERVER_CFG_SOURCE%" "%SERVER_CFG_TARGET%" >nul
if errorlevel 1 (
    echo [error] Failed to copy Zombie Mod server config to "%SERVER_CFG_TARGET%"
    pause
    exit /b 1
)

rem CS2 applies gamemode_casual_server.cfg during map startup after default casual cvars.
rem That is the earliest reliable place to kill warmup/freeze for this local server.
copy /Y "%GAMEMODE_CFG_SOURCE%" "%GAMEMODE_CFG_TARGET%" >nul
if errorlevel 1 (
    echo [error] Failed to copy Zombie Mod gamemode override to "%GAMEMODE_CFG_TARGET%"
    pause
    exit /b 1
)

if not "%CS2_GSLT%"=="" (
    set "LAN_ARGS=+sv_lan 0"
    set "TOKEN_ARGS=+sv_setsteamaccount %CS2_GSLT%"
    echo [start] CS2_GSLT detected. Starting with Steam account token.
) else (
    echo [start] CS2_GSLT is not set. Starting LAN/local server mode.
)

echo [start] Port: %PORT%
echo [start] Map: %MAP%
echo [start] Connect from CS2 console with: connect 127.0.0.1:%PORT%
echo.

pushd "%GAME_BIN_DIR%"
"%CS2_EXE%" -dedicated -console -usercon -insecure -condebug -port %PORT% %LAN_ARGS% %TOKEN_ARGS% +game_type 0 +game_mode 0 +exec zombiemod_server.cfg +mp_do_warmup_period 0 +mp_warmuptime 0 +mp_warmup_end +mp_freezetime 0 +mp_round_restart_delay 0 +mp_roundtime 5.25 +mp_roundtime_defuse 5.25 +mp_roundtime_hostage 5.25 +mp_ignore_round_win_conditions 1 +mp_timelimit 0 +mp_teammates_are_enemies 0 +mp_friendlyfire 0 +bot_quota 0 +bot_kick +bot_stop 0 +bot_dont_shoot 0 +mp_autoteambalance 0 +mp_limitteams 0 +mp_randomspawn 1 +mp_randomspawn_los 0 +map %MAP%
set "EXIT_CODE=%ERRORLEVEL%"
popd

if not "%EXIT_CODE%"=="0" (
    echo.
    echo [error] CS2 server exited with code %EXIT_CODE%.
    echo [error] If the console output above is short, check for crash dumps or rerun init-server.bat.
    pause
)

exit /b %EXIT_CODE%
