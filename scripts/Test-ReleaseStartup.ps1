#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string] $Path)
$ErrorActionPreference = 'Stop'
$package = (Resolve-Path -LiteralPath $Path).Path
$pointer = Get-Content -LiteralPath (Join-Path $package 'current.json') -Raw | ConvertFrom-Json
if ($pointer.current -notmatch '^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') { throw 'Invalid package version.' }
$artifactRoot = Join-Path (Split-Path $PSScriptRoot -Parent) "artifacts/release-smoke/$([guid]::NewGuid().ToString('N'))"
foreach ($mode in @('launcher', 'offline', 'direct')) {
    $runtime = Join-Path $artifactRoot "$mode runtime ž"
    $executable = if ($mode -eq 'direct') { Join-Path $package "versions/$($pointer.current)/game/Voxelgine.exe" } else { Join-Path $package 'AuroraFalls.exe' }
    $start = [Diagnostics.ProcessStartInfo]::new($executable)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.WorkingDirectory = $package
    foreach ($argument in @('--fishgfx-auto', '--data-root', $runtime)) { $start.ArgumentList.Add($argument) }
    if ($mode -ne 'launcher') {
        # Only this child tree gets an unreachable proxy, simulating GitHub being offline.
        $start.Environment['HTTPS_PROXY'] = 'http://127.0.0.1:1'
        $start.Environment['NO_PROXY'] = ''
    }
    $start.Environment.Remove('AURORAFALLS_GAME_CHILD') | Out-Null
    $start.Environment.Remove('AURORAFALLS_INSTALL_ROOT') | Out-Null
    $start.Environment.Remove('AURORAFALLS_BOOTSTRAP_PID') | Out-Null
    $process = [Diagnostics.Process]::Start($start)
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(45)
        $log = Join-Path $runtime 'logs/console.log'
        do {
            Start-Sleep -Milliseconds 200
            $text = if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Raw } else { '' }
        } until ($text -match 'Client shutdown complete\.' -or [DateTime]::UtcNow -gt $deadline)
        if ($text -notmatch 'FishGfx automatic render validation passed\.' -or $text -notmatch 'Client shutdown complete\.') {
            throw "$mode startup smoke failed. See $log; initial process ID $($process.Id)."
        }
        if (!$process.WaitForExit(5000)) { throw "$mode process did not exit after game shutdown: $($process.Id)." }
        Write-Host "$mode startup passed; isolated runtime: $runtime"
    } finally { $process.Dispose() }
}
Write-Host 'Packaged launcher, offline fallback, direct-executable redirection, and argument forwarding passed.'
