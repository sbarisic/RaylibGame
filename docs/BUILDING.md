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
```

For an existing checkout:

```powershell
git submodule sync --recursive
git submodule update --init --recursive
```

Do not use `git submodule update --remote`; reviewed gitlinks are the dependency
lock. See [Dependency pins](DEPENDENCIES.md).

## Build and tests

Run the modern FishGfx acceptance surface first. Legacy FishGfx solutions and
sample projects are not migration gates.

```powershell
dotnet restore thirdparty/FishGfx/FishGfx.Modern.sln
dotnet test thirdparty/FishGfx/FishGfx.Modern.sln -c Release --no-restore
```

Then build and test RaylibGame:

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
shutdown without Raylib installed. Performance is compared to the recorded
Raylib baseline: median frame time may regress by at most 10 percent and p99 by
at most 20 percent in the deterministic scene.
