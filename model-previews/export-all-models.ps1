param(
    [string]$WorkshopVpk = "C:\Users\hoppi\CS2ZombieMod\server\game\bin\win64\steamapps\workshop\content\730\3170427476\3170427476_dir.vpk",
    [string]$CliPath = "C:\Users\hoppi\CS2ZombieMod\tools\source2viewer\Source2Viewer-CLI.exe",
    [string]$PreviewRoot = "C:\Users\hoppi\CS2ZombieMod\model-previews",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

if (!(Test-Path -LiteralPath $WorkshopVpk)) {
    throw "Workshop VPK not found: $WorkshopVpk"
}

if (!(Test-Path -LiteralPath $CliPath)) {
    throw "Source2Viewer CLI not found: $CliPath"
}

$exportsRoot = Join-Path $PreviewRoot "exports"
New-Item -ItemType Directory -Force -Path $exportsRoot | Out-Null

function Read-VpkDirectoryEntries {
    param([string]$Path)

    $script:bytes = [IO.File]::ReadAllBytes($Path)
    $script:pos = 0

    function Read-U32 {
        $value = [BitConverter]::ToUInt32($script:bytes, $script:pos)
        $script:pos += 4
        return $value
    }

    function Read-U16 {
        $value = [BitConverter]::ToUInt16($script:bytes, $script:pos)
        $script:pos += 2
        return $value
    }

    function Read-ZStr {
        $start = $script:pos
        while ($script:pos -lt $script:bytes.Length -and $script:bytes[$script:pos] -ne 0) {
            $script:pos++
        }

        $value = [Text.Encoding]::UTF8.GetString($script:bytes, $start, $script:pos - $start)
        $script:pos++
        return $value
    }

    [void](Read-U32)
    $version = Read-U32
    $treeSize = Read-U32

    if ($version -ge 2) {
        [void](Read-U32)
        [void](Read-U32)
        [void](Read-U32)
        [void](Read-U32)
    }

    $treeEnd = $script:pos + $treeSize
    $entries = New-Object System.Collections.Generic.List[string]

    while ($script:pos -lt $treeEnd) {
        $ext = Read-ZStr
        if ($ext.Length -eq 0) {
            break
        }

        while ($script:pos -lt $treeEnd) {
            $pathPart = Read-ZStr
            if ($pathPart.Length -eq 0) {
                break
            }

            while ($script:pos -lt $treeEnd) {
                $name = Read-ZStr
                if ($name.Length -eq 0) {
                    break
                }

                [void](Read-U32)
                $preloadBytes = Read-U16
                [void](Read-U16)
                [void](Read-U32)
                [void](Read-U32)
                [void](Read-U16)

                if ($preloadBytes -gt 0) {
                    $script:pos += $preloadBytes
                }

                $fullPath = if ($pathPart -eq " ") { "$name.$ext" } else { "$pathPart/$name.$ext" }
                $entries.Add($fullPath)
            }
        }
    }

    return $entries
}

function Get-SafeFolderName {
    param([string]$ModelPath)

    return (($ModelPath -replace "\.vmdl_c$", "") -replace '[\\/:*?"<>|]', "_")
}

function Convert-ToPreviewLabel {
    param([string]$ModelPath)

    return ($ModelPath -replace "\.vmdl_c$", ".vmdl")
}

function Patch-GlbForBrowserPreview {
    param([IO.FileInfo]$File)

    $bytes = [IO.File]::ReadAllBytes($File.FullName)
    if ([Text.Encoding]::ASCII.GetString($bytes, 0, 4) -ne "glTF") {
        return $false
    }

    $version = [BitConverter]::ToUInt32($bytes, 4)
    $jsonLength = [BitConverter]::ToUInt32($bytes, 12)
    $jsonType = [Text.Encoding]::ASCII.GetString($bytes, 16, 4)
    if ($jsonType -ne "JSON") {
        return $false
    }

    $jsonText = [Text.Encoding]::UTF8.GetString($bytes, 20, $jsonLength).TrimEnd([char]0x20, [char]0x00)
    $doc = $jsonText | ConvertFrom-Json
    $changed = $false

    if ($doc.images) {
        foreach ($image in $doc.images) {
            if (!$image.name) {
                continue
            }

            $pngName = [IO.Path]::ChangeExtension([string]$image.name, ".png")
            $pngPath = Join-Path $File.DirectoryName $pngName
            if (!(Test-Path -LiteralPath $pngPath)) {
                continue
            }

            if ($image.PSObject.Properties.Name -contains "uri") {
                $image.uri = $pngName
            } else {
                $image | Add-Member -NotePropertyName uri -NotePropertyValue $pngName
            }

            if ($image.PSObject.Properties.Name -contains "mimeType") {
                $image.mimeType = "image/png"
            } else {
                $image | Add-Member -NotePropertyName mimeType -NotePropertyValue "image/png"
            }

            if ($image.PSObject.Properties.Name -contains "bufferView") {
                $image.PSObject.Properties.Remove("bufferView")
            }

            $changed = $true
        }
    }

    if ($doc.materials) {
        foreach ($material in $doc.materials) {
            if ($material.PSObject.Properties.Name -contains "occlusionTexture") {
                $material.PSObject.Properties.Remove("occlusionTexture")
                $changed = $true
            }

            if (!$material.pbrMetallicRoughness) {
                $material | Add-Member -NotePropertyName pbrMetallicRoughness -NotePropertyValue ([pscustomobject]@{})
                $changed = $true
            }

            $pbr = $material.pbrMetallicRoughness

            if ($pbr.PSObject.Properties.Name -contains "metallicFactor") {
                $pbr.metallicFactor = 0
            } else {
                $pbr | Add-Member -NotePropertyName metallicFactor -NotePropertyValue 0
            }

            if ($pbr.PSObject.Properties.Name -contains "roughnessFactor") {
                $pbr.roughnessFactor = 0.85
            } else {
                $pbr | Add-Member -NotePropertyName roughnessFactor -NotePropertyValue 0.85
            }

            if (($pbr.PSObject.Properties.Name -notcontains "baseColorTexture") -and
                ($pbr.PSObject.Properties.Name -contains "baseColorFactor")) {
                $factor = $pbr.baseColorFactor
                if ($factor.Count -ge 3 -and $factor[0] -lt 0.05 -and $factor[1] -lt 0.05 -and $factor[2] -lt 0.05) {
                    $pbr.baseColorFactor = @(0.75, 0.75, 0.75, 1)
                }
            }

            $changed = $true
        }
    }

    if (!$changed) {
        return $false
    }

    $newJson = ($doc | ConvertTo-Json -Depth 100 -Compress)
    $newJsonBytes = [Text.Encoding]::UTF8.GetBytes($newJson)
    $jsonPadding = (4 - ($newJsonBytes.Length % 4)) % 4
    if ($jsonPadding -gt 0) {
        $newJsonBytes = $newJsonBytes + ([byte[]](0x20) * $jsonPadding)
    }

    $binStart = 20 + $jsonLength
    $binBytes = $bytes[$binStart..($bytes.Length - 1)]
    $newLength = 12 + 8 + $newJsonBytes.Length + $binBytes.Length
    $out = New-Object byte[] $newLength

    [Array]::Copy([Text.Encoding]::ASCII.GetBytes("glTF"), 0, $out, 0, 4)
    [Array]::Copy([BitConverter]::GetBytes([uint32]$version), 0, $out, 4, 4)
    [Array]::Copy([BitConverter]::GetBytes([uint32]$newLength), 0, $out, 8, 4)
    [Array]::Copy([BitConverter]::GetBytes([uint32]$newJsonBytes.Length), 0, $out, 12, 4)
    [Array]::Copy([Text.Encoding]::ASCII.GetBytes("JSON"), 0, $out, 16, 4)
    [Array]::Copy($newJsonBytes, 0, $out, 20, $newJsonBytes.Length)
    [Array]::Copy($binBytes, 0, $out, 20 + $newJsonBytes.Length, $binBytes.Length)
    [IO.File]::WriteAllBytes($File.FullName, $out)

    return $true
}

$models = Read-VpkDirectoryEntries -Path $WorkshopVpk |
    Where-Object { $_ -like "*.vmdl_c" } |
    Sort-Object -Unique

Write-Host "Found $($models.Count) model resources."

$exported = 0
$skipped = 0
$failed = New-Object System.Collections.Generic.List[string]

for ($i = 0; $i -lt $models.Count; $i++) {
    $model = $models[$i]
    $safeFolder = Get-SafeFolderName -ModelPath $model
    $outDir = Join-Path $exportsRoot $safeFolder
    $existing = Get-ChildItem -Path $outDir -Recurse -Filter *.glb -ErrorAction SilentlyContinue |
        Where-Object { $_.BaseName -notlike "*_physics" } |
        Select-Object -First 1

    if ($existing -and !$Force) {
        $skipped++
        continue
    }

    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    Write-Host ("[{0}/{1}] Exporting {2}" -f ($i + 1), $models.Count, $model)

    $stdoutPath = Join-Path ([IO.Path]::GetTempPath()) ("s2v-out-" + [Guid]::NewGuid() + ".txt")
    $stderrPath = Join-Path ([IO.Path]::GetTempPath()) ("s2v-err-" + [Guid]::NewGuid() + ".txt")

    try {
        $process = Start-Process -FilePath $CliPath `
            -ArgumentList @("-i", $WorkshopVpk, "-o", $outDir, "-d", "--gltf_export_format", "glb", "--gltf_export_materials", "-f", $model) `
            -NoNewWindow `
            -Wait `
            -PassThru `
            -RedirectStandardOutput $stdoutPath `
            -RedirectStandardError $stderrPath

        $glb = Get-ChildItem -Path $outDir -Recurse -Filter *.glb -ErrorAction SilentlyContinue |
            Where-Object { $_.BaseName -notlike "*_physics" } |
            Select-Object -First 1

        if ($glb) {
            $exported++
        } else {
            $errorText = if (Test-Path -LiteralPath $stderrPath) { Get-Content -LiteralPath $stderrPath -Raw } else { "" }
            if ([string]::IsNullOrWhiteSpace($errorText) -and (Test-Path -LiteralPath $stdoutPath)) {
                $errorText = Get-Content -LiteralPath $stdoutPath -Raw
            }
            $failed.Add("$model :: exit $($process.ExitCode) $($errorText.Trim())")
        }
    } catch {
        $failed.Add("$model :: $($_.Exception.Message)")
    } finally {
        Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
    }
}

Get-ChildItem -Path $exportsRoot -Recurse -Filter "*_physics.glb" -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue

$patched = 0
foreach ($glb in Get-ChildItem -Path $exportsRoot -Recurse -Filter *.glb) {
    if (Patch-GlbForBrowserPreview -File $glb) {
        $patched++
    }
}

$manifest = New-Object System.Collections.Generic.List[object]
foreach ($model in $models) {
    $safeFolder = Get-SafeFolderName -ModelPath $model
    $outDir = Join-Path $exportsRoot $safeFolder
    $glb = Get-ChildItem -Path $outDir -Recurse -Filter *.glb -ErrorAction SilentlyContinue |
        Where-Object { $_.BaseName -notlike "*_physics" } |
        Select-Object -First 1

    if (!$glb) {
        continue
    }

    $sourceModel = Convert-ToPreviewLabel -ModelPath $model
    $relative = $glb.FullName.Substring($PreviewRoot.Length + 1).Replace([IO.Path]::DirectorySeparatorChar, "/")
    $parts = $sourceModel.Split("/")
    $name = [IO.Path]::GetFileNameWithoutExtension($parts[-1])
    $group = if ($parts.Length -gt 2) { ($parts[0..([Math]::Min($parts.Length - 2, 3))] -join "/") } else { $parts[0] }

    $manifest.Add([pscustomobject]@{
        name = $name
        label = $sourceModel
        group = $group
        sourceModel = $sourceModel
        src = $relative
        fullPath = $glb.FullName
    })
}

$manifest |
    Sort-Object label |
    ConvertTo-Json -Depth 8 |
    Set-Content -Path (Join-Path $PreviewRoot "manifest.json") -Encoding UTF8

Write-Host "Exported: $exported | Skipped existing: $skipped | Patched GLBs: $patched | Manifest: $($manifest.Count)"

if ($failed.Count -gt 0) {
    Write-Host "Failed exports:"
    $failed | ForEach-Object { Write-Host $_ }
}
