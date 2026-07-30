[CmdletBinding()]
param(
    [string] $ContentRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ContentRoot)) {
    $ContentRoot = Join-Path $repositoryRoot 'Voxelgine/data'
}
$ContentRoot = [System.IO.Path]::GetFullPath($ContentRoot)

$allowedExtensions = @('.png', '.json', '.vert', '.frag', '.ttf', '.otf', '.wav', '.flac', '.obj', '.mtl', '.lua')
$required = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'required-assets.txt') |
    Where-Object { $_ -and -not $_.StartsWith('#', [StringComparison]::Ordinal) }
$dynamic = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'dynamic-assets.txt') |
    Where-Object { $_ -and -not $_.StartsWith('#', [StringComparison]::Ordinal) }
$forbidden = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'forbidden-assets.txt') |
    Where-Object { $_ -and -not $_.StartsWith('#', [StringComparison]::Ordinal) }

function Convert-ToDataPath([string] $path) {
    return ('data/' + [System.IO.Path]::GetRelativePath($ContentRoot, $path).Replace('\', '/'))
}

function Test-Pattern([string] $path, [string] $pattern) {
    $regex = '^' + [regex]::Escape($pattern).Replace('\*\*', '.*').Replace('\*', '[^/]*') + '$'
    return [regex]::IsMatch($path, $regex, [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
}

$files = @(Get-ChildItem -LiteralPath $ContentRoot -Recurse -File)
$paths = @($files | ForEach-Object { Convert-ToDataPath $_.FullName })
$pathSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($path in $paths) { [void]$pathSet.Add($path) }

foreach ($file in $files) {
    $path = Convert-ToDataPath $file.FullName
    if ($file.Extension -notin $allowedExtensions) {
        if (-not ($forbidden | Where-Object { Test-Pattern $path $_ })) {
            throw "Asset extension has no deployment rule: $path"
        }
    }
}

$forbiddenRuntimeState = @(
    'data/config.json',
    'data/map.bin',
    'data/player.bin',
    'data/players/**',
    'data/FishUISamples/**'
)
foreach ($path in $paths) {
    if ($forbiddenRuntimeState | Where-Object { Test-Pattern $path $_ }) {
        throw "Runtime or sample content remains under the immutable data root: $path"
    }
}

foreach ($path in $required) {
    if (-not $pathSet.Contains($path)) {
        throw "Required asset is missing or has incorrect path case: $path"
    }
}

$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'Voxelgine') -Recurse -File -Include '*.cs', '*.json')
$referencePattern = [regex]'(?<path>data[\\/][A-Za-z0-9_./\\-]+\.(?:png|json|vert|frag|ttf|otf|wav|flac|obj|mtl|lua))'
$references = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($sourceFile in $sourceFiles) {
    $text = Get-Content -LiteralPath $sourceFile.FullName -Raw
    foreach ($match in $referencePattern.Matches($text)) {
        [void]$references.Add($match.Groups['path'].Value.Replace('\', '/'))
    }
}

function Add-RelativeDependency([string] $ownerPath, [string] $relativePath) {
    $fullPath = [System.IO.Path]::GetFullPath($relativePath, (Split-Path -Parent $ownerPath))
    if (-not $fullPath.StartsWith($ContentRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Asset dependency escapes the immutable data root: $ownerPath -> $relativePath"
    }
    $dataPath = Convert-ToDataPath $fullPath
    [void]$references.Add($dataPath)
    if (-not $pathSet.Contains($dataPath)) {
        throw "Asset dependency is missing or has incorrect path case: $dataPath"
    }
}

$audioBankPath = Join-Path $ContentRoot 'audio/audio-bank.json'
if (Test-Path -LiteralPath $audioBankPath) {
    $audioBank = Get-Content -LiteralPath $audioBankPath -Raw | ConvertFrom-Json
    foreach ($cue in $audioBank.cues) {
        foreach ($variant in $cue.variants) {
            Add-RelativeDependency $audioBankPath ([string]$variant.path)
        }
    }
}

$referencedMaterials = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$referencedObjectPaths = @($references + $required | Where-Object { $_.EndsWith('.obj', [StringComparison]::OrdinalIgnoreCase) } | Sort-Object -Unique)
foreach ($objectPath in $referencedObjectPaths) {
    $obj = Get-Item -LiteralPath (Join-Path $ContentRoot $objectPath.Substring('data/'.Length))
    foreach ($line in Get-Content -LiteralPath $obj.FullName) {
        if ($line -match '^\s*mtllib\s+(.+?)\s*$') {
            Add-RelativeDependency $obj.FullName $Matches[1]
            [void]$referencedMaterials.Add(
                [System.IO.Path]::GetFullPath($Matches[1], (Split-Path -Parent $obj.FullName)))
        }
    }
}

foreach ($materialPath in $referencedMaterials) {
    foreach ($line in Get-Content -LiteralPath $materialPath) {
        if ($line -match '^\s*map_[A-Za-z0-9_]+\s+(.+?)\s*$') {
            Add-RelativeDependency $materialPath $Matches[1]
        }
    }
}

foreach ($reference in $references) {
    if ($pathSet.Contains($reference)) { continue }
    if ($dynamic | Where-Object { Test-Pattern $reference $_ }) { continue }
    throw "Runtime asset reference is unresolved: $reference"
}

$unreferenced = @($paths | Where-Object {
    $path = $_
    -not $references.Contains($path) -and
    $path -notin $required -and
    -not ($dynamic | Where-Object { Test-Pattern $path $_ })
})
if ($unreferenced.Count -gt 0) {
    Write-Warning "Allowlisted assets without a discovered runtime reference:`n$($unreferenced -join "`n")"
}

Write-Host "Asset audit passed: $($files.Count) immutable content files, $($references.Count) direct references."
