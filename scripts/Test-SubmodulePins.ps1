[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$expected = [ordered]@{
    'thirdparty/FishGfx' = '0e543f59b50c633a05b92fe52d317e79cf4fb9f0'
    'thirdparty/FishGfx/thirdparty/FishUI' = '9395864c8d3528430bf602a960cf4ee0e41f802a'
    'thirdparty/miniaudio' = '9634bedb5b5a2ca38c1ee7108a9358a4e233f14d'
}

foreach ($entry in $expected.GetEnumerator()) {
    $submodulePath = Join-Path $repositoryRoot $entry.Key
    if (-not (Test-Path -LiteralPath (Join-Path $submodulePath '.git'))) {
        throw "Submodule '$($entry.Key)' is not initialized. Run git submodule update --init --recursive."
    }

    $actual = (& git -C $submodulePath rev-parse HEAD 2>&1).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to read submodule '$($entry.Key)': $actual"
    }
    if ($actual -ne $entry.Value) {
        throw "Submodule '$($entry.Key)' is at $actual; expected $($entry.Value). Do not use submodule update --remote."
    }

    Write-Host "Verified $($entry.Key) at $actual"
}

$moduleFiles = @(
    (Join-Path $repositoryRoot '.gitmodules'),
    (Join-Path $repositoryRoot 'thirdparty/FishGfx/.gitmodules')
)

foreach ($moduleFile in $moduleFiles) {
    if (-not (Test-Path -LiteralPath $moduleFile)) {
        continue
    }

    $branchSettings = & git config -f $moduleFile --get-regexp '^submodule\..*\.branch$' 2>$null
    if ($LASTEXITCODE -eq 0 -and $branchSettings) {
        throw "Branch tracking is forbidden in '$moduleFile': $branchSettings"
    }
}

Write-Host 'All recursive submodule revisions are pinned and branch tracking is disabled.'
