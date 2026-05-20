@echo off
setlocal EnableExtensions

rem Bootstraps a repo-local CS2 dedicated server with SteamCMD, Metamod:Source,
rem and CounterStrikeSharp with its bundled .NET runtime.

set "ROOT=%~dp0"
set "SERVER_DIR=%ROOT%server"
set "STEAMCMD_DIR=%ROOT%steamcmd"
set "DOWNLOAD_DIR=%STEAMCMD_DIR%\downloads"
set "STEAMCMD_EXE=%STEAMCMD_DIR%\steamcmd.exe"
set "GAME_CSGO_DIR=%SERVER_DIR%\game\csgo"
set "GAME_BIN_DIR=%SERVER_DIR%\game\bin\win64"
set "GAMEINFO=%GAME_CSGO_DIR%\gameinfo.gi"
set "CS2_EXE=%SERVER_DIR%\game\bin\win64\cs2.exe"

echo.
echo [init] CS2 Zombie Mod local server setup
echo [init] Repo root: "%ROOT%"
echo.

if not exist "%STEAMCMD_DIR%" mkdir "%STEAMCMD_DIR%"
if not exist "%DOWNLOAD_DIR%" mkdir "%DOWNLOAD_DIR%"
if not exist "%SERVER_DIR%" mkdir "%SERVER_DIR%"

if not exist "%STEAMCMD_EXE%" (
    echo [init] SteamCMD not found. Downloading SteamCMD...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; Invoke-WebRequest -Uri 'https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip' -OutFile '%DOWNLOAD_DIR%\steamcmd.zip'; Expand-Archive -Path '%DOWNLOAD_DIR%\steamcmd.zip' -DestinationPath '%STEAMCMD_DIR%' -Force"
    if errorlevel 1 goto :error
) else (
    echo [init] SteamCMD already installed.
)

echo.
echo [init] Installing or updating CS2 dedicated server into "%SERVER_DIR%"...
"%STEAMCMD_EXE%" +force_install_dir "%SERVER_DIR%" +login anonymous +app_update 730 validate +quit
if errorlevel 1 (
    echo [warn] SteamCMD returned a non-zero exit code.
    if exist "%CS2_EXE%" (
        echo [warn] CS2 server executable exists, so continuing with modding stack repair.
    ) else (
        goto :error
    )
)

if not exist "%GAME_CSGO_DIR%" (
    echo [error] Expected CS2 game folder was not found: "%GAME_CSGO_DIR%"
    goto :error
)

echo.
echo [init] Installing Steam runtime DLLs required by the Windows dedicated server...
for %%F in (steamclient64.dll tier0_s64.dll vstdlib_s64.dll) do (
    if not exist "%STEAMCMD_DIR%\%%F" (
        echo [error] Missing SteamCMD runtime file: "%STEAMCMD_DIR%\%%F"
        goto :error
    )

    copy /Y "%STEAMCMD_DIR%\%%F" "%GAME_BIN_DIR%\%%F" >nul
    if errorlevel 1 goto :error
)

echo.
echo [init] Downloading latest Metamod:Source 2.x Windows build...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; $page = Invoke-WebRequest -UseBasicParsing -Uri 'https://www.metamodsource.net/downloads.php/?branch=master'; $link = ($page.Links | Where-Object { $_.href -match 'mmsource-2\.0\.0-git\d+-windows\.zip$' } | Select-Object -First 1).href; if (-not $link) { throw 'Could not locate the latest Metamod Windows ZIP.' }; Invoke-WebRequest -Uri $link -OutFile '%DOWNLOAD_DIR%\metamod-windows.zip'; if (Test-Path '%DOWNLOAD_DIR%\metamod') { Remove-Item '%DOWNLOAD_DIR%\metamod' -Recurse -Force }; Expand-Archive -Path '%DOWNLOAD_DIR%\metamod-windows.zip' -DestinationPath '%DOWNLOAD_DIR%\metamod' -Force; Copy-Item -Path '%DOWNLOAD_DIR%\metamod\addons' -Destination '%GAME_CSGO_DIR%' -Recurse -Force"
if errorlevel 1 goto :error

if exist "%GAMEINFO%" (
    echo [init] Ensuring Metamod search path exists in gameinfo.gi...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $path='%GAMEINFO%'; $text = Get-Content -Raw -Path $path; if ($text -notmatch 'Game\s+csgo/addons/metamod') { $lines = Get-Content -Path $path; $out = New-Object System.Collections.Generic.List[string]; $inserted = $false; foreach ($line in $lines) { $out.Add($line); if (-not $inserted -and $line -match '^\s*Game_LowViolence\s+csgo_lv\b') { $out.Add(\"`t`t`tGame`tcsgo/addons/metamod\"); $inserted = $true } }; if (-not $inserted) { throw 'Could not find Game_LowViolence csgo_lv in gameinfo.gi.' }; $encoding = New-Object System.Text.UTF8Encoding($false); [System.IO.File]::WriteAllLines($path, $out, $encoding) }"
    if errorlevel 1 goto :error
) else (
    echo [error] Could not find gameinfo.gi at "%GAMEINFO%"
    goto :error
)

echo.
echo [init] Downloading latest CounterStrikeSharp with bundled runtime...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; $release = Invoke-RestMethod -Uri 'https://api.github.com/repos/roflmuffin/CounterStrikeSharp/releases/latest'; $asset = $release.assets | Where-Object { $_.name -like 'counterstrikesharp-with-runtime-windows-*.zip' } | Select-Object -First 1; if (-not $asset) { throw 'Could not locate the CounterStrikeSharp with-runtime Windows ZIP.' }; Invoke-WebRequest -Uri $asset.browser_download_url -OutFile '%DOWNLOAD_DIR%\counterstrikesharp-with-runtime-windows.zip'; if (Test-Path '%DOWNLOAD_DIR%\counterstrikesharp') { Remove-Item '%DOWNLOAD_DIR%\counterstrikesharp' -Recurse -Force }; Expand-Archive -Path '%DOWNLOAD_DIR%\counterstrikesharp-with-runtime-windows.zip' -DestinationPath '%DOWNLOAD_DIR%\counterstrikesharp' -Force; Copy-Item -Path '%DOWNLOAD_DIR%\counterstrikesharp\addons' -Destination '%GAME_CSGO_DIR%' -Recurse -Force"
if errorlevel 1 goto :error

echo.
echo [init] Checking Microsoft Visual C++ Redistributable runtime...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$runtime = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64' -ErrorAction SilentlyContinue; if ($runtime -and $runtime.Installed -eq 1) { exit 0 } exit 1"
if errorlevel 1 (
    echo [init] VC++ Redistributable was not detected. Downloading and running the installer...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; Invoke-WebRequest -Uri 'https://aka.ms/vs/17/release/vc_redist.x64.exe' -OutFile '%DOWNLOAD_DIR%\vc_redist.x64.exe'"
    if errorlevel 1 goto :error
    "%DOWNLOAD_DIR%\vc_redist.x64.exe" /install /quiet /norestart
    if errorlevel 1 (
        echo [warn] VC++ Redistributable installer did not complete cleanly.
        echo [warn] If CounterStrikeSharp fails to load, run "%DOWNLOAD_DIR%\vc_redist.x64.exe" as Administrator.
    )
) else (
    echo [init] VC++ Redistributable is installed.
)

echo.
echo [init] Server setup complete.
echo [init] Next: run build-and-deploy.bat, then start-server.bat.
exit /b 0

:error
echo.
echo [error] init-server.bat failed. Check the output above for details.
exit /b 1
