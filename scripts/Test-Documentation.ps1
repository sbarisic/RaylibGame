[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$documents = @(& git -C $repositoryRoot ls-files --cached --others --exclude-standard -- '*.md' | Sort-Object -Unique)
if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate first-party Markdown.' }
$protocolSource = Get-Content -LiteralPath (Join-Path $repositoryRoot 'VoxelgineEngine/Engine/Net/NetServer.cs') -Raw
$archiveSource = Get-Content -LiteralPath (Join-Path $repositoryRoot 'VoxelgineEngine/Engine/World/WorldArchive.cs') -Raw
$protocol = [regex]::Match($protocolSource, 'ProtocolVersion\s*=\s*(\d+)').Groups[1].Value
$archive = [regex]::Match($archiveSource, 'FormatVersion\s*=\s*(\d+)').Groups[1].Value
$failures = [System.Collections.Generic.List[string]]::new()

foreach ($document in $documents) {
    if ($document.StartsWith('thirdparty/')) { continue }
    $path = Join-Path $repositoryRoot $document
    $content = Get-Content -LiteralPath $path -Raw
    $inHistoricalBlock = $false
    foreach ($marker in [regex]::Matches($content, '<!-- historical:(start|end) -->')) {
        $opening = $marker.Groups[1].Value -eq 'start'
        if ($opening -eq $inHistoricalBlock) { $failures.Add("${document}: nested or unmatched historical marker") }
        $inHistoricalBlock = $opening
    }
    if ($inHistoricalBlock) { $failures.Add("${document}: unclosed historical block") }
    # Only explicitly marked historical blocks are exempt from current references.
    $current = [regex]::Replace($content, '(?s)<!-- historical:start -->.*?<!-- historical:end -->', '')
    foreach ($link in [regex]::Matches($content, '\[[^\]\r\n]*\]\(([^)\r\n]+)\)')) {
        $target = $link.Groups[1].Value.Trim().Trim('<', '>')
        if ($target -match '^(https?:|mailto:|app:|#)' -or $target -match '\s+"') { continue }
        $target = [uri]::UnescapeDataString(($target -split '#', 2)[0])
        if (-not $target) { continue }
        $resolved = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $path) $target))
        if (-not (Test-Path -LiteralPath $resolved)) { $failures.Add("${document}: broken local link '$target'") }
    }
    foreach ($match in [regex]::Matches($current, '(?i)\b(?:network\s+)?protocol(?:\s+version)?(?:\s+is)?\s*:?\s*[*`]*(\d+)')) {
        if ($match.Groups[1].Value -ne $protocol) { $failures.Add("${document}: stale protocol reference '$($match.Value)'") }
    }
    foreach ($match in [regex]::Matches($current, '(?i)\b(?:world\s+)?archive\s+(?:format|schema|version)(?:\s+is)?(?:\s+version)?\s*:?\s*[*`]*(\d+)')) {
        if ($match.Groups[1].Value -ne $archive) { $failures.Add("${document}: stale archive reference '$($match.Value)'") }
    }
}

$packetSource = Get-Content -LiteralPath (Join-Path $repositoryRoot 'VoxelgineEngine/Engine/Net/Packet.cs') -Raw
$enum = [regex]::Match($packetSource, '(?s)enum PacketType : byte\s*\{(.*?)\n\s*\}').Groups[1].Value
$multiplayer = Get-Content -LiteralPath (Join-Path $repositoryRoot 'MULTIPLAYER.md') -Raw
foreach ($entry in [regex]::Matches($enum, '(\w+)\s*=\s*(0x[0-9A-Fa-f]+)')) {
    $expected = '| ' + $entry.Groups[2].Value + ' | `' + $entry.Groups[1].Value + '` |'
    if (-not $multiplayer.Contains($expected)) { $failures.Add("MULTIPLAYER.md: missing current packet row $expected") }
}
if ($failures.Count) { throw ($failures -join [Environment]::NewLine) }
Write-Host "Verified $($documents.Count) first-party Markdown files, local links, packet IDs, protocol $protocol and archive $archive references."

& (Join-Path $repositoryRoot "thirdparty/FishGfx/scripts/Test-Documentation.ps1")
