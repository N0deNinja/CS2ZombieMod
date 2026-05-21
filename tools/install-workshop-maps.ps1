param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"

$Root = [System.IO.Path]::GetFullPath($Root.Trim('"'))
$source2Viewer = Join-Path $Root "tools\source2viewer\Source2Viewer-CLI.exe"
$workshopRoot = Join-Path $Root "server\game\bin\win64\steamapps\workshop\content\730"
$gameCsgoDir = Join-Path $Root "server\game\csgo"
$mapsDir = Join-Path $gameCsgoDir "maps"

$maps = @(
    @{
        Id = "3685437201"
        Archive = "3685437201_dir.vpk"
        MapFile = "zm_liquid_anomaly_s.vpk"
    },
    @{
        Id = "3222984182"
        Archive = "3222984182.vpk"
        MapFile = "zm_silent_village.vpk"
    },
    @{
        Id = "3283778158"
        Archive = "3283778158_dir.vpk"
        MapFile = "zm_mediumzm.vpk"
    }
)

if (!(Test-Path -LiteralPath $source2Viewer)) {
    Write-Warning "Source2Viewer CLI was not found at $source2Viewer. Skipping workshop map extraction."
    exit 0
}

if (!(Test-Path -LiteralPath $mapsDir)) {
    New-Item -ItemType Directory -Path $mapsDir | Out-Null
}

foreach ($map in $maps) {
    $archive = Join-Path $workshopRoot (Join-Path $map.Id $map.Archive)
    $target = Join-Path $mapsDir $map.MapFile

    if (!(Test-Path -LiteralPath $archive)) {
        Write-Warning "Workshop archive for $($map.Id) is missing: $archive"
        continue
    }

    if ((Test-Path -LiteralPath $target) -and
        ((Get-Item -LiteralPath $target).LastWriteTimeUtc -ge (Get-Item -LiteralPath $archive).LastWriteTimeUtc)) {
        Write-Host "[maps] $($map.MapFile) is already current."
        continue
    }

    Write-Host "[maps] Extracting $($map.MapFile) from workshop item $($map.Id)..."
    & $source2Viewer -i $archive -o $gameCsgoDir -f "maps/$($map.MapFile)"

    if (!(Test-Path -LiteralPath $target)) {
        throw "Expected extracted map was not created: $target"
    }
}
