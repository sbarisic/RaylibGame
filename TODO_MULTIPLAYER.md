# Aurora Falls - Multiplayer System TODO

Planned tasks for implementing multiplayer support (up to 10 players).

> **CPX (Complexity Points)** - 1 to 5 scale:
> - **1** - Single file control/component
> - **2** - Single file control/component with single function change dependencies
> - **3** - Multi-file control/component or single file with multiple dependencies, no architecture changes
> - **4** - Multi-file control/component with multiple dependencies and significant logic, possible minor architecture changes
> - **5** - Large feature spanning multiple components and subsystems, major architecture changes

> Instructions for the TODO list:
- Move all completed TODO items into a separate Completed document (DONE_MULTIPLAYER.md) and simplify by consolidating/combining similar ones and shortening the descriptions where possible

> How this TODO should be iterated:
> - First handle the Uncategorized section, if any similar issues already are on the TODO list, increase their priority instead of adding duplicates
> - When Uncategorized section is empty, start by fixing Active Bugs (take one at a time)
> - After Active Bugs, handle the rest of the TODO by priority and complexity (High priority takes precedence, then CPX points) (take one at a time)
> - Core Refactoring tasks should be completed before Networking Infrastructure; Networking before Server/Client; Server/Client before Synchronization; Synchronization before Gameplay

---

## Design Overview

### Architecture Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| **Network model** | Client-server authoritative | Server owns all game state; prevents cheating, simplifies conflict resolution. One player hosts (listen server) or dedicated headless server. |
| **Transport** | UDP with custom reliability layer | Low latency for position/input updates (unreliable channel), guaranteed delivery for world changes/chat/inventory (reliable channel). No external networking library — use raw `System.Net.Sockets.UdpClient` to maintain dependency-free philosophy. |
| **Tick model** | Server-authoritative fixed timestep | Server runs the existing 0.015s (66.6 Hz) lockstep loop. Clients send inputs at tick rate, server processes and broadcasts state. Existing accumulator pattern in `Program.cs` already supports this. |
| **Client prediction** | Client-side prediction with server reconciliation | Client predicts local player movement immediately using same Quake physics code. Server sends authoritative position; client replays unacknowledged inputs on correction. Preserves responsive strafe-jumping/bunny-hopping feel. |
| **Entity interpolation** | Buffered interpolation for remote entities | Remote players and entities are rendered with a small delay (~100ms buffer), interpolating between received snapshots. Leverages existing `GameFrameInfo.Interpolate()` pattern. |
| **World sync** | Full world transfer on join, delta updates during play | New clients receive GZip-compressed world data (existing `ChunkMap.Write()`). During play, only block changes are sent as deltas (`SetBlock` messages). |
| **Serialization format** | Binary with `BinaryWriter`/`BinaryReader` | Consistent with existing serialization in `Player.Serialization.cs` and `Chunk.Serialization.cs`. Compact, fast, no external dependencies. |
| **Player identity** | Integer player ID (0-9) + string display name | Server assigns IDs on connect. `Player.LocalPlayer` bool already exists; extend with player ID and remote player concept. |
| **Session model** | Listen server (host plays) + dedicated headless mode | Listen server: one player hosts and plays. Headless: `Program.cs` launches server-only without Raylib window/rendering. |

### Network Protocol

| Channel | Delivery | Use Cases |
|---------|----------|-----------|
| **Unreliable** | Fire-and-forget UDP | Player position/velocity/angle snapshots (high frequency), input states |
| **Reliable ordered** | Sequence numbers + ACK | Block place/remove, entity spawn/remove, player connect/disconnect, chat, inventory changes, world chunks |
| **Reliable unordered** | ACK without ordering | Sound events, particle events (nice-to-have, not gameplay-critical) |

### Packet Types

| ID | Name | Direction | Channel | Data |
|----|------|-----------|---------|------|
| 0x01 | `Connect` | C→S | Reliable | Player name, protocol version |
| 0x02 | `ConnectAccept` | S→C | Reliable | Assigned player ID, server tick, world seed |
| 0x03 | `ConnectReject` | S→C | Reliable | Reject reason string |
| 0x04 | `Disconnect` | Both | Reliable | Reason string |
| 0x05 | `PlayerJoined` | S→C | Reliable | Player ID, name, position |
| 0x06 | `PlayerLeft` | S→C | Reliable | Player ID |
| 0x10 | `InputState` | C→S | Unreliable | Tick number, InputState bools, camera angle |
| 0x11 | `PlayerSnapshot` | S→C | Unreliable | Tick number, player ID, position, velocity, angle, animation state |
| 0x12 | `WorldSnapshot` | S→C | Unreliable | Tick number, all player positions (bulk update) |
| 0x20 | `BlockChange` | S→C | Reliable | World position, new block type |
| 0x21 | `BlockPlaceRequest` | C→S | Reliable | World position, block type |
| 0x22 | `BlockRemoveRequest` | C→S | Reliable | World position |
| 0x30 | `EntitySpawn` | S→C | Reliable | Entity type, ID, position, properties |
| 0x31 | `EntityRemove` | S→C | Reliable | Entity ID |
| 0x32 | `EntitySnapshot` | S→C | Unreliable | Entity ID, position, velocity, animation |
| 0x40 | `WorldData` | S→C | Reliable | GZip chunk data (chunked transfer) |
| 0x41 | `WorldDataComplete` | S→C | Reliable | Total chunk count, checksum |
| 0x50 | `ChatMessage` | Both | Reliable | Player ID, message string |
| 0x60 | `WeaponFire` | C→S | Reliable | Weapon type, aim origin, aim direction |
| 0x61 | `WeaponFireEffect` | S→C | Reliable | Player ID, weapon type, origin, direction, hit position, hit type |
| 0x62 | `PlayerDamage` | S→C | Reliable | Target player ID, damage amount, source player ID |
| 0x70 | `DayTimeSync` | S→C | Reliable | Current time of day |
| 0x80 | `Ping` | Both | Unreliable | Timestamp |
| 0x81 | `Pong` | Both | Unreliable | Echoed timestamp |
| 0x90 | `InventoryUpdate` | S→C | Reliable | Slot entries (index, count) |
| 0xA0 | `SoundEvent` | S→C | Unreliable | Event type, position, source player ID |

### Multiplayer State Flow

```
Client Connect Flow:
  Client                          Server
    |--- Connect(name, ver) ------->|
    |                               | Validate, assign ID
    |<-- ConnectAccept(id, tick) ---|
    |<-- WorldData (chunks) --------|  (streamed)
    |<-- WorldDataComplete ---------|
    |<-- PlayerJoined (all) --------|  (existing players)
    |                               |--- PlayerJoined(new) --> Other Clients
    |--- InputState (each tick) --->|
    |<-- WorldSnapshot (each tick) -|
    |<-- PlayerSnapshot (per plyr) -|

Game Tick Flow (Server):
  1. Receive all client InputStates for this tick
  2. Apply each player's input to their player state (same Quake physics)
  3. Run entity UpdateLockstep (EntityManager)
  4. Run world updates (lighting, etc.)
  5. Broadcast WorldSnapshot + PlayerSnapshots + EntitySnapshots
  6. Increment server tick counter

Game Tick Flow (Client):
  1. Read local input, send InputState to server
  2. Predict local player movement (apply input locally with Quake physics)
  3. Receive server snapshots
  4. Reconcile local prediction (replay unACKed inputs from server-confirmed state)
  5. Interpolate remote players/entities between snapshots
  6. Render from local camera
```

### Systems Impact Analysis

| System | Multiplayer Impact | Refactoring Needed |
|--------|-------------------|-------------------|
| **FPSCamera** | ✅ Converted to instance-based — owned by `Player.Camera` | Each player has own `FPSCamera` instance; remote players can track angle independently |
| **Player** | ✅ `PlayerManager` with `Dictionary<int, Player>`, `PlayerId` on Player, `GameState.LocalPlayer` convenience property | `GameState.Ply` replaced with `Players.LocalPlayer`; all callers updated |
| **InputMgr** | ✅ Abstracted via `IInputSource` interface — `LocalInputSource` (Raylib), `NetworkInputSource` (stub) | `InputMgr` no longer has Raylib dependency; takes `IInputSource` in constructor, `SetInputSource()` for swapping |
| **GameState** | ✅ Separated: `GameSimulation` owns authoritative state (`ChunkMap`, `PlayerManager`, `EntityManager`, `DayNightCycle`, `PhysData`); `GameState` holds `Simulation` reference + client-only systems (`ParticleSystem`, `SoundMgr`, `FishUIManager`, rendering) | `GameState` properties delegate to `Simulation`; `VoxEntity`/`EntityManager` use `GameSimulation` directly; headless server can run `GameSimulation` without `GameState` |
| **EntityManager** | ✅ `NetworkId` on `VoxEntity`, auto-assigned by `EntityManager.Spawn()`. `Dictionary<int, VoxEntity>` for O(1) lookup. `GetEntityByNetworkId()`, `Remove()` by ID or reference. `IsAuthority` flag skips physics/AI on clients. `SpawnWithNetworkId()` for client-side entity creation. | Entity spawn/remove sync uses `NetworkId`; server assigns IDs, clients use them for snapshot matching. `VoxEntity` has `EntityTypeName`, `WriteSpawnProperties()`/`ReadSpawnProperties()`, `UpdateVisuals()`. |
| **ChunkMap** | ✅ Block change tracking via `BlockChange` struct and `_blockChangeLog` in `ChunkMap` | `GetPendingChanges()` / `ClearPendingChanges()` for network delta sync; `SetPlacedBlock()` logs old→new type changes |
| **WeaponGun** | ✅ Separated into `FireIntent`/`ResolveFireIntent()`/`ApplyFireEffects()`/`ApplyHitEffects()`. In multiplayer, `OnLeftClick` sends `WeaponFirePacket` to server and skips local resolution. Server resolves authoritatively via `HandleWeaponFire()` (world + entity + player AABB raycast) and broadcasts `WeaponFireEffectPacket`. Client applies hit effects (tracer, blood, sparks) on receipt. Single-player unchanged. | Server uses `ChunkMap.RaycastPos` + `EntityManager.Raycast` + `RaycastPlayers` (player AABB). `WeaponFireEffectPacket` extended with `HitNormal`, `EntityNetworkId`. |
| **ParticleSystem** | Visual only, no gameplay impact | Client-only, triggered by network events (fire effects, blood, etc.) |
| **SoundMgr** | ✅ `Init()` guarded against double `InitAudioDevice()` (native crash). Positional audio is local | Client-only, triggered by network events. Safe to instantiate multiple times. |
| **DayNightCycle** | ✅ `IsAuthority` flag gates local time advancement; `SetTime()` for server sync | Server sets `IsAuthority = true`, clients set `false` — time only updates via `SetTime()` from server |
| **PhysicsUtils** | ✅ Split: pure math (`PhysicsUtils` in `VoxelgineEngine`) + world collision (`WorldCollision` in `Voxelgine`). `RayMath` (ray-AABB intersection) extracted to `VoxelgineEngine`. | Server uses `PhysicsUtils` + `RayMath` for authoritative physics and hit detection without Raylib dependency |
| **PhysData** | Physics constants | Server sends PhysData on connect so all clients match |
| **GameFrameInfo** | Frame interpolation struct | Extend for remote player interpolation |
| **FishDI** | 7 singletons, local services | Add network services (NetServer/NetClient) as DI singletons |
| **Project structure** | ✅ Phase 1+2 complete: `VoxelgineEngine` contains 22+ Raylib-free files — DI, physics (`AABB`, `PhysicsUtils`, `RayMath`), animations (`LerpManager`, `AnimLerp`, `AnimLerpImpl`), input, noise, threading, logging. `Voxelgine` has `AABBExtensions` (Raylib bridge), `WorldCollision` (ChunkMap-dependent), `Raycast` (entity-dependent). | `VoxelgineEngine.csproj` has `Microsoft.Extensions.Hosting` + `TextCopy` NuGet, `AllowUnsafeBlocks`. `Voxelgine` references `VoxelgineEngine`. `VoxelgineServer` references `VoxelgineEngine`. |
| **Program.cs** | Single entry point, local game loop | Add CLI args for dedicated server mode; branch into server-only or client+server loop |
| **GBuffer/Rendering** | Local rendering pipeline | No changes — client-only, server skips rendering |
| **GUI/FishUI** | Local UI | Add multiplayer UI (server browser, player list, chat) — client-only |
| **Frustum** | Camera-based culling | No changes — client-only |
| **ViewModel** | ✅ Migrated from OBJ to `CustomModel` JSON system. Arm (`viewmodel_arm.json`) always rendered; weapons (`gun.json`, `hammer.json`) attached via grip→hand alignment. `MuzzlePoint` from `projectile` mesh. Arm lowered when nothing equipped. | `CustomMaterial`/`CustomModel` extended with `SetTexture()`, `DrawWithMatrix()`, tinted `Draw()`. `ResMgr.GetModelTexture()` added. |

---

## Core Refactoring

> These tasks make the existing single-player code multiplayer-ready without adding any networking yet. The game must remain fully functional in single-player after each refactoring step.

### Medium Priority

*No medium priority tasks*

---

## Networking Infrastructure

> Low-level networking: transport, connection management, packet serialization. No game logic here.

*All tasks completed — see [DONE_MULTIPLAYER.md](DONE_MULTIPLAYER.md)*

---

## Server Implementation

> Server-side game logic. Processes client inputs, runs authoritative simulation, broadcasts state.

*All tasks completed — see [DONE_MULTIPLAYER.md](DONE_MULTIPLAYER.md)*

---

## Client Implementation

> Client-side networking, prediction, interpolation, and rendering of remote players.

### Medium Priority

*No medium priority tasks*

---

## Synchronization

> Specific sync strategies for individual game systems.

*All tasks completed — see [DONE_MULTIPLAYER.md](DONE_MULTIPLAYER.md)*

---

## Gameplay Features

> Multiplayer-specific gameplay features built on top of the networking layer.

*All tasks completed — see [DONE_MULTIPLAYER.md](DONE_MULTIPLAYER.md)*

---

## UI & Menus

### Higher Priority

*No higher priority tasks*

### Medium Priority

*No medium priority tasks*

### Lower Priority

*No low priority tasks*

### On Hold

- [ ] **Server browser (LAN)** — Broadcast UDP discovery on LAN. Servers respond with name, player count, map info. Client displays list of discovered servers. Select to connect. **[CPX: 3]**

---

## Testing & Debugging

### Medium Priority

*No medium priority tasks*

### Lower Priority

*No lower priority tasks*

---

## Documentation (On Hold)

### Medium Priority

- [ ] **Multiplayer architecture document** — Detailed technical document describing the client-server model, prediction/reconciliation, interpolation, and sync strategies. Include diagrams for packet flow and tick model. **[CPX: 2]**
- [ ] **Hosting guide** — Instructions for hosting a dedicated server and a listen server. CLI arguments, port forwarding requirements, server configuration. **[CPX: 1]**

### Lower Priority

- [ ] **Network protocol reference** — Document all packet types, their binary formats, and field descriptions. Auto-generate from packet class XML docs if possible. **[CPX: 1]**

---

## Known Issues / Bugs

### Active Bugs

*No active bugs*

### Resolved Bugs

*Moved to [DONE_MULTIPLAYER.md](DONE_MULTIPLAYER.md)*

### Uncategorized

- Make light recalculations happen only on visible frustum culled chunks to speed things up. Make the render limit 8 chunks.

---

## Notes

- The existing fixed timestep (0.015s / 66.6 Hz) is ideal for networked simulation — do not change the tick rate
- `Player.LocalPlayer` bool already exists, confirming multiplayer was anticipated in the original design
- Existing binary serialization for Player and ChunkMap provides a strong foundation for network packets
- Quake-style physics are well-documented for client-side prediction — extensive prior art in Quake/Source engine netcode
- Maintain the "dependency-free" philosophy — use only `System.Net.Sockets`, no external networking libraries
- The Mod System (TODO_MODS.md) should be designed with multiplayer in mind
- WORLDBUILDING.md describes cooperative survival gameplay for up to 10 players — this is the target player count
- All gameplay-affecting logic must run on the server; clients are presentation layers with prediction for responsiveness
- Single-player mode has been replaced by listen server — Host Game is the only way to play (hosts a local server and connects to it)
- `FPSCamera` static-to-instance refactoring is the most critical prerequisite — almost every other task depends on supporting multiple player instances
- FishUI documentation is in `FishUI.xml`, usage examples are in `data/FishUISamples/Samples/` folder
- Weapon JSON models use Blockbench format with named parts: `gun_body` (main body), `grip` (hand attachment point), `projectile` (muzzle/spawn point for fire effects). Gun axis: Y+ top, Z- forward (away from player), X+ right
- `viewmodel_arm.json` has `arm` and `hand` parts with parent-child hierarchy. Hand is the attachment point for weapon grip
- `CustomModel`/`CustomMesh` system extended with `SetTexture()`, `DrawWithMatrix()`, tinted `Draw()` for viewmodel rendering. `ResMgr.GetModelTexture()` loads textures from `data/models/` directory

---

## Suggested Implementation Order

Steps 1–24 and 26 completed — see [DONE_MULTIPLAYER.md](DONE_MULTIPLAYER.md).

25. **Chat + UI** → social features and menus
27. **Testing tools** → network simulation, loopback
28. **Documentation** → guides and references

---

## Completed

*Moved to [DONE_MULTIPLAYER.md](DONE_MULTIPLAYER.md)*
