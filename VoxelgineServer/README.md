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

## Running and administering a server

```powershell
dotnet run --project VoxelgineServer/VoxelgineServer.csproj -c Release -- --port 7777 --seed 666 --data-root C:/AuroraServer
```

The default UDP port is 7777 and logging defaults to Info. Allow that UDP port
through the host firewall and forward it for remote clients when needed. Use
`--log-level Trace` to opt into packet tracing. Trace logging does not serialize
packets again to estimate their size. Use `--help` for startup options.

An existing compatible archive wins over `--seed`. `--force-regen` deliberately
replaces the world. `--world-plan <directory>` loads a validated canonical plan;
it cannot be combined with an explicit `--seed`.

The local console accepts `players`, `status`, `save`, `time <hour>`, `say`,
`kick`, and `quit`/`stop`. `players` prints public identity fingerprints. Use
`op <player name, slot ID, or fingerprint>` and `deop <...>` to persist permissions.
`ban <...>` persists the identity ban before disconnecting a matching player;
`unban <fingerprint>` removes it even while that player is offline. Global player
commands and debug mutations require administrator permission. The local console
is the authority for permission changes; connecting from loopback grants none.

The server uses protocol 2 and archive format 6. Upgrade clients with the server;
unauthenticated gameplay connections are rejected. See [Multiplayer](../MULTIPLAYER.md)
and [Runtime data](../docs/RUNTIME_DATA.md) for handshake, key backups, fresh
profiles, legacy backups, and server trust reset. World saves remain synchronous;
`[Persistence] world-save` records their full duration in milliseconds.
