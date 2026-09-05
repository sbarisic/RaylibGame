# Building and validation

## Prerequisites

The Windows x64 client requires:

- Git with recursive submodule support;
- .NET 10 SDK and the .NET 9 targeting pack (the pinned nested FishUI project
  still targets `net9.0`);
- CMake available on `PATH`; and
- Visual Studio 2022 Build Tools or Visual Studio 2022 with the Desktop
  development with C++ workload and the MSVC x64 compiler.

The dedicated server and portable engine require only Git and the .NET 10 SDK.
They must build and publish on Windows and Linux without a graphics or audio
toolchain.

Confirm the local tools:

```powershell
git --version
dotnet --info
cmake --version
```

## Recursive checkout

```powershell
git clone --recurse-submodules https://github.com/sbarisic/RaylibGame.git
Set-Location RaylibGame
git submodule status --recursive
pwsh -NoProfile -File scripts/Test-SubmodulePins.ps1
pwsh -NoProfile -File scripts/Test-Documentation.ps1
```

For an existing checkout:

```powershell
git submodule sync --recursive
git submodule update --init --recursive
```

Do not use `git submodule update --remote`; reviewed gitlinks are the dependency
lock. See [Dependency pins](DEPENDENCIES.md).

## Build and tests

When renderer dependencies change, run the modern FishGfx acceptance surface.
Legacy FishGfx solutions and sample projects are not acceptance gates.

```powershell
dotnet restore thirdparty/FishGfx/FishGfx.Modern.sln
dotnet test thirdparty/FishGfx/FishGfx.Modern.sln -c Release --no-restore
```

Run engine, client, and audio suites sequentially in Debug and Release. Avoid
concurrent builds/tests sharing native or managed outputs. For example, the
Release commands are:

```powershell
dotnet restore RaylibGame.sln
dotnet build RaylibGame.sln -c Release -p:Platform=x64 --no-restore
dotnet test VoxelgineEngine.Tests/VoxelgineEngine.Tests.csproj -c Release `
  -f net10.0
dotnet test UnitTest/UnitTest.csproj -c Release `
  -f net10.0-windows10.0.17763.0 `
  -p:Platform=x64
dotnet test Voxelgine.Audio.Tests/Voxelgine.Audio.Tests.csproj -c Release `
  -p:Platform=x64
```

The non-interactive client render smoke is:

```powershell
dotnet run --project Voxelgine/Voxelgine.csproj -c Release `
  -f net10.0-windows10.0.17763.0 -- --fishgfx-auto
```

It creates a real FishGfx window/context, renders several frames, and requires
non-clear pixels. It needs an interactive Windows desktop and is therefore a
manual or desktop-runner check, not a headless GitHub-hosted runner step.

## Publish and audit

Publish the Windows x64 client:

```powershell
dotnet publish Voxelgine/Voxelgine.csproj -c Release `
  -f net10.0-windows10.0.17763.0 -r win-x64 --self-contained false `
  -p:Platform=x64 -o artifacts/client
pwsh -NoProfile -File scripts/Test-PublishLayout.ps1 `
  -Kind Client -Path artifacts/client
```

The client audit requires FishGfx/FishUI assemblies and data, `glfw3.dll`,
`VoxelAudioNative.dll`, the audio bank, exactly ten FLAC ambience loops, game
models/textures, and FishGfx/game shaders.

Publish the framework-neutral server without a runtime identifier:

```powershell
dotnet publish VoxelgineServer/VoxelgineServer.csproj -c Release `
  -f net10.0 --self-contained false -o artifacts/server
pwsh -NoProfile -File scripts/Test-PublishLayout.ps1 `
  -Kind Server -Path artifacts/server
```

The server audit rejects the client assembly, FishGfx, FishUI, GLFW, Raylib,
VoxelAudioNative, `Voxelgine.Audio`, OpenAL, Steam Audio, and renderer package
references in the dependency manifest.

## Final cutover gates

The migration is cut over. This check must remain empty in production source:

```powershell
rg -n --glob '!thirdparty/FishGfx/data/textures/voxels/raylibgame/**' `
  --glob '!docs/**' --glob '!README.md' `
  'Raylib_cs|\bRaylib\.|\bRlgl\.' Voxelgine VoxelgineEngine VoxelgineServer
```

Final acceptance also includes a fresh recursive clone, hosted and remote
multiplayer, a dedicated server, save/load compatibility, window and DPI modes,
ambience transitions, repeated state changes, shader-reload failure, and clean
shutdown without Raylib installed. The historical Raylib comparison is a migration target, not a current measured
result. Use the current workload and acceptance procedures in
[Validation](VALIDATION.md), and report unavailable graphical/platform checks
separately from passed suites.

## Focused performance measurements

BenchmarkDotNet accepts filters:

```powershell
dotnet run --project BenchmarkSuite1/BenchmarkSuite1.csproj -c Release -- --filter '*FarmingBenchmarks*' '*ObjectOverlayBenchmarks*' '*ColumnRevisionBenchmarks*'
```

For bounded comparative measurements with setup excluded, use:

```powershell
dotnet run --project BenchmarkSuite1/Measure/Measure.csproj -c Release
```

The probe records median/p95/p99, current-thread allocations, overlay write/dirty
counts, network queue retention, and repeated-column cache retention. Its
`Baseline=true` build option and `EngineProject=<absolute baseline project>`
override allow the same probe to build against an isolated original checkout.
The baseline overlay branch models the old write/dirty loop; it is not a GPU
frame-time benchmark. Preserve raw JSON and machine/runtime context with results.
Do not run timed comparisons concurrently with builds or test suites.

Protocol 2 requires clients and servers to upgrade together; archive format 6 is
preserved. Test isolated runtime roots so fresh identity profiles, server pins,
and legacy backups cannot affect normal developer saves.

Generation, meshing, scheduling, page grouping and retention probes are documented
in [Voxel performance](VOXEL_PERFORMANCE.md). Run generation from the repository
root so `--features` loads the checked-in structure and CeramicFish assets. GPU
probes open real windows; keep their captures and JSON under ignored artifacts.
