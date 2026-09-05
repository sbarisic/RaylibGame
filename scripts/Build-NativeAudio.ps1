[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Configuration,
    [string] $CMakeExecutable,
    [Parameter(Mandatory)][string] $OutputFile,
    [Parameter(Mandatory)][string] $BuildDirectory,
    [switch] $ResolveOnly
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$source = Join-Path $repo 'native/VoxelAudioNative'
$miniaudio = Join-Path $repo 'thirdparty/miniaudio'
if (!(Test-Path "$miniaudio/miniaudio.c")) { throw 'Missing miniaudio. Run git submodule update --init --recursive.' }
$vswhere = "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe"
if (!(Test-Path $vswhere)) { throw 'Install Visual Studio with Desktop development with C++ and a Windows SDK.' }
$instances = & $vswhere -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -format json | ConvertFrom-Json
$vs = $instances | Where-Object { ([version]$_.installationVersion).Major -in 17,18 } | Sort-Object { [version]$_.installationVersion } -Descending | Select-Object -First 1
if (!$vs) { throw 'Install Visual Studio 2022 or 2026 with Desktop development with C++ and a Windows SDK.' }
$major = ([version]$vs.installationVersion).Major
$generator = if ($major -eq 18) { 'Visual Studio 18 2026' } else { 'Visual Studio 17 2022' }
$minimum = if ($major -eq 18) { [version]'4.2' } else { [version]'3.24' }
$candidates = if ($CMakeExecutable) { @($CMakeExecutable) } else {
    @((Get-Command cmake.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source),
      (Join-Path $vs.installationPath 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'))
}
$cmake = $null
foreach ($candidate in $candidates) {
    if (!$candidate -or !(Test-Path -LiteralPath $candidate)) { continue }
    $versionText = (& $candidate --version) -join "`n"
    if ($LASTEXITCODE -eq 0 -and $versionText -match 'cmake version (\d+\.\d+\.\d+)' -and [version]$Matches[1] -ge $minimum) { $cmake = $candidate; break }
}
if (!$cmake) { throw "CMake $minimum or newer is required for $generator. Install the Visual Studio C++ CMake tools, add CMake to PATH, or set -p:CMakeExecutable=<full path>." }
$toolchain = Get-ChildItem (Join-Path $vs.installationPath 'VC/Tools/MSVC') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$build = Join-Path $BuildDirectory "toolchains/vs$major-$($vs.instanceId)-$($toolchain.Name)"
if ($ResolveOnly) {
    New-Item -ItemType Directory -Force -Path $BuildDirectory | Out-Null
    $stamp = Join-Path $BuildDirectory 'native-toolchain.json'
    $resolved = [ordered]@{ cmake = $cmake; cmakeVersion = $versionText; generator = $generator; instance = $vs.installationPath; toolchain = $toolchain.Name; configuration = $Configuration } | ConvertTo-Json
    if (!(Test-Path -LiteralPath $stamp) -or [IO.File]::ReadAllText($stamp) -ne $resolved) { [IO.File]::WriteAllText($stamp, $resolved) }
    return
}
& $cmake -S $source -B $build -G $generator -A x64 "-DCMAKE_GENERATOR_INSTANCE=$($vs.installationPath)" "-DMINIAUDIO_ROOT=$miniaudio"
if ($LASTEXITCODE) { throw 'Native audio configuration failed. Check the CMake errors above and install the MSVC x64 compiler and Windows SDK.' }
& $cmake --build $build --config $Configuration --target VoxelAudioNative --parallel
if ($LASTEXITCODE) { throw 'Native audio compilation failed; see compiler errors above.' }
New-Item -ItemType Directory -Force -Path (Split-Path $OutputFile -Parent) | Out-Null
$binary = Join-Path $build "$Configuration/VoxelAudioNative.dll"
if (!(Test-Path $OutputFile) -or [IO.File]::GetLastWriteTimeUtc($binary) -ne [IO.File]::GetLastWriteTimeUtc($OutputFile) -or (Get-Item $binary).Length -ne (Get-Item $OutputFile).Length) { Copy-Item -LiteralPath $binary -Destination $OutputFile -Force }
# MSBuild compares this output to source files and the toolchain stamp on the next build.
[IO.File]::SetLastWriteTimeUtc($OutputFile, [DateTime]::UtcNow)
