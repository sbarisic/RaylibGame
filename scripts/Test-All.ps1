[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',

    [switch] $CleanRuntimeData,

    [switch] $IncludeFishGfx
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$artifactRoot = Join-Path $repositoryRoot "artifacts/validation/$timestamp"
$runtimeRoot = Join-Path $artifactRoot 'runtime'
$publishRoot = Join-Path $artifactRoot 'publish'
$stateRoot = Join-Path $artifactRoot 'repository-state'

New-Item -ItemType Directory -Path $artifactRoot, $runtimeRoot, $publishRoot, $stateRoot -Force | Out-Null

function Invoke-Checked {
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][scriptblock] $Command,
        [switch] $RequireNativeSuccess
    )

    Write-Host "== $Name =="
    $global:LASTEXITCODE = 0
    $output = & $Command 2>&1
    $exitCode = $LASTEXITCODE
    $output | Tee-Object -FilePath (Join-Path $artifactRoot "$Name.log")
    if ($RequireNativeSuccess -and $exitCode -ne 0) {
        throw "$Name failed with exit code $exitCode."
    }
}

function Get-RepositoryPaths {
    $paths = [System.Collections.Generic.List[string]]::new()
    $paths.Add($repositoryRoot)

    $status = & git -C $repositoryRoot submodule status --recursive 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to enumerate recursive submodules.'
    }

    foreach ($line in $status) {
        if ($line -match '^.[0-9a-fA-F]+\s+([^\s]+)') {
            $paths.Add((Join-Path $repositoryRoot $Matches[1]))
        }
    }

    return $paths
}

function Write-RepositorySnapshot {
    param(
        [Parameter(Mandatory)][string] $Destination
    )

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    $repositories = @(Get-RepositoryPaths)
    $manifest = [System.Collections.Generic.List[string]]::new()

    for ($repositoryIndex = 0; $repositoryIndex -lt $repositories.Count; $repositoryIndex++) {
        $path = $repositories[$repositoryIndex]
        $name = ('repo-{0:D2}' -f $repositoryIndex)
        $relative = [System.IO.Path]::GetRelativePath($repositoryRoot, $path).Replace('\', '/')
        $manifest.Add("$name`t$relative")

        & git -C $path rev-parse HEAD 2>$null | Set-Content -LiteralPath (Join-Path $Destination "$name.head") -Encoding utf8NoBOM
        & git -C $path diff --binary --no-ext-diff 2>$null | Set-Content -LiteralPath (Join-Path $Destination "$name.unstaged.diff") -Encoding utf8NoBOM
        & git -C $path diff --cached --binary --no-ext-diff 2>$null | Set-Content -LiteralPath (Join-Path $Destination "$name.staged.diff") -Encoding utf8NoBOM
        & git -C $path ls-files -s 2>$null | Set-Content -LiteralPath (Join-Path $Destination "$name.index") -Encoding utf8NoBOM

        $hashes = [System.Collections.Generic.List[string]]::new()
        $tracked = @(& git -C $path ls-files 2>$null)
        foreach ($trackedPath in ($tracked | Sort-Object -CaseSensitive)) {
            $fullPath = Join-Path $path $trackedPath
            if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
                $hash = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash
                $hashes.Add("$hash`t$trackedPath")
            }
            else {
                $hashes.Add("MISSING`t$trackedPath")
            }
        }
        $hashes | Set-Content -LiteralPath (Join-Path $Destination "$name.hashes") -Encoding utf8NoBOM
    }

    $manifest | Set-Content -LiteralPath (Join-Path $Destination 'repositories.txt') -Encoding utf8NoBOM
}

function Assert-SnapshotsEqual {
    param(
        [Parameter(Mandatory)][string] $Before,
        [Parameter(Mandatory)][string] $After
    )

    $beforeFiles = @(Get-ChildItem -LiteralPath $Before -File | Sort-Object Name)
    $afterFiles = @(Get-ChildItem -LiteralPath $After -File | Sort-Object Name)
    if (($beforeFiles.Name -join "`n") -cne ($afterFiles.Name -join "`n")) {
        throw 'Validation changed the recursive repository layout.'
    }

    foreach ($beforeFile in $beforeFiles) {
        $afterFile = Join-Path $After $beforeFile.Name
        $beforeHash = (Get-FileHash -LiteralPath $beforeFile.FullName -Algorithm SHA256).Hash
        $afterHash = (Get-FileHash -LiteralPath $afterFile -Algorithm SHA256).Hash
        if ($beforeHash -cne $afterHash) {
            throw "Validation changed tracked repository state: $($beforeFile.Name)"
        }
    }
}

$beforeState = Join-Path $stateRoot 'before'
$afterState = Join-Path $stateRoot 'after'
Write-RepositorySnapshot -Destination $beforeState

try {
    # Every run already owns a newly created timestamped runtime directory.
    # The switch documents that callers require this isolation and is retained
    # for command-line compatibility with local validation workflows.
    if ($CleanRuntimeData) {
        Write-Host "Using clean isolated runtime data: $runtimeRoot"
    }

    Push-Location $repositoryRoot
    try {
        Invoke-Checked 'submodule-pins' { & (Join-Path $PSScriptRoot 'Test-SubmodulePins.ps1') }
        Invoke-Checked 'assets' { & (Join-Path $PSScriptRoot 'Test-Assets.ps1') }
        Invoke-Checked 'tests' { dotnet test RaylibGame.sln -c $Configuration -p:Platform=x64 --nologo } -RequireNativeSuccess

        if ($IncludeFishGfx) {
            Invoke-Checked 'fishgfx-tests' {
                dotnet test thirdparty/FishGfx/FishGfx.Modern.sln -c $Configuration --nologo
            } -RequireNativeSuccess
        }

        $clientPublish = Join-Path $publishRoot 'client'
        $serverPublish = Join-Path $publishRoot 'server'
        Invoke-Checked 'publish-client' {
            dotnet publish Voxelgine/Voxelgine.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained false -o $clientPublish --nologo
        } -RequireNativeSuccess
        Invoke-Checked 'publish-server' {
            dotnet publish VoxelgineServer/VoxelgineServer.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained false -o $serverPublish --nologo
        } -RequireNativeSuccess
        Invoke-Checked 'publish-layout-client' { & (Join-Path $PSScriptRoot 'Test-PublishLayout.ps1') -Kind Client -Path $clientPublish }
        Invoke-Checked 'publish-layout-server' { & (Join-Path $PSScriptRoot 'Test-PublishLayout.ps1') -Kind Server -Path $serverPublish }

        $automaticModes = @(
            '--fishgfx-auto-menu-options',
            '--fishgfx-auto-menu-host',
            '--fishgfx-auto-menu-join',
            '--fishgfx-auto-menu-developer',
            '--fishgfx-auto-gameplay',
            '--fishgfx-auto-transition',
            '--fishgfx-auto-npc',
            '--fishgfx-auto-effects',
			'--fishgfx-auto-voxel-material',
			'--fishgfx-auto-world-preview',
			'--fishgfx-auto-ceramic-fish-village-lab'
        )
        foreach ($mode in $automaticModes) {
            $safeName = $mode.TrimStart('-').Replace('-', '_')
            $modeRuntime = Join-Path $runtimeRoot $safeName
            New-Item -ItemType Directory -Path $modeRuntime -Force | Out-Null
            Invoke-Checked "smoke-$safeName" {
                & (Join-Path $clientPublish 'Voxelgine.exe') $mode --data-root $modeRuntime
            } -RequireNativeSuccess
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    Write-RepositorySnapshot -Destination $afterState
    Assert-SnapshotsEqual -Before $beforeState -After $afterState
}

Write-Host "Validation succeeded. Results: $artifactRoot"
