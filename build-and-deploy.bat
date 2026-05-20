@echo off
setlocal EnableExtensions EnableDelayedExpansion

rem Builds the plugin and deploys the compiled output to the repo-local
rem CounterStrikeSharp plugin folder.

set "ROOT=%~dp0"
set "SERVER_DIR=%ROOT%server"
set "CSS_PLUGINS_DIR=%SERVER_DIR%\game\csgo\addons\counterstrikesharp\plugins"
set "CONFIGURATION=%BUILD_CONFIGURATION%"

if "%CONFIGURATION%"=="" set "CONFIGURATION=Debug"

echo.
echo [deploy] Building and deploying Zombie Mod
echo [deploy] Repo root: "%ROOT%"
echo [deploy] Configuration: %CONFIGURATION%
echo.

if not exist "%SERVER_DIR%" (
    echo [error] Local server folder does not exist: "%SERVER_DIR%"
    echo [error] Run init-server.bat first.
    exit /b 1
)

if not exist "%CSS_PLUGINS_DIR%" (
    echo [error] CounterStrikeSharp plugin folder does not exist: "%CSS_PLUGINS_DIR%"
    echo [error] Run init-server.bat first and make sure CounterStrikeSharp installed successfully.
    exit /b 1
)

set "SOLUTION_FILE="
for %%F in ("%ROOT%*.sln") do (
    if not defined SOLUTION_FILE set "SOLUTION_FILE=%%~fF"
)

if not defined SOLUTION_FILE (
    echo [error] No solution file was found in the repo root.
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

for /f "usebackq delims=" %%T in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "$xml=[xml](Get-Content -Raw '%PROJECT_FILE%'); $tfm=$xml.Project.PropertyGroup.TargetFramework | Select-Object -First 1; if (-not $tfm) { throw 'TargetFramework not found in project file.' }; $tfm"`) do set "TARGET_FRAMEWORK=%%T"

if not defined TARGET_FRAMEWORK (
    echo [error] Could not detect TargetFramework from "%PROJECT_FILE%".
    exit /b 1
)

set "OUTPUT_DIR=%ROOT%bin\%CONFIGURATION%\%TARGET_FRAMEWORK%"
set "TARGET_DIR=%CSS_PLUGINS_DIR%\%PLUGIN_NAME%"

echo [deploy] Solution: "%SOLUTION_FILE%"
echo [deploy] Project: "%PROJECT_FILE%"
echo [deploy] Target framework: %TARGET_FRAMEWORK%
echo.

echo [deploy] Running dotnet build...
dotnet build "%SOLUTION_FILE%" -c "%CONFIGURATION%"
if errorlevel 1 exit /b 1

if not exist "%OUTPUT_DIR%" (
    echo [error] Build output folder was not found: "%OUTPUT_DIR%"
    exit /b 1
)

if not exist "%OUTPUT_DIR%\%PLUGIN_NAME%.dll" (
    echo [error] Built plugin DLL was not found: "%OUTPUT_DIR%\%PLUGIN_NAME%.dll"
    exit /b 1
)

echo.
echo [deploy] Copying plugin output to:
echo [deploy] "%TARGET_DIR%"
if not exist "%TARGET_DIR%" mkdir "%TARGET_DIR%"
robocopy "%OUTPUT_DIR%" "%TARGET_DIR%" /E /PURGE /XF "*.xml" >nul
if %ERRORLEVEL% GEQ 8 (
    echo [error] Failed to copy plugin files. Robocopy exit code: %ERRORLEVEL%
    exit /b 1
)

echo [deploy] Deploy complete.
exit /b 0
