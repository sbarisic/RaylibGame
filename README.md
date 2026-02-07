[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/sbarisic/RaylibGame)

# Aurora Falls - Voxelgine Engine

**Aurora Falls** is a voxel-based sandbox engine and game written in modern **C# (.NET 9)**, featuring real-time 3D rendering with **Raylib-cs**, a modular entity system, client-server multiplayer (up to 10 players), and a fully interactive world.

Players can explore, build, and modify a procedurally generated floating island environment, interact with blocks and entities, and use a variety of tools and weapons — alone or with others.

![Screenshot 1](img/39Kudu88Xu.png)

![Screenshot 2](img/5LoczCbPHp.png)

---

## Features

- **Procedural World Generation** — Floating islands generated via simplex noise with grass, dirt, stone, sand, and water
- **Block System** — Place, destroy, and interact with 20+ block types including transparent blocks (water, glass, ice)
- **Dual-Channel Lighting** — Separate skylight and block light propagation with real-time updates
- **Quake-Style Physics** — Strafe-jumping, bunny-hopping, air control, water swimming with buoyancy
- **Client-Server Multiplayer** — Up to 10 players, server-authoritative with client-side prediction and remote player interpolation
- **Entity System** — Networked entities with pickup items, NPCs with pathfinding, and interactive doors
- **Combat System** — Server-authoritative weapon fire with raycast hit detection against world, entities, and players
- **Player Health & Respawn** — Damage, death overlay, and timed respawn at spawn point
- **Particle System** — Smoke, blood, fire sparks, and weapon tracer effects with depth-sorted rendering
- **FishUI-Based GUI** — Custom inventory, item boxes, in-game menus, server connect/host dialogs
- **Save/Load System** — GZip-compressed world and player state persistence
- **Dependency Injection** — `FishDI` container with interface-based services (`IFishLogging`, `IFishConfig`, `IFishDebug`, etc.)
- **Structured Logging** — `IFishLogging` with timestamped file output and console mirroring
- **Hot-Reload Shaders** — Edit shaders at runtime for rapid iteration
- **Frame Interpolation** — Smooth camera and position rendering independent of physics tick rate

---

## Architecture

### Solution Structure

```
RaylibGame.sln
├── Voxelgine/              # Main client project (Raylib rendering, GUI, gameplay states)
│   ├── Engine/             # Core systems, player, entities, weapons, physics, server loop
│   ├── Graphics/           # Chunk rendering, GBuffer, skybox, frustum culling
│   ├── GUI/                # FishUI integration and custom controls
│   ├── States/             # Game states (main menu, gameplay, multiplayer, NPC preview)
│   └── data/               # Assets (textures, models, sounds, shaders)
├── VoxelgineEngine/        # Shared library (Raylib-free: DI, physics, input, networking)
│   └── Engine/
│       ├── DI/             # FishDI container, service interfaces
│       ├── Physics/        # AABB, PhysicsUtils, RayMath (pure math)
│       ├── Animations/     # LerpManager, AnimLerp, easing functions
│       ├── Input/          # IInputSource, NetworkInputSource
│       └── Net/            # UDP transport, packets, reliable delivery, client/server
├── VoxelgineServer/        # Dedicated headless server (CLI, no Raylib)
└── UnitTest/               # Unit tests for core systems
```

### Core Systems

| System | Files | Description |
|--------|-------|-------------|
| **Program** | `Program.cs` | Entry point, game loop with fixed timestep physics |
| **GameWindow** | `GameWindow.cs` | Window management, render targets, state switching |
| **GameState** | `States/GameState.cs` | Single-player gameplay state, world/player/entity management |
| **GameSimulation** | `Engine/GameSimulation.cs` | Authoritative game state (`ChunkMap`, `PlayerManager`, `EntityManager`, `DayNightCycle`, `PhysData`) |
| **MultiplayerGameState** | `States/MultiplayerGameState.cs` | Multiplayer client: connection, prediction, interpolation, remote players |
| **ServerLoop** | `Engine/ServerLoop.cs` | Server game loop: input processing, physics, combat, world/entity sync |
| **InputMgr** | `VoxelgineEngine/.../InputMgr.cs` | Input abstraction via `IInputSource` (local Raylib / network) |
| **SoundMgr** | `SoundMgr.cs` | Positional audio, sound combos (randomized effects) |
| **ResMgr** | `ResMgr.cs` | Resource loading (textures, models, shaders) with hot-reload |
| **GameConfig** | `GameConfig.cs` | JSON-based configuration (resolution, vsync, sensitivity) |
| **FishDI** | `VoxelgineEngine/.../FishDI.cs` | Dependency injection container (singleton/scoped/transient services) |
| **FishLogging** | `VoxelgineEngine/.../FishLogging.cs` | Timestamped file + console logging via `IFishLogging` interface |

### Graphics Pipeline

| Component | Files | Description |
|-----------|-------|-------------|
| **ChunkMap** | `Graphics/ChunkMap.cs` | Spatial hash grid of chunks, world queries, lighting computation |
| **Chunk** | `Graphics/Chunk.cs` | 16³ block storage, mesh generation, transparent face caching |
| **PlacedBlock** | `Graphics/Chunk/PlacedBlock.cs` | Block type + dual light values (skylight/blocklight) |
| **BlockLayout** | `Graphics/Chunk/BlockLayout.cs` | Face visibility and UV calculation |
| **GBuffer** | `Graphics/GBuffer.cs` | Deferred rendering targets |
| **Frustum** | `Graphics/Frustum.cs` | View frustum culling |
| **Skybox** | `Engine/Skybox.cs` | Procedural sky rendering |

### Entity System

| Component | Files | Description |
|-----------|-------|-------------|
| **VoxEntity** | `Engine/Entities/VoxEntity.cs` | Base class with network ID, spawn properties, snapshot serialization |
| **EntityManager** | `Engine/Entities/EntityManager.cs` | Entity spawning, physics, network ID tracking, authority flag |
| **VEntPickup** | `Engine/Entities/VEntPickup.cs` | Collectible items with rotation animation |
| **VEntNPC** | `Engine/Entities/VEntNPC.cs` | NPC entities with JSON model, pathfinding, animator |
| **VEntSlidingDoor** | `Engine/Entities/VEntSlidingDoor.cs` | Interactive animated doors with network serialization |

### Player & Physics

| Component | Files | Description |
|-----------|-------|-------------|
| **Player** | `Engine/Player/Player.cs` | Player state, input, physics, inventory, health/respawn |
| **PlayerManager** | `Engine/Player/PlayerManager.cs` | `Dictionary<int, Player>` with remote player tracking |
| **RemotePlayer** | `Engine/Player/RemotePlayer.cs` | Client-side remote player with snapshot interpolation and humanoid model |
| **FPSCamera** | `Engine/FPSCamera.cs` | Instance-based first-person camera with mouse look |
| **ViewModel** | `Engine/ViewModel.cs` | First-person weapon/tool rendering |
| **PhysicsUtils** | `VoxelgineEngine/.../PhysicsUtils.cs` | Pure math: ClipVelocity, acceleration, AABB creation |
| **WorldCollision** | `Engine/Physics/WorldCollision.cs` | ChunkMap-dependent collision and movement |
| **RayMath** | `VoxelgineEngine/.../RayMath.cs` | Ray-AABB intersection (slab method) |
| **AABB** | `VoxelgineEngine/.../AABB.cs` | Axis-aligned bounding box (Raylib-free) |

### GUI System

| Component | Files | Description |
|-----------|-------|-------------|
| **FishUIManager** | `GUI/FishUI/FishUIManager.cs` | Main UI manager wrapping FishUI library |
| **RaylibFishUIGfx** | `GUI/FishUI/RaylibFishUIGfx.cs` | Raylib graphics backend for FishUI |
| **RaylibFishUIInput** | `GUI/FishUI/RaylibFishUIInput.cs` | Raylib input backend for FishUI |
| **FishUIItemBox** | `GUI/FishUI/Controls/FishUIItemBox.cs` | Inventory slot with icon rendering |
| **FishUIInventory** | `GUI/FishUI/Controls/FishUIInventory.cs` | Hotbar/inventory display |
| **FishUIInfoLabel** | `GUI/FishUI/Controls/FishUIInfoLabel.cs` | Debug/info text overlay |

### Animation & Effects

| Component | Files | Description |
|-----------|-------|-------------|
| **AnimLerp** | `Engine/Animations/AnimLerp.cs` | Interpolation with 30+ easing functions |
| **LerpManager** | `Engine/Animations/LerpManager.cs` | Global animation instance management |
| **ParticleSystem** | `Engine/ParticleSystem.cs` | Billboard particles with depth sorting |

### Weapons & Items

| Component | Files | Description |
|-----------|-------|-------------|
| **InventoryItem** | `Engine/Weapons/InventoryItem.cs` | Base item with block placement logic |
| **Weapon** | `Engine/Weapons/Weapon.cs` | Base weapon class |
| **WeaponGun** | `Engine/Weapons/WeaponGun.cs` | Firearm with separated fire intent / resolve / effects (multiplayer-ready) |
| **WeaponPicker** | `Engine/Weapons/WeaponPicker.cs` | Block picker tool |
| **FireIntent** | `Engine/Weapons/FireIntent.cs` | `FireIntent`, `FireResult`, `FireHitType` structs for server-authoritative combat |

### Networking

| Component | Files | Description |
|-----------|-------|-------------|
| **UdpTransport** | `VoxelgineEngine/.../UdpTransport.cs` | Raw UDP socket wrapper with async receive loop |
| **ReliableChannel** | `VoxelgineEngine/.../ReliableChannel.cs` | Reliability layer: sequence numbers, ACKs, retransmission |
| **NetConnection** | `VoxelgineEngine/.../NetConnection.cs` | Per-connection state: reliable channel, RTT, timeout |
| **NetServer** | `VoxelgineEngine/.../NetServer.cs` | Server: connection management, player IDs, broadcast |
| **NetClient** | `VoxelgineEngine/.../NetClient.cs` | Client: connect, world loading, tick sync |
| **Packet** | `VoxelgineEngine/.../Packet.cs` | 24 packet types with binary serialization |
| **ClientPrediction** | `VoxelgineEngine/.../ClientPrediction.cs` | Prediction state buffer with server reconciliation |
| **SnapshotBuffer** | `VoxelgineEngine/.../SnapshotBuffer.cs` | Generic ring buffer for remote entity interpolation |
| **WorldTransferManager** | `VoxelgineEngine/.../WorldTransferManager.cs` | Server-side world data fragmentation and streaming |
| **WorldReceiver** | `VoxelgineEngine/.../WorldReceiver.cs` | Client-side fragment reassembly with checksum verification |

---

## Controls

| Key | Action |
|-----|--------|
| **WASD** | Move |
| **Mouse** | Look around |
| **Space** | Jump / Swim up |
| **Shift** | Walk (slow) / Swim down / Ledge safety |
| **C** | Toggle noclip mode |
| **1-4** | Select hotbar slot |
| **Left Click** | Use item / Break block |
| **Right Click** | Place block |
| **F1** | Toggle debug menu |
| **F3** | Toggle debug mode |
| **F5** | Quick save |
| **Esc** | Return to main menu |

---

## Building

### Requirements

- .NET 9 SDK
- Visual Studio 2022+ or VS Code with C# extension

### Build & Run

```bash
# Clone the repository
git clone https://github.com/sbarisic/RaylibGame.git
cd RaylibGame

# Build and run the client
dotnet run --project Voxelgine

# Run a dedicated headless server
dotnet run --project VoxelgineServer -- --port 7777 --seed 666
```

### Run Tests

```bash
dotnet test
```

---

## Project Status

| System | Status | Notes |
|--------|--------|-------|
| Core Engine | ✅ Complete | Window, input, audio, resources, DI, logging |
| Graphics | ✅ Complete | Chunks, lighting, frustum culling, deferred rendering |
| Voxel World | ✅ Complete | Generation, block types, dual lighting, block change tracking |
| Player | ✅ Complete | Movement, physics, inventory, health/respawn, remote rendering |
| Physics | ✅ Complete | Quake-style with water buoyancy, split into pure math + world collision |
| GUI | ✅ Complete | FishUI-based menus, HUD, server connect/host dialogs |
| Entity System | ✅ Complete | Networked entities with spawn properties, authority flag |
| Weapons | ✅ Complete | Server-authoritative fire intent/resolve/effects pipeline |
| Animation | ✅ Complete | Lerp system, NPC animator with walk/idle/attack |
| Multiplayer | 🔶 Partial | Client-server authoritative, prediction, interpolation, combat — chat/UI/bandwidth pending |
| Particles | 🔶 Partial | Smoke, blood, fire effects — spark type planned |
| NPC/AI | ⬜ Planned | Entity + pathfinding exist, no behavior trees |
| Mod System | ⬜ Planned | Tracked in [TODO_MODS.md](TODO_MODS.md) |
| Scripting | ⬜ Planned | Stub exists |

---

## License

This project is for educational and experimental purposes.

---

## Screenshots

![Screenshot 3](img/m2n3Uh6ucn.png)

![Screenshot 4](img/r2g4zhselE.png)
