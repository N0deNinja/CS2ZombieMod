@echo off
setlocal EnableExtensions EnableDelayedExpansion

rem Builds the plugin, refreshes the repo-local CounterStrikeSharp plugin
rem folder, then deploys that plugin folder to the Ubuntu server over SSH.
rem
rem Optional overrides:
rem   BUILD_CONFIGURATION=Debug
rem   ZM_SSH_KEY=C:\path\to\key
rem   ZM_SSH_USER=root
rem   ZM_SSH_HOST=178.105.156.187
rem   ZM_REMOTE_PLUGINS_DIR=/root/server/game/csgo/addons/counterstrikesharp/plugins
rem   ZM_DEPLOY_DRY_RUN=1
rem   ZM_REMOTE_POST_DEPLOY_COMMAND=systemctl restart cs2
rem
rem The deploy preserves server-side SQLite data by keeping the remote plugin
rem data/ directory and never copying local data/*.db files over it.

set "ROOT=%~dp0"
set "LOCAL_DEPLOY_SCRIPT=%ROOT%build-and-deploy.bat"
set "CONFIGURATION=%BUILD_CONFIGURATION%"
set "SSH_KEY=%ZM_SSH_KEY%"
set "SSH_USER=%ZM_SSH_USER%"
set "SSH_HOST=%ZM_SSH_HOST%"
set "REMOTE_PLUGINS_DIR=%ZM_REMOTE_PLUGINS_DIR%"

if "%CONFIGURATION%"=="" set "CONFIGURATION=Release"
if "%SSH_KEY%"=="" set "SSH_KEY=%USERPROFILE%\.ssh\id_ed25519"
if "%SSH_USER%"=="" set "SSH_USER=root"
if "%SSH_HOST%"=="" set "SSH_HOST=178.105.156.187"
if "%REMOTE_PLUGINS_DIR%"=="" set "REMOTE_PLUGINS_DIR=/root/server/game/csgo/addons/counterstrikesharp/plugins"

if not exist "%LOCAL_DEPLOY_SCRIPT%" (
    echo [error] Local deploy script was not found: "%LOCAL_DEPLOY_SCRIPT%"
    exit /b 1
)

if not exist "%SSH_KEY%" (
    echo [error] SSH key was not found: "%SSH_KEY%"
    exit /b 1
)

where ssh >nul 2>nul
if errorlevel 1 (
    echo [error] ssh was not found in PATH.
    exit /b 1
)

where scp >nul 2>nul
if errorlevel 1 (
    echo [error] scp was not found in PATH.
    exit /b 1
)

set "PROJECT_FILE="
for %%F in ("%ROOT%*.csproj") do (
    if not defined PROJECT_FILE set "PROJECT_FILE=%%~fF"
)

if not defined PROJECT_FILE (
    echo [error] No project file was found in the repo root.
    exit /b 1
)

for %%F in ("%PROJECT_FILE%") do set "PLUGIN_NAME=%%~nF"

set "LOCAL_PLUGIN_DIR=%ROOT%server\game\csgo\addons\counterstrikesharp\plugins\%PLUGIN_NAME%"
set "REMOTE_PLUGIN_DIR=%REMOTE_PLUGINS_DIR%/%PLUGIN_NAME%"
set "REMOTE_STAGE_DIR=%REMOTE_PLUGINS_DIR%/.%PLUGIN_NAME%.upload"

echo.
echo [ssh-deploy] Building and deploying Zombie Mod to SSH
echo [ssh-deploy] Repo root: "%ROOT%"
echo [ssh-deploy] Configuration: %CONFIGURATION%
echo [ssh-deploy] Local plugin folder: "%LOCAL_PLUGIN_DIR%"
echo [ssh-deploy] Remote target: %SSH_USER%@%SSH_HOST%:%REMOTE_PLUGIN_DIR%
echo.

set "BUILD_CONFIGURATION=%CONFIGURATION%"
call "%LOCAL_DEPLOY_SCRIPT%"
if errorlevel 1 exit /b 1

if not exist "%LOCAL_PLUGIN_DIR%\%PLUGIN_NAME%.dll" (
    echo [error] Local plugin DLL was not found after build: "%LOCAL_PLUGIN_DIR%\%PLUGIN_NAME%.dll"
    exit /b 1
)

if "%ZM_DEPLOY_DRY_RUN%"=="1" (
    echo.
    echo [ssh-deploy] Dry run enabled. SSH copy was skipped.
    echo [ssh-deploy] Would stage to: %REMOTE_STAGE_DIR%
    echo [ssh-deploy] Would replace:  %REMOTE_PLUGIN_DIR%
    exit /b 0
)

echo.
echo [ssh-deploy] Preparing remote staging folder...
ssh -i "%SSH_KEY%" "%SSH_USER%@%SSH_HOST%" "set -e; rm -rf '%REMOTE_STAGE_DIR%'; mkdir -p '%REMOTE_STAGE_DIR%'"
if errorlevel 1 exit /b 1

echo [ssh-deploy] Copying plugin folder with scp...
scp -i "%SSH_KEY%" -r "%LOCAL_PLUGIN_DIR%" "%SSH_USER%@%SSH_HOST%:%REMOTE_STAGE_DIR%/"
if errorlevel 1 exit /b 1

echo [ssh-deploy] Replacing remote plugin folder...
ssh -i "%SSH_KEY%" "%SSH_USER%@%SSH_HOST%" "set -e; mkdir -p '%REMOTE_PLUGINS_DIR%'; rm -rf '%REMOTE_STAGE_DIR%/%PLUGIN_NAME%/data'; if [ -d '%REMOTE_PLUGIN_DIR%/data' ]; then mkdir -p '%REMOTE_STAGE_DIR%/%PLUGIN_NAME%'; cp -a '%REMOTE_PLUGIN_DIR%/data' '%REMOTE_STAGE_DIR%/%PLUGIN_NAME%/data'; fi; rm -f '%REMOTE_STAGE_DIR%/%PLUGIN_NAME%'/*.db '%REMOTE_STAGE_DIR%/%PLUGIN_NAME%'/*.db-shm '%REMOTE_STAGE_DIR%/%PLUGIN_NAME%'/*.db-wal; rm -rf '%REMOTE_PLUGIN_DIR%'; mv '%REMOTE_STAGE_DIR%/%PLUGIN_NAME%' '%REMOTE_PLUGIN_DIR%'; rmdir '%REMOTE_STAGE_DIR%' 2>/dev/null || true"
if errorlevel 1 exit /b 1

echo [ssh-deploy] Quarantining stale COD plugin from Zombie server install if present...
ssh -i "%SSH_KEY%" "%SSH_USER%@%SSH_HOST%" "set -e; if [ -d '%REMOTE_PLUGINS_DIR%/ReclaimCsCod' ]; then mv '%REMOTE_PLUGINS_DIR%/ReclaimCsCod' '%REMOTE_PLUGINS_DIR%/.ReclaimCsCod.disabled-$(date +%%Y%%m%%d%%H%%M%%S)'; fi"
if errorlevel 1 exit /b 1

if defined ZM_REMOTE_POST_DEPLOY_COMMAND (
    echo [ssh-deploy] Running remote post-deploy command...
    ssh -i "%SSH_KEY%" "%SSH_USER%@%SSH_HOST%" "%ZM_REMOTE_POST_DEPLOY_COMMAND%"
    if errorlevel 1 exit /b 1
)

echo [ssh-deploy] Deploy complete.
exit /b 0
