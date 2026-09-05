[CmdletBinding()]
param([Parameter(Mandatory)][string] $Path, [switch] $CopyRuntime)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $Path).Path
$vswhere = "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe"
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vs) { throw 'MSVC tools are required to audit native dependencies.' }
$tools = Get-ChildItem "$vs/VC/Tools/MSVC" -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$dumpbin = Join-Path $tools.FullName 'bin/Hostx64/x64/dumpbin.exe'
$redist = Get-ChildItem "$vs/VC/Redist/MSVC" -Directory | Where-Object Name -Match '^\d+\.' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$crt = if ($redist) { Get-ChildItem "$($redist.FullName)/x64" -Directory -Filter 'Microsoft.VC*.CRT' | Select-Object -First 1 }
$system = '^(api-ms-|ext-ms-)|^(kernel32|kernelbase|user32|gdi32|gdi32full|advapi32|shell32|ole32|oleaut32|ws2_32|winmm|imm32|version|ntdll|sechost|shlwapi|comdlg32|comctl32|setupapi|cfgmgr32|bcrypt|bcryptprimitives|crypt32|ncrypt|secur32|sspicli|rpcrt4|normaliz|iphlpapi|dnsapi|mswsock|powrprof|dwmapi|uxtheme|opengl32|glu32|mscoree|ucrtbase|msvcrt|winhttp|wininet|urlmon|propsys|wtsapi32|hid|avrt|mfplat|mfreadwrite|mfuuid|wintrust|imagehlp|dbghelp|psapi|userenv|netapi32|wldap32|msasn1|cryptbase|authz|dxgi|d3d11|d3d12|dwrite|d2d1|windowscodecs|directwriteforwarder)\.dll$'
$queue = [Collections.Generic.Queue[string]]::new()
Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object Extension -In '.dll','.exe' | ForEach-Object { $queue.Enqueue($_.FullName) }
$visited = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
while ($queue.Count) {
    $binary = $queue.Dequeue()
    if (!$visited.Add($binary)) { continue }
    $output = & $dumpbin /nologo /dependents $binary
    if ($LASTEXITCODE) { throw "Cannot inspect native imports: $binary" }
    foreach ($line in $output) {
        if ($line -notmatch '^\s+([a-zA-Z0-9_.-]+\.dll)\s*$') { continue }
        $dependency = $Matches[1]
        if ($dependency -match $system) { continue }
        $local = Join-Path (Split-Path $binary -Parent) $dependency
        if (Test-Path -LiteralPath $local) { continue }
        if ($CopyRuntime -and $dependency -match '^(msvcp|vcruntime|concrt)\d+.*\.dll$' -and $crt -and (Test-Path (Join-Path $crt.FullName $dependency))) {
            Copy-Item -LiteralPath (Join-Path $crt.FullName $dependency) -Destination $local
            $queue.Enqueue($local)
        } else { throw "Unbundled native dependency '$dependency' imported by '$binary'." }
    }
}
Write-Host "Native dependency audit passed ($($visited.Count) binaries)."
