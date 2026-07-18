[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Client', 'Server')]
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
