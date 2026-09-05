#requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory)][ValidatePattern('^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$')][string] $Version)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) {
    $candidates = @(
        (Get-Command pwsh.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source)
        "$env:ProgramW6432/PowerShell/7/pwsh.exe"
        "$env:ProgramFiles/PowerShell/7/pwsh.exe"
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -Unique
    $powerShell7 = $null
    foreach ($candidate in $candidates) {
        $major = & $candidate -NoProfile -Command '$PSVersionTable.PSVersion.Major'
        if ($LASTEXITCODE -eq 0 -and "$major" -match '^\d+$' -and [int]$major -ge 7) { $powerShell7 = $candidate; break }
    }
    if (!$powerShell7) { throw 'Packaging requires PowerShell 7. Install it with: winget install --id Microsoft.PowerShell --source winget. Then rerun this command.' }
    Write-Host 'Running packaging with PowerShell 7...'
    & $powerShell7 -NoProfile -File $PSCommandPath -Version $Version
    if ($LASTEXITCODE -ne 0) { throw "Packaging failed in PowerShell 7 (exit code $LASTEXITCODE). See the error above." }
    return
}
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo
$output = Join-Path $repo 'artifacts/releases'
$zip = Join-Path $output "AuroraFalls-$Version-win-x64.zip"
if (Test-Path -LiteralPath $zip) { throw "Refusing to overwrite $zip. Remove your local artifact explicitly if rebuilding an unpublished version." }
& "$PSScriptRoot/Test-SubmodulePins.ps1"
$work = Join-Path $repo "artifacts/release-work/$Version-$([guid]::NewGuid().ToString('N'))"
$package = Join-Path $work 'package'
$payload = Join-Path $package "versions/$Version"
$game = Join-Path $payload 'game'
$updater = Join-Path $payload 'updater'
New-Item -ItemType Directory -Path $package -Force | Out-Null
$sourceCommit = & git rev-parse HEAD
if ($LASTEXITCODE) { throw 'Cannot identify the source commit.' }
$sourceChanges = @(& git status --porcelain --untracked-files=normal)
if ($LASTEXITCODE) { throw 'Cannot inspect source changes.' }
$submodules = @(& git submodule status --recursive)
if ($LASTEXITCODE) { throw 'Cannot identify dependency commits.' }
@{ version = $Version; sourceCommit = $sourceCommit; sourceDirty = ($sourceChanges.Count -gt 0); builtAtUtc = [DateTime]::UtcNow.ToString('o'); submodules = $submodules } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$package/build-info.json" -Encoding utf8NoBOM
# Fresh intermediates prevent a package from reusing normal development build outputs.
$common = @('-c','Release','-r','win-x64','--self-contained','true','--artifacts-path',"$work/build",'-p:Platform=x64','-p:PublishTrimmed=false',"-p:Version=$($Version.Substring(1))")
& dotnet publish Voxelgine/Voxelgine.csproj @common '-p:ReleasePackage=true' '-p:PublishSingleFile=false' -o $game
if ($LASTEXITCODE) { throw 'Game publish failed.' }
& dotnet publish AuroraFalls.Updater/AuroraFalls.Updater.csproj @common '-p:PublishSingleFile=false' -o $updater
if ($LASTEXITCODE) { throw 'Updater publish failed.' }
& dotnet publish AuroraFalls.Bootstrap/AuroraFalls.Bootstrap.csproj @common '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' -o "$work/bootstrap"
if ($LASTEXITCODE) { throw 'Bootstrap publish failed.' }
Copy-Item -LiteralPath "$work/bootstrap/AuroraFalls.exe" -Destination $package
# Debug symbols are useful locally but unnecessary in the tester download.
Get-ChildItem -LiteralPath $payload -Filter '*.pdb' -Recurse -File | Remove-Item
& "$PSScriptRoot/Test-NativeDependencies.ps1" -Path $game -CopyRuntime
& "$PSScriptRoot/Test-NativeDependencies.ps1" -Path $updater -CopyRuntime
$files = [ordered]@{}
Get-ChildItem -LiteralPath $payload -Recurse -File | Sort-Object FullName | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($payload, $_.FullName).Replace('\','/')
    $files[$relative] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
}
@{ schemaVersion = 1; bootstrapProtocol = 1; version = $Version; platform = 'win-x64'; files = $files } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$payload/manifest.json" -Encoding utf8NoBOM
@{ schemaVersion = 1; current = $Version; previous = $null } |
    ConvertTo-Json | Set-Content -LiteralPath "$package/current.json" -Encoding utf8NoBOM
Copy-Item -LiteralPath "$repo/RELEASE.md" -Destination $package
& "$PSScriptRoot/Test-PublishLayout.ps1" -Kind Release -Path $package
New-Item -ItemType Directory -Force -Path $output | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($package, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath "$zip.sha256" -Encoding ascii
Write-Host "Package: $zip"
Write-Host "Extracted package for local testing: $package"
