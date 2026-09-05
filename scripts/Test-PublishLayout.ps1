[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Client', 'Server', 'Release')]
    [string] $Kind,

    [Parameter(Mandatory)]
    [string] $Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
    throw "Publish directory '$Path' does not exist."
}

$publishRoot = (Resolve-Path -LiteralPath $Path).Path

if ($Kind -eq 'Release') {
    $pointer = Get-Content -LiteralPath (Join-Path $publishRoot 'current.json') -Raw | ConvertFrom-Json
    if ($pointer.schemaVersion -ne 1 -or $pointer.current -notmatch '^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') { throw 'Invalid release pointer.' }
    $versionRoot = Join-Path $publishRoot "versions/$($pointer.current)"
    $manifest = Get-Content -LiteralPath "$versionRoot/manifest.json" -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.bootstrapProtocol -ne 1 -or $manifest.version -ne $pointer.current -or $manifest.platform -ne 'win-x64') { throw 'Invalid release manifest.' }
    foreach ($required in @('AuroraFalls.exe', "versions/$($pointer.current)/updater/AuroraFalls.Updater.exe", "versions/$($pointer.current)/updater/coreclr.dll", "versions/$($pointer.current)/game/coreclr.dll")) {
        if (!(Test-Path -LiteralPath (Join-Path $publishRoot $required) -PathType Leaf)) { throw "Release missing $required" }
    }
    $actual = @(Get-ChildItem -LiteralPath $versionRoot -Recurse -File | Where-Object { $_.FullName -ne [IO.Path]::GetFullPath("$versionRoot/manifest.json") })
    $inventory = @($manifest.files.PSObject.Properties)
    if ($actual.Count -ne $inventory.Count) { throw 'Release inventory file count mismatch.' }
    foreach ($file in $inventory) {
        if ($file.Name -notmatch '^(game|updater)/' -or $file.Name -match '\.\.|\\|:') { throw 'Unsafe manifest path.' }
        if ((Get-FileHash -LiteralPath (Join-Path $versionRoot $file.Name) -Algorithm SHA256).Hash -ne $file.Value) { throw "Release checksum mismatch: $($file.Name)" }
    }
    & $PSCommandPath -Kind Client -Path "$versionRoot/game"
    & "$PSScriptRoot/Test-NativeDependencies.ps1" -Path "$versionRoot/game"
    & "$PSScriptRoot/Test-NativeDependencies.ps1" -Path "$versionRoot/updater"
    Write-Host "Release $($pointer.current) verified."
    return
}

function Assert-File {
    param([Parameter(Mandatory)][string] $RelativePath)

    $fullPath = Join-Path $publishRoot $RelativePath
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Required $Kind publish file is missing: $RelativePath"
    }
}

function Assert-AbsentFile {
    param([Parameter(Mandatory)][string] $RelativePath)

    $fullPath = Join-Path $publishRoot $RelativePath
    if (Test-Path -LiteralPath $fullPath) {
        throw "Unexpected $Kind publish file is present: $RelativePath"
    }
}

if ($Kind -eq 'Client') {
    $requiredFiles = @(
        'Voxelgine.dll',
        'VoxelgineEngine.dll',
        'Voxelgine.Audio.dll',
        'FishGfx.dll',
        'FishGfx.FishUI.dll',
        'FishUI.dll',
        'glfw3.dll',
        'VoxelAudioNative.dll',
        'data/default3d.vert',
        'data/shaders/voxel.vert',
        'data/shaders/gui.vert',
        'data/shaders/fishgfx/scene_post.vert',
        'data/shaders/fishgfx/scene_post.frag',
        'data/themes/gwen.yaml',
        'data/audio/audio-bank.json',
        'data/models/npc/humanoid.json',
        'data/models/orb_xp/orb_xp.obj',
        'data/textures/atlas.png'
    )

    foreach ($requiredFile in $requiredFiles) {
        Assert-File $requiredFile
    }

    $forbiddenClientPatterns = @(
        'Raylib*.dll',
        'OpenAL*.dll',
        'phonon*.dll'
    )
    $clientFiles = @(Get-ChildItem -LiteralPath $publishRoot -Recurse -File)

    function Test-ManifestPattern([string] $relativePath, [string] $pattern) {
        $regex = '^' + [regex]::Escape($pattern).Replace('\*\*', '.*').Replace('\*', '[^/]*') + '$'
        return [regex]::IsMatch($relativePath, $regex, [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
    }

    $forbiddenManifest = Join-Path $PSScriptRoot 'forbidden-assets.txt'
    $forbiddenAssetPatterns = Get-Content -LiteralPath $forbiddenManifest |
        Where-Object { $_ -and -not $_.StartsWith('#', [StringComparison]::Ordinal) }
    foreach ($file in $clientFiles) {
        $relativePath = [System.IO.Path]::GetRelativePath($publishRoot, $file.FullName).Replace('\', '/')
        if ($forbiddenAssetPatterns | Where-Object { Test-ManifestPattern $relativePath $_ }) {
            throw "Client publish contains forbidden content: $relativePath"
        }
    }
    foreach ($pattern in $forbiddenClientPatterns) {
        $matches = @($clientFiles | Where-Object Name -Like $pattern)
        if ($matches.Count -gt 0) {
            $relativeMatches = $matches | ForEach-Object {
                [System.IO.Path]::GetRelativePath($publishRoot, $_.FullName)
            }
            throw "Client publish contains forbidden '$pattern' files: $($relativeMatches -join ', ')"
        }
    }

    $ambienceLoops = @(
        'data/sound/birds/birds_loop.flac',
        'data/sound/fire/burning_loop.flac',
        'data/sound/rain/rain_inside1_loop.flac',
        'data/sound/rain/rain_inside2_loop.flac',
        'data/sound/rain/rain_inside3_loop.flac',
        'data/sound/rain/rain_outside1_loop.flac',
        'data/sound/rain/rain_outside2_loop.flac',
        'data/sound/rain/rain_outside3_loop.flac',
        'data/sound/wind/wind_loop.flac',
        'data/sound/wind/wind2_loop.flac'
    )

    foreach ($loop in $ambienceLoops) {
        Assert-File $loop
        Assert-AbsentFile ([System.IO.Path]::ChangeExtension($loop, '.wav'))
    }

    $publishedFlacs = @(Get-ChildItem -LiteralPath (Join-Path $publishRoot 'data/sound') -Recurse -File -Filter '*.flac')
    if ($publishedFlacs.Count -ne $ambienceLoops.Count) {
        throw "Expected exactly $($ambienceLoops.Count) FLAC ambience loops; found $($publishedFlacs.Count)."
    }

    $bankPath = Join-Path $publishRoot 'data/audio/audio-bank.json'
    $bank = Get-Content -LiteralPath $bankPath -Raw | ConvertFrom-Json
    $requiredCueIds = @(
        'ambience.wind',
        'ambience.birds',
        'ambience.underwater',
        'ambience.campfire',
        'ambience.rain.inside',
        'ambience.rain.outside'
    )
    $publishedCueIds = @($bank.cues | ForEach-Object id)
    foreach ($cueId in $requiredCueIds) {
        if ($cueId -notin $publishedCueIds) {
            throw "Required audio cue '$cueId' is missing from the published bank."
        }
    }

    $bankDirectory = Split-Path -Parent $bankPath
    foreach ($cue in $bank.cues) {
        foreach ($variant in $cue.variants) {
            $assetPath = [System.IO.Path]::GetFullPath(
                [string] $variant.path,
                $bankDirectory)
            if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) {
                throw "Audio cue '$($cue.id)' references a missing published asset: $($variant.path)"
            }
        }
    }

    Write-Host "Client publish layout is complete ($($publishedFlacs.Count) streamed FLAC loops)."
    return
}

Assert-File 'VoxelgineServer.dll'
Assert-File 'VoxelgineEngine.dll'

$forbiddenPatterns = @(
    'Voxelgine.dll',
    'Voxelgine.Audio.dll',
    'FishGfx*.dll',
    'FishUI*.dll',
    'glfw3.dll',
    'Raylib*.dll',
    'VoxelAudioNative.dll',
    'OpenAL*.dll',
    'phonon*.dll'
)

$publishedFiles = @(Get-ChildItem -LiteralPath $publishRoot -Recurse -File)
foreach ($pattern in $forbiddenPatterns) {
    $matches = @($publishedFiles | Where-Object Name -Like $pattern)
    if ($matches.Count -gt 0) {
        $relativeMatches = $matches | ForEach-Object {
            [System.IO.Path]::GetRelativePath($publishRoot, $_.FullName)
        }
        throw "Server publish contains forbidden '$pattern' files: $($relativeMatches -join ', ')"
    }
}

$dependencyManifest = Join-Path $publishRoot 'VoxelgineServer.deps.json'
Assert-File 'VoxelgineServer.deps.json'
$dependencyText = Get-Content -LiteralPath $dependencyManifest -Raw
$forbiddenDependencyPattern = '(?i)FishGfx|FishUI|Raylib|Voxelgine\.Audio|Silk\.NET\.OpenGL|OpenAL|phonon|miniaudio'
$dependencyMatches = @(
    [regex]::Matches($dependencyText, $forbiddenDependencyPattern) |
        ForEach-Object Value |
        Sort-Object -Unique
)
if ($dependencyMatches.Count -gt 0) {
    throw "Server dependency manifest contains forbidden client dependencies: $($dependencyMatches -join ', ')"
}

Write-Host 'Server publish is portable and contains no client graphics or audio artifacts.'
