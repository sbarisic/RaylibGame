#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][ValidatePattern('^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$')][string] $Version)
$ErrorActionPreference = 'Stop'
$repo = 'sbarisic/RaylibGame'
$releases = & gh api --paginate "repos/$repo/releases?per_page=100" --jq '.[] | [.tag_name, .draft] | @tsv'
if ($LASTEXITCODE) { throw 'Cannot enumerate existing releases.' }
foreach ($line in $releases) {
    $tag, $draft = $line -split "`t"
    if ($tag -eq $Version) { throw "Release $Version already exists (including drafts); inspect it before retrying." }
    if ($draft -eq 'false' -and $tag -match '^v\d+\.\d+\.\d+$' -and [version]$tag.Substring(1) -ge [version]$Version.Substring(1)) { throw 'Release versions must increase.' }
}
$zip = Join-Path (Split-Path $PSScriptRoot -Parent) "artifacts/releases/AuroraFalls-$Version-win-x64.zip"
$sha = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
& gh release create $Version --repo $repo --verify-tag --draft --title "Aurora Falls $Version" --generate-notes $zip "$zip.sha256"
if ($LASTEXITCODE) { throw 'Draft release creation/upload failed. Inspect the draft before retrying.' }
$releaseJson = & gh api "repos/$repo/releases/tags/$Version"
if ($LASTEXITCODE) { throw 'Cannot verify uploaded assets; release remains a draft.' }
$release = $releaseJson | ConvertFrom-Json
foreach ($file in @($zip, "$zip.sha256")) {
    $asset = @($release.assets | Where-Object name -EQ ([IO.Path]::GetFileName($file)))
    $digest = 'sha256:' + (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($asset.Count -ne 1 -or $asset[0].digest -ne $digest -or $asset[0].size -ne (Get-Item -LiteralPath $file).Length) { throw 'GitHub asset verification failed; release remains a draft.' }
}
& gh release edit $Version --repo $repo --draft=false --prerelease=false --latest
if ($LASTEXITCODE) { throw 'Release publication failed; inspect its state on GitHub.' }
Write-Host "Published $Version ($sha)."
