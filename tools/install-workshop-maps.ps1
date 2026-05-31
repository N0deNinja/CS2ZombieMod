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
        Id = "3623739053"
        ArchiveCandidates = @("3623739053_dir.vpk", "3623739053.vpk")
        MapFile = "zm_vents_remake_m.vpk"
    },
    @{
        Id = "3685437201"
        ArchiveCandidates = @("3685437201_dir.vpk", "3685437201.vpk")
        MapFile = "zm_liquid_anomaly_s.vpk"
    },
    @{
        Id = "3222984182"
        ArchiveCandidates = @("3222984182.vpk", "3222984182_dir.vpk")
        MapFile = "zm_silent_village.vpk"
    },
    @{
        Id = "3283778158"
        ArchiveCandidates = @("3283778158_dir.vpk", "3283778158.vpk")
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
    $archive = $null
    foreach ($candidate in $map.ArchiveCandidates) {
        $candidatePath = Join-Path $workshopRoot (Join-Path $map.Id $candidate)
        if (Test-Path -LiteralPath $candidatePath) {
            $archive = $candidatePath
            break
        }
    }

    $target = Join-Path $mapsDir $map.MapFile

    if (!$archive) {
        Write-Warning "Workshop archive for $($map.Id) is missing. Checked: $($map.ArchiveCandidates -join ', ')"
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
