# Voxelgine dedicated server

`VoxelgineServer` targets plain `net10.0` and references only
`VoxelgineEngine`. It deliberately does not reference the Windows client,
FishGfx, FishUI, a legacy renderer, or an audio runtime.

Authoritative simulation, world storage, physics, AI, packets, entities,
players, generation, serialization, lighting, and the server loop are compiled
once in `VoxelgineEngine`. The client and dedicated server consume the same
types through project references; the server project contains no linked client
sources or parallel portable implementations.

Build and publish from the repository root:

```powershell
dotnet build VoxelgineServer/VoxelgineServer.csproj -c Release
dotnet publish VoxelgineServer/VoxelgineServer.csproj -c Release -o artifacts/server
```

The published directory must not contain `Voxelgine.dll`, a legacy renderer,
FishGfx/FishUI, GLFW, or audio-native binaries.
