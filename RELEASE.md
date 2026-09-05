# Aurora Falls testing releases

There is one Windows x64 release channel. Every published `vMAJOR.MINOR.PATCH`
release is available to all standalone testers. Early development versions use
`v0.x.y`; there are no beta/nightly channels. GitHub's **Latest** release is the
update source. Publish normal releases, not GitHub prereleases.

## For testers

Download `AuroraFalls-v0.x.y-win-x64.zip` from
[GitHub Releases](https://github.com/sbarisic/RaylibGame/releases/latest), extract
the entire ZIP into a writable folder, and double-click `AuroraFalls.exe`.
Do not run inside the ZIP, merge ZIPs over an existing installation, or install
under Program Files. No .NET installation, Visual Studio, administrator access,
or GitHub account is required. Windows may show its normal first-run warning
because these early builds are not code signed.

Each launch checks for an update before opening the game. A newer version is
downloaded, verified, installed, and launched automatically. The launcher shows
progress and offers **Play current version**; closing the progress window has
the same effect. Offline startup, GitHub errors/rate limits, failed downloads,
and failed installation all fall back to the installed game. Metadata checks
time out after five seconds; downloads abort after thirty seconds without data.
One game/update process can run per installation at a time.

Saves, settings, identities and game logs remain in the existing per-user
`AuroraFalls` runtime directory. `--data-root` and other game arguments pass
through the launcher unchanged. See [runtime data](docs/RUNTIME_DATA.md).
Updater diagnostics are in `%LOCALAPPDATA%/AuroraFallsUpdater/logs/updater.log`.
The updater does not change saved-game formats or roll back saved games.

Older `build*` releases do not contain this updater. Their users must download
and extract a new versioned release once. Subsequent updates are automatic.

## Build prerequisites and local packaging

Use Windows x64, PowerShell 7, Git with recursive submodules, .NET 10 and .NET 9
SDKs, and Visual Studio 2022 or 2026 with **Desktop development with C++**, the
MSVC x64 compiler, a Windows SDK, and C++ CMake tools.

The native-audio build searches an explicit `-p:CMakeExecutable=<full path>`,
then compatible CMake on PATH, then the selected Visual Studio's bundled CMake.
Visual Studio 2022 requires CMake 3.24 or newer; VS 2026 requires 4.2 or newer.
No global PATH modification is necessary. Native caches are separated by
Visual Studio instance and configuration. Error 9009 previously meant that
the build could not find the bare `cmake` command; subsequent missing managed
DLL errors were a consequence of that failure.

From the repository root:

```powershell
git submodule update --init --recursive
dotnet build RaylibGame.sln -c Release -p:Platform=x64
pwsh -NoProfile -File scripts/New-ReleasePackage.ps1 -Version v0.1.0
```

You can also run `./scripts/New-ReleasePackage.ps1 -Version v0.1.0` directly
from Windows PowerShell 5.1: it automatically hands off to installed PowerShell
7. Omitting `-Version` prompts for a version such as `v0.1.0`. If PowerShell 7
is missing, the script prints an installation command.

The script produces `artifacts/releases/AuroraFalls-v0.1.0-win-x64.zip` and
its `.zip.sha256` file. It prints the extracted staging location for testing.
Every package uses fresh build intermediates. Its root `build-info.json` records
the source commit, whether local changes were included, dependency commits, and
the UTC build time so testers can identify the source behind a download.
It never publishes to GitHub. It refuses to overwrite an existing local ZIP;
explicitly remove that artifact before rebuilding an **unpublished** version.

Publishing includes the .NET runtime, untrimmed game/updater assemblies, game
assets, GLFW and native audio. Native audio uses the static CRT. The native
import audit copies necessary Microsoft CRT DLLs from Visual Studio's
redistribution directory beside their consumers and rejects unresolved imports.
Do not suppress the asset or native dependency audits to force a release.

Ordinary Debug/Release builds, F5, Ctrl+F5, and `dotnet run` do not check for
updates. Only packaging sets `ReleasePackage=true`; its game output is isolated
under `bin/Package`. A packaged game opened directly redirects to the root
launcher before game initialization, unless a debugger is attached. Developers
should keep Voxelgine as their Visual Studio startup project.

## Publish a release

Choose a new, increasing version. Start with `v0.1.0` after the old `build*`
tags; subsequent examples are `v0.1.1` and `v0.2.0`. Commit the release changes
and verify the clean-machine smoke checks below before tagging:

```powershell
git push origin main
git tag -a v0.1.0 -m "Aurora Falls v0.1.0"
git push origin v0.1.0
```

The tag workflow uses `windows-2022`, recursively checks out pinned dependencies,
builds the solution, runs engine/client/audio/updater tests sequentially,
packages, and audits. It creates a draft release, uploads the ZIP and checksum,
verifies their GitHub SHA-256 digests and sizes, then publishes it as Latest.
The Actions `GITHUB_TOKEN` needs contents-write permission for publication.
Tester binaries contain no token. Workflow artifacts preserve the packaged build.

Tags must be exactly `vMAJOR.MINOR.PATCH`, with no leading zeroes, suffixes or
build metadata. Published versions must increase. Existing releases, including
drafts with the same tag, are rejected. Never move published tags or replace
published assets. For a failed upload, inspect the unpublished draft and delete
it explicitly before retrying, or choose a new version. Publication is never
attempted after failed builds/tests/audits.

Review the generated release notes on GitHub, especially multiplayer and save
compatibility. Dedicated servers remain manually deployed and may need a
matching update when network compatibility changes.

## Installation layout and recovery

```text
AuroraFalls.exe                    fixed bootstrap, dispatch contract v1
current.json                      schemaVersion, current, previous
current.previous.json             previous pointer, after an update
versions/v0.1.0/manifest.json       schema/platform/version and file hashes
versions/v0.1.0/updater/            versioned updater and its runtime
versions/v0.1.0/game/               game, runtime and immutable assets
```

The fixed bootstrap launches the updater in the active version and owns the
installation lock until the game exits. Updaters ship with every version, so the
next launch uses the new updater without replacing a running executable.
Bootstrap dispatch contract v1 is fixed for this first implementation; changing
that protocol requires a fresh portable ZIP. Packages requiring another
protocol are rejected safely.

The updater accepts only a newer normal release from
`sbarisic/RaylibGame/releases/latest`, with the exact Windows package asset and
a GitHub SHA-256 digest. It verifies the archive, embedded manifest and all file
hashes, rejects unsafe paths/links, then installs into a version directory.
It atomically replaces `current.json` only after the package is complete.
Downloads use updater-owned `.update-<guid>` staging directories in the same
installation. Archive limits are 4 GiB compressed, 8 GiB expanded, and 100,000
entries. The current and previous versions are retained; verified older version
folders and marked abandoned staging directories are cleaned on later starts.

An interrupted download does not affect the installed game. An interrupted
activation can recover through `current.previous.json`. A failure to create the
new game process restores the previous selection. A game that starts and then
crashes does **not** trigger automatic rollback: report it and publish a fix.
No rollback of saves is attempted. If the installation itself is damaged,
extract a fresh ZIP into a new folder; existing runtime data is preserved.

For a bad published build, revert the faulty changes and publish a **higher**
patch version. Merely marking an older release Latest cannot downgrade clients.
Do not delete versions or edit pointers while the launcher/game is running.

## Validation before sharing

```powershell
dotnet build RaylibGame.sln -c Debug -p:Platform=x64
dotnet build RaylibGame.sln -c Release -p:Platform=x64
dotnet test AuroraFalls.Update.Tests/AuroraFalls.Update.Tests.csproj -c Release
dotnet test VoxelgineEngine.Tests/VoxelgineEngine.Tests.csproj -c Release
dotnet test UnitTest/UnitTest.csproj -c Release -p:Platform=x64
dotnet test Voxelgine.Audio.Tests/Voxelgine.Audio.Tests.csproj -c Release -p:Platform=x64
pwsh -NoProfile -File scripts/Test-Documentation.ps1
# Interactive desktop: use the extracted package path printed during packaging.
pwsh -NoProfile -File scripts/Test-ReleaseStartup.ps1 -Path C:/AuroraFallsTest
```

The startup smoke runs the packaged launcher, offline fallback, and direct-game
redirection, using separate runtime roots with spaces and Unicode characters.
It validates rendered pixels and clean shutdown; it opens real windows briefly.

Updater tests use isolated temporary installations and an HTTP fixture. They
cover version selection, integrity, extraction safety, cancellation, filesystem
failures, recovery, locking and argument forwarding. No test publishes a release
or touches normal saves. The release package audit also runs automatically.

Manual acceptance on a clean supported Windows x64 machine without .NET or
Visual Studio: extract and launch offline; verify rendering and audio; update
an older package to a newer published version; repeat offline/cancelled startup;
check saves and identity persistence; test a folder with spaces and non-ASCII
characters; confirm no elevation prompt. Use two isolated test versions for
the update smoke. On a developer machine, verify F5 and Ctrl+F5 never open the
updater or contact GitHub. Graphical/clean-machine checks require an interactive
desktop and are not claimed by headless CI.
