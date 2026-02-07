# Aurora Falls - Multiplayer System TODO

Planned tasks for implementing multiplayer support (up to 10 players).

> **CPX (Complexity Points)** - 1 to 5 scale:
> - **1** - Single file control/component
> - **2** - Single file control/component with single function change dependencies
> - **3** - Multi-file control/component or single file with multiple dependencies, no architecture changes
> - **4** - Multi-file control/component with multiple dependencies and significant logic, possible minor architecture changes
> - **5** - Large feature spanning multiple components and subsystems, major architecture changes

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
| **ViewModel** | Local view model rendering | No changes — client-only, each client renders own ViewModel |

---

## Core Refactoring

> These tasks make the existing single-player code multiplayer-ready without adding any networking yet. The game must remain fully functional in single-player after each refactoring step.

### High Priority

- [x] **Project split phase 1: Move Raylib-independent code to VoxelgineEngine** ✅
- [x] **Project split phase 2: Split mixed Raylib/logic classes** ✅

### Medium Priority

- [x] **EntityManager: Network entity IDs** ✅
- [x] **Player.Serialization: Network snapshot format** ✅
- [x] **VoxEntity: Network serialization** ✅

### Lower Priority

- [x] **WeaponGun: Separate fire intent from hit resolution** ✅
- [x] **DayNightCycle: External time source** ✅

---

## Networking Infrastructure

> Low-level networking: transport, connection management, packet serialization. No game logic here.

### High Priority

- [x] **UDP transport layer** ✅
- [x] **Packet serialization framework** ✅
- [x] **Reliable delivery layer** ✅
- [x] **Connection manager** ✅

### Medium Priority

- [ ] **Bandwidth management** — Implement send rate limiting and packet batching. Combine multiple small packets into a single UDP datagram (up to MTU ~1200 bytes). Prioritize unreliable snapshots over reliable messages when near bandwidth limit. Track bytes/sec sent and received per connection for diagnostics. **[CPX: 3]**
- [ ] **Packet fragmentation** — Large packets (world data transfer) exceed MTU. Implement fragmentation: split large reliable packets into numbered fragments, reassemble on receive, ACK only when all fragments arrive. Used primarily for `WorldData` packets during client connect. **[CPX: 3]**

---

## Server Implementation

> Server-side game logic. Processes client inputs, runs authoritative simulation, broadcasts state.

### High Priority

- [x] **NetServer core** ✅
- [x] **Server game loop** ✅
- [x] **Server player management** ✅
- [x] **Server input processing** ✅
- [x] **Server world transfer** ✅
- [x] **Server block change authority** ✅

### Medium Priority

- [x] **Server combat authority** ✅
- [x] **Server entity synchronization** ✅
- [x] **Server time sync** ✅

### Lower Priority

- [ ] **Listen server mode** — Allow a player to host and play simultaneously. The hosting client runs `NetServer` + `GameSimulation` + rendering. Local player's input bypasses networking (applied directly to simulation). Remote players connect normally. `Program.cs` flag `--host --port 7777` enables this mode alongside normal client rendering. **[CPX: 3]**
- [ ] **Server console/admin commands** — Headless server reads stdin for commands: `kick <player>`, `ban <player>`, `say <message>`, `time <hours>`, `save`, `quit`. For listen server, these are available via the debug menu (F1). **[CPX: 2]**

---

## Client Implementation

> Client-side networking, prediction, interpolation, and rendering of remote players.

### High Priority

- [x] **NetClient core** ✅
- [x] **Client input sending** ✅
- [x] **Client-side prediction** ✅
- [x] **Client world loading** ✅
- [x] **Remote player rendering** ✅
- [x] **Client block change handling** ✅

### Medium Priority

- [x] **Remote player interpolation buffer** ✅
- [x] **Client entity synchronization** ✅
- [x] **Client combat effects** ✅

### Lower Priority

- [ ] **Client disconnect handling** — Detect server timeout (no packets for 10s) or explicit `Disconnect` packet. Show "Connection Lost" overlay. Option to reconnect or return to main menu. Clean up all network state, remote players, and restore single-player-like state. **[CPX: 2]**
- [ ] **Network statistics HUD** — Debug overlay (toggle with key) showing: ping, packet loss %, bytes/sec in/out, server tick rate, prediction error count, interpolation buffer health. Useful for development and player diagnostics. **[CPX: 2]**

---

## Synchronization

> Specific sync strategies for individual game systems.

### High Priority

- [x] **Player position sync** ✅
- [x] **World block sync** ✅

### Medium Priority

- [x] **Entity state sync** ✅
- [ ] **Inventory sync** — Server tracks each player's inventory. Item pickup/use/drop are server-authoritative. Server sends `InventoryUpdate` packets on change. Client displays inventory from server data. Local prediction for immediate feedback (item count decrement on use), server corrects if needed. **[CPX: 3]**

### Lower Priority

- [ ] **Particle/sound sync** — Server sends compact event packets for gameplay-relevant effects (weapon fire, block break, explosion). Clients spawn particles and play sounds locally based on these events. Ambient particles (smoke) remain client-only. **[CPX: 2]**
- [ ] **Day/night sync** — Server periodically sends current time. Client smoothly transitions to server time (lerp, not snap, to avoid visual pop). **[CPX: 1]**

---

## Gameplay Features

> Multiplayer-specific gameplay features built on top of the networking layer.

### High Priority

- [x] **Player health system** ✅
- [x] **Player respawn system** ✅

### Medium Priority

- [ ] **Text chat** — Client sends `ChatMessage` to server, server broadcasts to all. Display chat messages in a scrollable FishUI panel anchored to bottom-left. Fade out after 10 seconds. Toggle chat input with Enter key. Support `/commands` for admin actions. **[CPX: 3]**
- [ ] **Player name tags** — Render player display names above remote player models using billboard text. Scale with distance, fade at long range, hidden when obstructed by blocks. **[CPX: 2]**
- [ ] **Kill feed** — Display a temporary message when a player kills another player (e.g., "PlayerA killed PlayerB with Gun"). Server sends kill event, clients display in top-right corner with fade-out timer (FishUI Toast notify). **[CPX: 1]**
- [ ] **Player model** — Create or assign a visible 3D model for remote players (could reuse/adapt NPC model). Animate walk cycle based on velocity. Play attack animation when player fires weapon or places/destroys blocks (sync via animation state byte in `WorldSnapshotPacket`). Rotate head bone based on camera pitch (look direction) so remote players visually look where they aim. Show held weapon model in hand. **[CPX: 3]**

### Lower Priority

- [ ] **Scoreboard** — Tab key shows overlay with all connected players: name, kills, deaths, ping. Server tracks stats and broadcasts periodically. **[CPX: 2]**

---

## UI & Menus

### High Priority

- [x] **Connect to server UI** ✅
- [ ] **Host game UI** — FishUI dialog from main menu: port input (default 7777), max players slider (2-10), "Host" button that starts listen server and enters gameplay. Option to load existing save or generate new world. **[CPX: 2]**

### Medium Priority

- [ ] **Player list overlay** — In-game FishUI panel (toggle with Tab or similar key) showing connected players, their ping, and status. Visible during gameplay. **[CPX: 2]**
- [ ] **Connection status indicator** — Small HUD element showing connection quality (ping bar icon, green/yellow/red based on latency). Show "Reconnecting..." text when experiencing packet loss. **[CPX: 1]**

### Lower Priority

- [ ] **Server browser (LAN)** — Broadcast UDP discovery on LAN. Servers respond with name, player count, map info. Client displays list of discovered servers. Select to connect. **[CPX: 3]**

---

## Testing & Debugging

### High Priority

*No active high test priority items*

### Medium Priority

- [ ] **Network simulation** — Debug options to simulate bad network conditions: artificial latency (add delay to packet delivery), packet loss (randomly drop X% of packets), jitter (randomize latency). Configurable via debug menu. Essential for testing prediction/interpolation robustness. **[CPX: 2]**
- [ ] **Network packet logger** — Debug option to log all sent/received packets with timestamps, types, sizes, and source/destination. Output to `IFishLogging`. Toggle via debug menu. **[CPX: 1]**

### Lower Priority

- [ ] **Multiplayer unit tests** — Test packet serialization round-trips, reliable delivery layer ACK/retransmit logic, snapshot interpolation math, prediction reconciliation correctness. Add to existing `UnitTest` project. **[CPX: 3]**

---

## Documentation

### High Priority

- [ ] **WORLDBUILDING.md: Multiplayer review** — Analyze and update worldbuilding document for multiplayer support (up to 10 players), consider cooperative/competitive elements, shared resources, base building **[CPX: 2]**

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

- ~~**Newly connected player does not see existing players' avatars** — `PlayerJoinedPacket` for existing players was sent before world data, but `MultiplayerGameState.HandlePlayerJoined()` discarded them because `_simulation` was null. Fixed by buffering `PlayerJoinedPacket` arrivals in `_pendingPlayerJoins` list and replaying them after world loading completes in `OnWorldDataReady()`.~~
- ~~**Server weapon raycast returns block center instead of precise face hit point** — `HandleWeaponFire()` used `ChunkMap.RaycastPos()` which returns integer block coordinates, then offset by (0.5, 0.5, 0.5) to get block center. This caused weapon tracers and spark/fire particles to appear at block centers instead of on the surface where the shot actually hit. Fixed by adding `ChunkMap.RaycastPrecise()` which computes the exact ray-plane intersection point on the hit block face using the DDA face normal. `HandleWeaponFire()` now uses `RaycastPrecise()`.~~
- ~~**Fire/spark effects not visible when shooting blocks in multiplayer** — `ChunkMap.RaycastPrecise()` had the face normal plane mapping inverted for all three axes. The DDA face normal is `-Step` (e.g., face `(-1,0,0)` means the ray stepped +X, entering through the block's -X face at `blockPos.X`), but the code mapped `face < 0` to `blockPos + 1` instead of `blockPos`. This placed the hit point on the far side of the block (inside the adjacent block), so fire/spark particles were spawned inside solid geometry and invisible. Fixed by inverting the plane value conditions: `face > 0` → `blockPos + 1` (ray entered +X face), `face < 0` → `blockPos` (ray entered -X face), and similarly for Y and Z.~~
- ~~**Single-player weapon effects not showing** — `Eng.MultiplayerGameState` is always non-null (created at startup in `Program.cs`), so `WeaponGun.OnLeftClick` always took the multiplayer path: sending a `WeaponFirePacket` to a non-existent server and returning before `ResolveFireIntent()`/`ApplyHitEffects()`. Fixed by adding `IsActive` property to `MultiplayerGameState` (checks `_initialized && _client.IsConnected`) and changing the guard in `OnLeftClick` from `mpState != null` to `mpState != null && mpState.IsActive`.~~

### Uncategorized

*No uncategorized items*

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
- When implementing, always ensure single-player mode continues to work (single-player = listen server with no remote connections)
- `FPSCamera` static-to-instance refactoring is the most critical prerequisite — almost every other task depends on supporting multiple player instances

---

## Suggested Implementation Order

1. ~~**FPSCamera instance refactoring**~~ → ✅ Done
2. ~~**Player ID + PlayerManager**~~ → ✅ Done
3. ~~**InputMgr abstraction**~~ → ✅ Done
4. ~~**ChunkMap block change tracking**~~ → ✅ Done
5. ~~**GameState simulation separation**~~ → ✅ Done
6. ~~**Project split phase 1**~~ → ✅ Done
7. ~~**Project split phase 2**~~ → ✅ Done
8. ~~**Entity network IDs**~~ → ✅ Done
9. ~~**Serialization extensions**~~ → ✅ Done
10. ~~**UDP transport + packet framework**~~ → ✅ Done
11. ~~**Reliable delivery layer**~~ → ✅ Done
12. ~~**Connection manager**~~ → ✅ Done
13. ~~**NetServer + NetClient cores**~~ → ✅ Done
14. ~~**Server game loop + player management**~~ → ✅ Done
15. ~~**Client input sending + world loading**~~ → ✅ Done
16. ~~**Player position sync + remote rendering**~~ → ✅ Done
17. ~~**Client-side prediction + reconciliation**~~ → ✅ Done
18. ~~**Multiplayer game state + end-to-end demo**~~ → ✅ Done
19. ~~**Block sync (server + client)**~~ → ✅ Done
20. ~~**Remote player interpolation**~~ → ✅ Done
21. ~~**Entity sync**~~ → ✅ Done
22. ~~**Weapon fire authority**~~ → ✅ Done
23. ~~**Player health + respawn**~~ → ✅ Done
24. **Chat + UI** → social features and menus
25. **Listen server mode** → host-and-play
26. **Bandwidth management + fragmentation** → production-ready networking
27. **Testing tools** → network simulation, loopback
28. **Documentation** → guides and references

---

## Completed

- **FPSCamera: Convert from static to instance-based** — Refactored `FPSCamera` to instantiable class with instance fields. `Player` owns `Camera` field, created in constructor with config sensitivity. Updated all references in `Player.cs`, `Player.Input.cs`, `Player.Physics.cs`, `GameWindow.cs`, `Program.cs`. Build verified.
- **Player: Add player ID and PlayerManager** — Added `int PlayerId` property to `Player` (constructor parameter, default 0). Created `PlayerManager` class with `Dictionary<int, Player>`, `AddPlayer()`, `AddLocalPlayer()`, `RemovePlayer()`, `GetPlayer()`, `GetAllPlayers()`, `GetLocalPlayer()`, and `LocalPlayer` convenience property. `GameState` now holds `PlayerManager Players` with `LocalPlayer` shortcut property. All `Ply` references updated across `GameState.cs`, `GameWindow.cs`, `EntityManager.cs`, `VEntSlidingDoor.cs`. Single-player creates player with ID 0. Build verified.
- **InputMgr: Abstract input source** — Created `IInputSource` interface with `Poll(float gameTime)` method. Implemented `LocalInputSource` (wraps Raylib keyboard/mouse polling via `GameConfig` key mappings) and `NetworkInputSource` stub (stores last received `InputState` from network). Refactored `InputMgr` to take `IInputSource` in constructor with `SetInputSource()` for runtime swapping. Moved Raylib polling out of `InputMgr` — it now has zero Raylib dependency. `GameWindow` creates `LocalInputSource` and passes it to `InputMgr`. All existing consumers unchanged. Build verified.
- **ChunkMap: Block change tracking** — Created `BlockChange` readonly struct (`X`, `Y`, `Z`, `OldType`, `NewType`) in `Voxelgine/Graphics/Chunk/BlockChange.cs`. Added `_blockChangeLog` (`List<BlockChange>`) field to `ChunkMap` with `GetPendingChanges()` (returns `IReadOnlyList<BlockChange>`) and `ClearPendingChanges()` methods. `SetPlacedBlock()` now reads the old block type before modification and logs the change if the type differs. `SetPlacedBlockNoLighting()` (internal chunk operations) intentionally excluded from logging. World generation changes can be cleared after generation. Build verified.
- **GameState: Separate simulation from presentation** — Created `GameSimulation` class (`Voxelgine/Engine/GameSimulation.cs`) that owns authoritative game state: `ChunkMap Map`, `PlayerManager Players`, `EntityManager Entities`, `DayNightCycle DayNight`, `PhysData PhysicsData`. `GameState` now holds `GameSimulation Simulation` with delegate properties (`Map`, `Players`, `LocalPlayer`, `DayNight`, `Entities`) for backward compatibility, plus client-only systems (`ParticleSystem`, `SoundMgr`, `FishUIManager`, rendering). `VoxEntity` stores `GameSimulation` instead of `GameState` (`GetSimulation()`/`SetSimulation()`). `EntityManager.Spawn()` takes `GameSimulation`. Removed unused `IGameWindow` dependency from `EntityManager`. Removed `using Voxelgine.States` from entity classes. Headless server can now run `GameSimulation` without any presentation layer. Build verified.
- **Project split phase 1: Move Raylib-independent code to VoxelgineEngine** — Moved 16 Raylib-free files from `Voxelgine` to `VoxelgineEngine`, maintaining directory structure. **Moved files:** DI interfaces (`IFishLogging`, `IFishConfig`, `IFishDebug`, `IFishClipboard`), `FishDI` (DI container), `PhysData` (physics constants), `InputMgr`/`IInputSource`/`NetworkInputSource` (input abstraction), `Noise` (simplex noise), `SpatialHashGrid`, `ThreadWorker`, `FishLogging`, `Debug`, `SettingsHiddenAttribute`, `OnKeyPressedEventArg`. **VoxelgineEngine.csproj** updated with `AllowUnsafeBlocks`, `Microsoft.Extensions.Hosting` 10.0.2, `TextCopy` 6.2.1 NuGet packages. `SettingsHiddenAttribute` made `public` for cross-assembly visibility. Removed placeholder `EngineShared.cs`. **Cannot move yet (Phase 2):** Files referencing Raylib types (`AABB`, `GameFrameInfo`, `DayNightCycle`), files depending on `ChunkMap`/`VoxEntity`/`Player` (`PhysicsUtils`, `Raycast`, `GameSimulation`, `PlayerManager`, `EntityManager`, pathfinding), animation files depending on `IFishEngineRunner` (defined in `IFishProgram.cs` which references `Voxelgine.States`). Full solution build verified (0 errors).
- **Project split phase 2: Split mixed Raylib/logic classes** — Extracted Raylib-free math and logic from mixed files and moved to `VoxelgineEngine`. **AABB:** Moved `AABB` struct to `VoxelgineEngine/Engine/Physics/AABB.cs` (removed `BoundingBox` constructor and `ToBoundingBox()` method). Created `AABBExtensions.cs` in `Voxelgine/Engine/Physics/` with `ToAABB(this BoundingBox)` and `ToBoundingBox(this AABB)` extension methods for Raylib bridge. Updated `Chunk.Rendering.cs` callers to use `.ToAABB()`. **PhysicsUtils:** Split into pure math (`VoxelgineEngine/Engine/Physics/PhysicsUtils.cs` — `ClipVelocity`, `Accelerate`, `AirAccelerate`, `ApplyFriction`, `ApplyGravity`, `CreatePlayerAABB`, `CreateEntityAABB`) and world collision (`Voxelgine/Engine/Physics/WorldCollision.cs` — `CollidesWithWorld`, `MoveWithCollision` requiring `ChunkMap`). `CreatePlayerAABB` default parameters changed from `Player.*` constants to literal values (0.4f, 1.7f, 1.6f). Updated `EntityManager.cs` to use `WorldCollision.MoveWithCollision`. **Raycast:** Extracted `RayIntersectsAABB` (slab method) to `VoxelgineEngine/Engine/Physics/RayMath.cs`. `Raycast.cs` retained entity-dependent methods calling `RayMath.RayIntersectsAABB`. **Animations:** Moved `LerpManager.cs` (`ILerpManager`, `LerpManager`, `Easing`), `AnimLerp.cs`, and `AnimLerpImpl.cs` (`LerpVec3`, `LerpQuat`, `LerpFloat`) to `VoxelgineEngine/Engine/Animations/`. `LerpManager` made `public` (was internal). `AnimLerp` constructor refactored from `IFishEngineRunner` to `ILerpManager` parameter. Updated `ViewModel.cs` and `VEntPickup.cs` to resolve `ILerpManager` from DI and pass to lerp constructors. **Remaining in Voxelgine (cannot move):** `VoxEntity` (Raylib Model/Color/Draw), `EntityManager` (VoxEntity+ChunkMap), `GameSimulation` (ChunkMap), `PlayerManager` (Player), `GameFrameInfo` (Camera3D/Frustum), `DayNightCycle` (Raylib), pathfinding (ChunkMap), `NPCAnimator` (CustomModel), `IFishEngineRunner` (Voxelgine.States types). Full solution build verified (0 errors).
- **EntityManager: Network entity IDs** — Added `int NetworkId` property to `VoxEntity` (auto-assigned by `EntityManager`, `internal set`, default 0 = unassigned). `EntityManager` now maintains `Dictionary<int, VoxEntity>` alongside the entity list for O(1) lookup by network ID. IDs are assigned incrementally starting from 1 in `Spawn()`. Added `GetEntityByNetworkId(int)` for lookup, `Remove(int networkId)` and `Remove(VoxEntity)` for entity removal (both clean up list and dictionary). Added `GetEntityCount()` for diagnostics. Entity subclasses (`VEntPickup`, `VEntNPC`, `VEntSlidingDoor`, `VEntPlayer`) unchanged — IDs are assigned at the `EntityManager` level. Build verified.
- **Player.Serialization: Network snapshot format** — Added `WriteSnapshot(BinaryWriter, int serverTick)` and `ReadSnapshot(BinaryReader)` → `int` to `Player.Serialization.cs` for compact network state synchronization (42 bytes per snapshot: tick, position, velocity, camera angle, animation state byte, weapon index byte). Added `SetVelocity(Vector3)` to `Player.Physics.cs` alongside existing `GetVelocity()`. Added `GetSelectedInventoryIndex()` / `SetSelectedInventoryIndex(int)` to `Player.GUI.cs` wrapping `FishUIInventory` with null safety. Existing `Write()`/`Read()` methods preserved for save file serialization. Build verified.
- **VoxEntity: Network serialization** — Added `WriteSnapshot(BinaryWriter)` / `ReadSnapshot(BinaryReader)` to `VoxEntity` base class writing position (12B), velocity (12B), and rotation state (5B). Added `virtual WriteSnapshotExtra` / `ReadSnapshotExtra` extension points for subclass-specific state. `VEntNPC` overrides to serialize look direction (12B) and current animation name (string). `VEntSlidingDoor` overrides to serialize door state enum (1B) and slide progress (4B), with collision and position recalculated on read. `VEntPickup` and `VEntPlayer` have no extra network state — bobbing/particles are cosmetic client-only effects. Build verified.
- **DayNightCycle: External time source** — Added `bool IsAuthority` property to `DayNightCycle` (default `true`). When `IsAuthority` is false, `Update()` skips time advancement entirely — time only changes via the existing `SetTime(float hours)` method (called by server sync). Single-player and server mode use `IsAuthority = true` (default), multiplayer clients set `false`. `SetTime()` already existed and continues to work regardless of `IsAuthority` state. Build verified.
- **WeaponGun: Separate fire intent from hit resolution** — Created `FireIntent` readonly struct (origin, direction, max range, weapon type, source player) and `FireResult` readonly struct (hit type, position, normal, distance, entity, body part name) with `FireHitType` enum (None/World/Entity) in `Voxelgine/Engine/Weapons/FireIntent.cs`. Refactored `WeaponGun.OnLeftClick` into three stages: (1) `ApplyFireEffects()` — immediate feedback (kickback, sound) that plays before hit resolution; (2) `ResolveFireIntent()` — performs world + entity raycasts, NPC body part detection, returns `FireResult`; (3) `ApplyHitEffects()` — spawns tracer, blood particles, fire sparks based on result. In single-player, all three stages run locally in sequence. In multiplayer, client runs stage 1 immediately, sends intent to server; server runs stage 2 authoritatively; client runs stage 3 on server confirmation. `ResolveFireIntent` is `public` so the server can call it directly. Game behavior unchanged in single-player. Build verified.
- **UDP transport layer** — Created `UdpTransport` class in `VoxelgineEngine/Engine/Net/UdpTransport.cs` wrapping `System.Net.Sockets.UdpClient`. Server mode: `Bind(int port)` binds to a UDP port and starts async receive loop. Client mode: `Open()` creates socket on ephemeral port. `SendTo(byte[] data, IPEndPoint target)` sends datagrams with thread-safe locking. `OnDataReceived` event (`Action<byte[], IPEndPoint>`) fires on the receive task thread when data arrives. Async receive loop uses `UdpClient.ReceiveAsync(CancellationToken)` with graceful handling of `SocketException`, `OperationCanceledException`, and `ObjectDisposedException`. `Close()`/`Dispose()` cancels the receive loop and releases the socket. No external dependencies — uses only `System.Net.Sockets`. Build verified.
- **Packet serialization framework** — Created `Packet.cs` in `VoxelgineEngine/Engine/Net/` with `PacketType` enum (24 types matching protocol table), `BinaryExtensions` (WriteVector3/ReadVector3, WriteVector2/ReadVector2), abstract `Packet` base class (`Type`, `Write(BinaryWriter)`, `Read(BinaryReader)`, `Serialize()` → `byte[]`, static `Deserialize(byte[])` → `Packet`), and `PacketRegistry` (maps `PacketType` → factory `Func<Packet>`, static constructor registers all 24 types). Implemented all 24 packet classes with full field definitions and binary serialization across 6 files: `ConnectionPackets.cs` (Connect, ConnectAccept, ConnectReject, Disconnect, PlayerJoined, PlayerLeft), `StatePackets.cs` (InputStatePacket with `PackKeys`/`UnpackKeys` bitmask conversion for `InputState` struct, PlayerSnapshotPacket, WorldSnapshotPacket with `PlayerEntry[]` array), `WorldPackets.cs` (BlockChange, BlockPlaceRequest, BlockRemoveRequest, WorldData with fragment index + byte array, WorldDataComplete with checksum), `EntityPackets.cs` (EntitySpawn with properties byte array, EntityRemove, EntitySnapshot), `CombatPackets.cs` (WeaponFire, WeaponFireEffect, PlayerDamage), `MiscPackets.cs` (ChatMessage, DayTimeSync, Ping, Pong). All in `Voxelgine.Engine` namespace, `VoxelgineEngine` project. Build verified.
- **Reliable delivery layer** — Created `ReliableChannel` class in `VoxelgineEngine/Engine/Net/ReliableChannel.cs`. Implements reliability on top of UDP with a 9-byte protocol header: `[reliable flag:1][sequence:2][ack sequence:2][ack bitfield:4]` prepended to packet data. `Wrap(byte[] packetData, bool reliable, float currentTime)` assigns monotonically increasing sequence numbers to reliable packets (skipping 0), stores them in a `Dictionary<ushort, PendingPacket>` send buffer, and all packets (reliable and unreliable) carry piggybacked ACK data. `Unwrap(byte[] rawData)` strips the header, calls `ProcessAck()` to remove acknowledged packets from send buffer using the ack sequence + 32-bit bitfield, and `TrackReceivedSequence()` detects duplicates via a sliding receive window (remote sequence + 32-bit bitfield with `SequenceDiff` handling ushort wraparound). `GetRetransmissions(float currentTime, float timeout)` collects unACKed packets past timeout and re-wraps with current ACK data. Data flow: `Packet.Serialize()` → `ReliableChannel.Wrap()` → `UdpTransport.SendTo()` and reverse `UdpTransport` → `ReliableChannel.Unwrap()` → `Packet.Deserialize()`. Clean separation — channel operates on raw bytes, unaware of Packet types. Build verified.
- **Connection manager** — Created `NetConnection` class in `VoxelgineEngine/Engine/Net/NetConnection.cs` representing a connection to a remote endpoint. `ConnectionState` enum (Connecting/Connected/Disconnected) tracks lifecycle. `NetConnection` wraps a `ReliableChannel` per connection with `WrapPacket(Packet, bool reliable, float currentTime)` → `byte[]` and `UnwrapPacket(byte[] rawData, float currentTime)` → `Packet` (with try-catch for malformed data). RTT measurement via `CreatePing(float currentTime)` / `CreatePong(long timestamp)` / `ProcessPong(PongPacket)` using `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()` with exponential smoothing (80/20 weight). `ShouldSendPing(float currentTime)` checks 1-second interval. Timeout detection via `HasTimedOut(float currentTime)` with 10-second default. Tracks `PlayerId` (default -1), `PlayerName`, and exposes `RoundTripTime` (seconds) / `RoundTripTimeMs` (int). `Channel` property exposes underlying `ReliableChannel` for retransmission handling. Server holds `Dictionary<IPEndPoint, NetConnection>` for all clients; client holds single `NetConnection` to server. Build verified.
- **NetServer core** — Created `NetServer` class in `VoxelgineEngine/Engine/Net/NetServer.cs`. Binds UDP port via `UdpTransport`, accepts client connections with protocol version validation (`ProtocolVersion = 1`), player ID assignment (0-9 via `AllocatePlayerId()`), and rejection when full (`MaxPlayers = 10`). Manages connections via `Dictionary<IPEndPoint, NetConnection>` for routing and `Dictionary<int, NetConnection>` for player ID lookup. Thread-safe receive queue (`ConcurrentQueue`) decouples transport background thread from game thread — all packet processing occurs during `Tick(float currentTime)`. System packets handled internally: `ConnectPacket` (validate version, assign ID, send `ConnectAcceptPacket`), `DisconnectPacket` (remove connection), `PingPacket` (reply with `PongPacket`), `PongPacket` (update RTT). All other packets forwarded via `OnPacketReceived` event. `Tick()` also handles retransmissions (`ReliableChannel.GetRetransmissions`), periodic ping sending, and timeout detection (10s). Public API: `SendTo(int playerId, Packet, bool reliable, float)`, `Broadcast(Packet, bool reliable, float)`, `BroadcastExcept(int excludePlayerId, ...)`, `Kick(int playerId, string reason, float)`. Events: `OnClientConnected`, `OnClientDisconnected`, `OnPacketReceived`. `ServerTick` counter incremented each `Tick()` call. `IDisposable` with `Stop()`/`Dispose()` cleanup. Build verified.
- **NetClient core** — Created `NetClient` class in `VoxelgineEngine/Engine/Net/NetClient.cs`. `ClientState` enum (Disconnected/Connecting/Loading/Playing) tracks the full client lifecycle. `Connect(string host, int port, string playerName, float currentTime)` opens `UdpTransport` on ephemeral port, resolves hostname via `Dns.GetHostAddresses`, creates `NetConnection` to server, and sends `ConnectPacket` with `NetServer.ProtocolVersion`. Thread-safe `ConcurrentQueue<byte[]>` queues incoming datagrams filtered by server endpoint. `Tick(float currentTime)` drains queue, handles retransmissions (`ReliableChannel.GetRetransmissions`), periodic pings, and timeout detection (state-aware messages: "Connection attempt timed out" vs "Connection to server timed out"). System packets handled internally: `ConnectAcceptPacket` (assigns PlayerId, transitions Connecting→Loading), `ConnectRejectPacket` (fires `OnConnectionRejected` + `OnDisconnected`), `DisconnectPacket` (cleanup + `OnDisconnected`), `PingPacket` (reply Pong), `PongPacket` (update RTT). All other packets forwarded via `OnPacketReceived`. `Send(Packet, bool reliable, float)` sends to server. `FinishLoading()` transitions Loading→Playing (called by game loop after world data received). `Disconnect(string reason, float)` sends `DisconnectPacket` and cleans up. Exposes `PlayerId`, `PlayerName`, `RoundTripTime`/`RoundTripTimeMs`, `IsConnected`. `Cleanup()` resets all state, closes transport, drains queue. `IDisposable`. Build verified.
- **Server game loop** — Created `ServerLoop` class in `VoxelgineServer/ServerLoop.cs` as the dedicated headless server. Added `Voxelgine` project reference to `VoxelgineServer.csproj` for access to `GameSimulation`, `ChunkMap`, `EntityManager`, etc. `ServerLoop` owns `NetServer` + `GameSimulation`, sets up DI (FishDI with `ServerConfig : IFishConfig` and `ServerEngineRunner : IFishEngineRunner` as private inner classes — null presentation properties for headless operation). `Start(int port, int worldSeed)` generates world via `ChunkMap.GenerateFloatingIsland()`, starts `NetServer`, and enters fixed timestep loop (Stopwatch-based, 0.015s DeltaTime matching client). Each tick: `NetServer.Tick()` → `DayNightCycle.Update()` → `EntityManager.UpdateLockstep()`. Includes CPU-friendly `Thread.Sleep(1)` when no ticks pending. `Stop()` signals graceful shutdown via volatile bool. Events log player connect/disconnect. Rewrote `VoxelgineServer/Program.cs` with CLI argument parsing (`--port <port>`, `--seed <seed>`, `--help`) and `Console.CancelKeyPress` for Ctrl+C shutdown. Fixed `VEntPickup.UpdateLockstep` null guard — particle spawning now checks `Eng.GameState is GameState gs` to prevent NullReferenceException on headless server. Build verified.
- **Server player management** — Added server-side `Player` constructor overload `Player(IFishEngineRunner, int playerId)` in `Player.cs` that creates a player instance without GUI, SoundMgr, ViewModel, or Raylib calls — suitable for headless server simulation. Added null guards for `Snd` in `Player.Physics.cs`: `PhysicsHit()` early-returns if `Snd` is null, swimming sound in `UpdateSwimmingPhysics` guarded with `Snd != null` check. Added `int WorldSeed` property to `NetServer` — `HandleNewConnection` now includes the correct seed in `ConnectAcceptPacket`. Expanded `ServerLoop` event handlers: `OnClientConnected` creates `Player` with server constructor, sets `DefaultSpawnPosition` (32, 73, 19), sends `PlayerJoinedPacket` for all existing players to the new client, adds player to `PlayerManager`, broadcasts `PlayerJoinedPacket` for new player to all others. `OnClientDisconnected` removes player from `PlayerManager` and broadcasts `PlayerLeftPacket` to remaining clients. Added `GetPlayerName(int playerId)` helper that queries `NetServer.GetConnection()`. Stored `IFishEngineRunner` reference and world seed in `ServerLoop` fields. Build verified.
- **Client input sending** — Created `ClientInputBuffer` class in `VoxelgineEngine/Engine/Net/ClientInputBuffer.cs` implementing a circular buffer of 128 entries (`BufferedInput` struct: tick number, `InputState`, camera angle `Vector2`). `Record(int tickNumber, InputState state, Vector2 cameraAngle)` stores input in the buffer and returns a ready-to-send `InputStatePacket` with keys packed via `PackKeys()`, camera angle, and mouse wheel. `TryGetInput(int tickNumber, out BufferedInput)` retrieves a specific tick's input. `GetInputsInRange(int afterTick, int upToTick)` returns all buffered inputs in a tick range ordered by tick number — designed for prediction reconciliation replay. `Clear()` resets the buffer. Added `int LocalTick` property to `NetClient` — initialized from `ConnectAcceptPacket.ServerTick` in `HandleConnectAccept()` so the client tick counter starts synchronized with the server. Reset to 0 in `Cleanup()`. The game loop increments `LocalTick` each fixed timestep, calls `ClientInputBuffer.Record()` to create the packet, and sends it unreliably via `NetClient.Send()`. Build verified.
- **Server world transfer** — Created `WorldTransferManager` class in `VoxelgineEngine/Engine/Net/WorldTransferManager.cs` that fragments GZip-compressed world data into `WorldDataPacket`s (1024 bytes each) and sends them reliably at a rate-limited pace (8 fragments per tick ≈ 546 KB/s at 66.6 Hz). `BeginTransfer(int playerId, byte[] compressedWorldData)` splits the data, computes a 32-bit FNV-1a checksum, and queues the transfer. `Tick(float currentTime)` sends the next batch of fragments for all pending transfers; when all fragments are sent, sends `WorldDataCompletePacket` with total fragment count and checksum, then fires `OnTransferComplete` event. `CancelTransfer(int playerId)` aborts a transfer (e.g., on disconnect). `HasPendingTransfer()` and `ActiveTransferCount` for diagnostics. Integrated into `ServerLoop`: added `WorldTransferManager` field initialized alongside `NetServer`. `OnClientConnected` now serializes the `ChunkMap` via `SerializeWorld()` helper (`ChunkMap.Write()` → `MemoryStream.ToArray()`) and calls `BeginTransfer()`. `Tick()` calls `_worldTransfer.Tick()` after network processing. `OnClientDisconnected` calls `CancelTransfer()`. `OnWorldTransferComplete` logs completion. Server log shows compressed size and fragment count for each transfer. Build verified.
- **Server input processing** — Implemented server-side player input processing pipeline in `ServerLoop.cs`. Added per-player `InputMgr` + `NetworkInputSource` dictionaries, created in `OnClientConnected` and cleaned up in `OnClientDisconnected`. `OnPacketReceived` handles `InputStatePacket`: unpacks key bitmask via `UnpackKeys()` into `InputState`, sets camera angle on the player's `FPSCamera` (Vector2 yaw/pitch → Vector3 with Z=0), and feeds the state into the player's `NetworkInputSource`. Missing inputs are automatically handled — `NetworkInputSource` retains its last state, so the previous input is repeated if no packet arrives for a tick. `ProcessPlayerPhysics(float dt)` iterates all players each tick: ticks `InputMgr` (polls `NetworkInputSource`), calls `Player.UpdateDirectionVectors()` to update Fwd/Left/Up from camera angle, then calls `Player.UpdatePhysics(ChunkMap, PhysData, dt, InputMgr)` for authoritative Quake-style movement. Added `Player.UpdateDirectionVectors()` method in `Player.cs` as a server-friendly alternative to `UpdateFPSCamera(ref GameFrameInfo)` — same logic (reads `Camera.GetForward/Left/Up()`) without requiring the Raylib-dependent `GameFrameInfo` parameter. After all player physics, `BroadcastPlayerSnapshots()` builds a `WorldSnapshotPacket` with all player positions, velocities, and camera angles, and broadcasts it unreliably at tick rate for client reconciliation and remote player interpolation. Added `AllowUnsafeBlocks` to `VoxelgineServer.csproj` for `InputState` fixed buffer access. Build verified.
- **Client world loading** — Created `WorldReceiver` class in `VoxelgineEngine/Engine/Net/WorldReceiver.cs` as the client-side counterpart to `WorldTransferManager`. Collects `WorldDataPacket` fragments into `Dictionary<int, byte[]>` keyed by fragment index via `HandleWorldData()`. When `WorldDataCompletePacket` arrives, `HandleWorldDataComplete()` stores total fragment count and FNV-1a checksum, then calls `TryAssemble()`. Assembly checks all fragments are present (0 to totalFragments-1), concatenates into a single `byte[]` via `MemoryStream`, verifies the FNV-1a checksum (same algorithm as `WorldTransferManager.ComputeChecksum`: offset basis 2166136261, prime 16777619), and fires `OnWorldDataReady` event with the compressed world data. On checksum mismatch, fires `OnTransferFailed` with error message. Handles out-of-order fragment arrival (dictionary-based storage), duplicate fragments from reliable retransmission (overwritten silently), and edge case where `WorldDataCompletePacket` arrives before all fragments (`TryAssemble` called after each fragment too). Exposes `Progress` (float 0-1), `FragmentsReceived`, `TotalFragments`, `IsComplete`, `IsReceiving` for UI progress tracking. `Reset()` clears all state for disconnect/reconnect. Integrated into `NetClient`: added `WorldReceiver _worldReceiver` field, `OnWorldDataReady` event (`Action<byte[]>`), `OnWorldTransferFailed` event (`Action<string>`), `WorldReceiver` property for progress access. Constructor wires `WorldReceiver` events to relay methods. `HandlePacket` intercepts `WorldDataPacket` and `WorldDataCompletePacket` in Loading state. `Cleanup()` calls `_worldReceiver.Reset()`. Game code subscribes to `OnWorldDataReady`, calls `ChunkMap.Read(new MemoryStream(data))` to decompress, then `NetClient.FinishLoading()` to transition Loading→Playing. Build verified.
- **Remote player rendering** — Created `RemotePlayer` class in `Voxelgine/Engine/Player/RemotePlayer.cs` as a lightweight client-side representation of other players. Stores `PlayerId`, `PlayerName`, interpolated `Position`, `Velocity`, `CameraAngle`. Loads the humanoid `CustomModel` (same `npc/humanoid.json` used by `VEntNPC`) with `NPCAnimator` for walk/idle animations. Implements a simple two-snapshot interpolation buffer with 100ms delay: `ApplySnapshot(position, velocity, cameraAngle, currentTime)` shifts current→previous and stores new snapshot; `Update(currentTime, deltaTime)` interpolates between snapshots using time-based lerp factor (clamped 0–1) with proper angle wrapping via `LerpAngleSingle`. `Draw3D()` positions the model at feet level (eye position - `PlayerEyeOffset`), converts camera yaw to `LookDirection` for model facing, and draws. Falls back to wireframe placeholder if model fails to load. Debug mode renders bounding box (green) and look direction line. `SetPosition()` for initial placement from `PlayerJoinedPacket`. Extended `PlayerManager` with remote player tracking: `Dictionary<int, RemotePlayer>` with `AddRemotePlayer()`, `RemoveRemotePlayer()`, `GetRemotePlayer()`, `GetAllRemotePlayers()`, `RemotePlayerCount`, `ClearRemotePlayers()`, and `LocalPlayerId` property. `RemovePlayer()` now also cleans up the remote player entry. Integrated into `GameState.Draw()`: remote players are updated (interpolation + animation) each frame using `Raylib.GetTime()`/`GetFrameTime()`, then rendered in `Draw3D()` after entities and before transparent blocks. Build verified.
- **Client-side prediction** — Created `ClientPrediction` class in `VoxelgineEngine/Engine/Net/ClientPrediction.cs` implementing the prediction state tracking system. `PredictedState` struct stores tick number, position, and velocity. Circular buffer of 128 entries (matching `ClientInputBuffer.BufferSize`) indexed by `tickNumber % BufferSize`. `RecordPrediction(tick, position, velocity)` stores predicted state after each tick's `Player.UpdatePhysics()`. `ProcessServerSnapshot(serverTick, serverPosition, serverVelocity)` compares server-authoritative state with predicted state at that tick — returns true if position error exceeds `CorrectionThreshold` (0.01 units), indicating reconciliation is needed. Ignores old/duplicate snapshots via `LastServerTick` tracking. Exposes `ReconciliationCount` and `LastCorrectionDistance` for network diagnostics. `Reset()` for disconnect/reconnect cleanup. Created `PredictionReconciler` static class in `Voxelgine/Engine/Player/PredictionReconciler.cs` (in Voxelgine project for `ChunkMap` access) that performs the actual input replay. Uses reusable `NetworkInputSource` + `InputMgr` to avoid allocation. `Reconcile(player, serverPosition, serverVelocity, serverTick, currentTick, inputBuffer, prediction, map, physData, dt)` snaps the player to server state via `SetPosition()`/`SetVelocity()`, retrieves buffered inputs via `GetInputsInRange(serverTick, currentTick)`, and for each input: restores camera angle via `SetCamAngle()`, updates direction vectors via `UpdateDirectionVectors()`, feeds input state through `NetworkInputSource`/`InputMgr`, calls `Player.UpdatePhysics()` for authoritative Quake-style replay, and records the new predicted state via `RecordPrediction()`. Preserves responsive strafe-jumping/bunny-hopping feel while ensuring server authority. Build verified.
- **Server time sync** — Added periodic `DayTimeSync` broadcasting to `ServerLoop`. Server broadcasts `DayTimeSyncPacket` (containing `DayNightCycle.TimeOfDay`) to all connected clients every 5 seconds (`TimeSyncInterval` constant). Added `_lastTimeSyncTime` field to track the last broadcast time. `BroadcastTimeSync(float currentTime)` method checks the interval, creates the packet, and calls `_server.Broadcast()` reliably. Integrated into `Tick()` as step 5, called after `DayNightCycle.Update()` so the broadcasted time reflects the current tick's advancement. New clients also receive a `DayTimeSyncPacket` immediately on connect (sent in `OnClientConnected` before world transfer begins) so they have the correct time of day before world data arrives. Tick step numbering updated (5→time sync, 6→entities, 7→player snapshots). Build verified.
- **Player position sync** — Server broadcasts `WorldSnapshotPacket` containing all player positions, velocities, and camera angles at tick rate via `BroadcastPlayerSnapshots()` in `ServerLoop`. Clients receive snapshots in `MultiplayerGameState.HandleWorldSnapshot()`: local player uses `ClientPrediction.ProcessServerSnapshot()` to compare against predicted state and triggers `PredictionReconciler.Reconcile()` when correction threshold exceeded; remote players receive positions via `RemotePlayer.ApplySnapshot()` for interpolated rendering. Position, velocity, and camera angle are synced. Build verified.
- **MultiplayerGameState: End-to-end multiplayer client** — Created `MultiplayerGameState` class in `Voxelgine/States/MultiplayerGameState.cs` (~730 lines) as the full multiplayer client gameplay state. Manages the complete client lifecycle: `NetClient` connection to server, world loading via `WorldReceiver` with progress bar UI, `GameSimulation` creation from received world data, local `Player` instantiation with full GUI/SoundMgr/ViewModel, client-side prediction via `ClientInputBuffer` + `ClientPrediction` + `PredictionReconciler`, remote player management via `PlayerManager` + `RemotePlayer` instances, and `DayNightCycle` with `IsAuthority = false` (server-synced time). `UpdateLockstep()` increments `LocalTick`, records input, sends `InputStatePacket`, runs local player physics prediction and records predicted state. `Draw()` populates `GameFrameInfo` from physics camera state, applies frame interpolation between ticks, sets `RenderCam` for rendering pipeline, and draws world/entities/remote players/local player. `Draw2D()` renders loading screen with progress bar during world transfer, or in-game HUD with ping/tick/player count during gameplay. Handles `PlayerJoinedPacket`, `PlayerLeftPacket`, `WorldSnapshotPacket`, and `DayTimeSyncPacket`. Added "Multiplayer" button to `MainMenuStateFishUI` (connects to 127.0.0.1:7777). Added `MultiplayerGameState` property to `IFishEngineRunner` interface and implementations (`FEngineRunner` in `Program.cs`, `ServerEngineRunner` in `ServerLoop.cs`). Added `InputMgr.State` property for reading current input state. Build verified.
- **Server world persistence** — Added `MapFile = "server_world.bin"` constant to `ServerLoop`. Server `Start()` now checks if `MapFile` exists: if found, loads the world via `ChunkMap.Read()` from file; if not, generates a new world via `ChunkMap.GenerateFloatingIsland()` and saves it via `ChunkMap.Write()`. Changed default world size from 16×16 to 32×32 chunks (`DefaultWorldWidth`/`DefaultWorldLength`). Updated `DefaultSpawnPosition` to (16, 73, 16) to match world center. Build verified.
- **SoundMgr: Double initialization crash fix** — Added `if (!Raylib.IsAudioDeviceReady())` guard before `Raylib.InitAudioDevice()` in `SoundMgr.Init()`. The native Raylib crash (calling `InitAudioDevice()` twice) bypassed managed try-catch blocks, causing hard crashes when `MultiplayerGameState` created a `SoundMgr` instance after `GameState` had already initialized the audio device. Build verified.
- **Remote player visibility on connect fix** — `PlayerJoinedPacket` for existing players was sent by the server before world data, but `MultiplayerGameState.HandlePlayerJoined()` discarded them because `_simulation` was null (only created after world loading). Fixed by adding `_pendingPlayerJoins` buffer (`List<PlayerJoinedPacket>`) — packets arriving before `_simulation` exists are buffered and replayed in `OnWorldDataReady()` after world loading completes. `Cleanup()` clears the buffer. Build verified.
- **Server block change authority** — Added `HandleBlockPlaceRequest()` and `HandleBlockRemoveRequest()` methods to `ServerLoop`. Server receives `BlockPlaceRequestPacket`/`BlockRemoveRequestPacket` from clients in `OnPacketReceived`, validates player exists and is within reach distance (`MaxBlockReach = 25` units, slightly above client's 20 to account for prediction lag), then applies the block change to `ChunkMap` via `SetBlock()`. Invalid requests (out of range, unknown player) are silently rejected. Added `BroadcastBlockChanges()` method called each tick (step 8) that collects `ChunkMap.GetPendingChanges()`, broadcasts a `BlockChangePacket` for each change to all clients reliably, then calls `ClearPendingChanges()`. Build verified.
- **Client block change handling** — Added `HandleBlockChange()` to `MultiplayerGameState` that receives `BlockChangePacket` from server and applies block changes to the local `ChunkMap` via `SetBlock()`. For local player actions, blocks are applied optimistically (client prediction) — `InventoryItem.DestroyBlock()`/`PlaceBlock()` modify the `ChunkMap` directly for immediate feedback. Added `SendPendingBlockChanges()` called after `TickGUI()` each frame: collects pending block changes from `ChunkMap.GetPendingChanges()`, sends `BlockRemoveRequestPacket` (for removals) or `BlockPlaceRequestPacket` (for placements) reliably to the server, then clears the log. Server validates and broadcasts authoritative `BlockChangePacket`s to all clients. Chunk mesh rebuilds are triggered automatically by `SetBlock()`. Build verified.
- **World block sync** — Server tracks block changes via `ChunkMap._blockChangeLog` (populated automatically by `SetPlacedBlock()`). Each tick, `BroadcastBlockChanges()` collects pending changes and broadcasts `BlockChangePacket` reliably to all clients. On client connect, full world state is transferred via `WorldTransferManager`. During play, only delta block changes are sent. Clients apply server-confirmed changes to their local `ChunkMap`. Build verified.
- **Remote player interpolation buffer** — Created generic `SnapshotBuffer<T>` class in `VoxelgineEngine/Engine/Net/SnapshotBuffer.cs` — a 32-entry ring buffer of `TimestampedSnapshot<T>` structs with `Add(T data, float time)` and `Sample(float renderTime, out T from, out T to, out float t)`. `Sample()` finds the two snapshots bracketing the render time and computes the interpolation factor (clamped [0,1]). Handles edge cases: fewer than 2 snapshots (returns single/default), render time before oldest (clamps), render time after newest (extrapolates from last pair, clamped). Refactored `RemotePlayer` to use `SnapshotBuffer<PlayerSnapshot>` instead of the manual two-snapshot approach. `PlayerSnapshot` struct holds position, velocity, camera angle. `ApplySnapshot()` calls `_snapshotBuffer.Add()`. `Update()` calls `_snapshotBuffer.Sample()` at render time (current time - 100ms `InterpolationDelay`) and interpolates position (`Vector3.Lerp`), velocity, and camera angle (`LerpAngle` with wrapping). `SetPosition()` resets the buffer for clean teleport. The multi-snapshot ring buffer provides smoother interpolation under packet jitter — instead of only two snapshots, it can select the optimal pair from up to 32 recent entries. Ready for reuse by entity interpolation. Build verified.
- **Entity synchronization (server + client + state sync)** — Implemented full entity network synchronization across three tightly coupled tasks. **Server side (`ServerLoop.cs`):** Added `SpawnEntities()` method that creates server-side entities (VEntPickup at pickup spawn + VEntNPC at NPC spawn with pathfinding, matching single-player world setup), called after world generation. `OnClientConnected` now sends `EntitySpawnPacket` for all existing entities to new clients (alongside existing player packets). Added `BroadcastEntitySnapshots()` as tick step 9 — each tick, iterates all entities and broadcasts `EntitySnapshotPacket` (position, velocity, animation state byte) unreliably. `GetEntityAnimationState()` maps NPC animator state to compact byte (0=idle, 1=walk, 2=attack). `BuildEntitySpawnPacket()` serializes entity type name, network ID, position, and spawn properties (size, model name, subclass data) into the packet. **Client side (`MultiplayerGameState.cs`):** Handles `EntitySpawnPacket` in `OnPacketReceived` — `CreateEntityByType()` factory maps type name strings ("VEntNPC", "VEntPickup", "VEntSlidingDoor", "VEntPlayer") to concrete instances, reads spawn properties via `ReadSpawnProperties()`, and spawns via `EntityManager.SpawnWithNetworkId()`. Handles `EntityRemovePacket` — removes entity by network ID and cleans up interpolation buffer. Handles `EntitySnapshotPacket` — stores in per-entity `SnapshotBuffer<EntitySnapshot>` (reusing the generic buffer from remote player interpolation). `UpdateEntityInterpolation()` called each frame samples buffers at render time minus 100ms delay, interpolates position/velocity via `Vector3.Lerp`. `UpdateEntityAnimation()` maps animation state byte back to animation name for NPC animators and calls `UpdateVisuals()` for cosmetic rotation on all entities. Entity packets received before simulation exists are buffered in `_pendingEntityPackets` and replayed after world loading (same pattern as `_pendingPlayerJoins`). **Infrastructure:** `EntityManager` gained `bool IsAuthority` property (default true) — when false, `UpdateLockstep()` skips physics and AI entirely. `SpawnWithNetworkId()` method for client-side entity creation with server-assigned network IDs. `VoxEntity` gained `EntityTypeName` virtual property (returns class name), `WriteSpawnProperties()`/`ReadSpawnProperties()` for spawn-time serialization (size + model name + virtual `WriteSpawnPropertiesExtra`/`ReadSpawnPropertiesExtra`), and `UpdateVisuals()` method for cosmetic-only updates (rotation) without AI/physics. `VEntSlidingDoor` overrides `WriteSpawnPropertiesExtra`/`ReadSpawnPropertiesExtra` to serialize slide direction, distance, trigger radius, and closed position. Build verified.
- **Server combat authority + Client combat effects** — Implemented server-authoritative weapon fire resolution and client-side hit effect rendering. **Server side (`ServerLoop.cs`):** Added `HandleWeaponFire()` in `OnPacketReceived` — validates player exists, direction is normalized, and fire origin is near player position (anti-cheat). Performs authoritative raycast against world blocks (`ChunkMap.RaycastPrecise`), entities (`EntityManager.Raycast`), and other player AABBs (new `RaycastPlayers()` method using `PhysicsUtils.CreatePlayerAABB` + `RayMath.RayIntersectsAABB`). Takes closest hit across all three targets. Broadcasts `WeaponFireEffectPacket` to all clients with hit position, normal, type, and entity network ID.
- **Server raycast precision fix** — `ChunkMap.RaycastPos()` returned integer block coordinates (the DDA traversal result), causing `HandleWeaponFire()` to use block center `(x+0.5, y+0.5, z+0.5)` as the hit position. Weapon tracers and spark/fire particles appeared at block centers instead of on the actual hit surface. Added `ChunkMap.RaycastPrecise(origin, distance, dir, out hitPoint, out faceDir)` which calls `RaycastPos` then computes the exact ray-plane intersection using the DDA face normal: determines the face plane coordinate from the block position and face direction, solves `t = (planeValue - originComponent) / dirComponent`, and returns `origin + dir * t`. `HandleWeaponFire()` in `ServerLoop` now uses `RaycastPrecise` for accurate hit positions. Fallback for degenerate case (ray parallel to face plane) returns face center. Existing `RaycastPos` callers (ground detection, block interaction) unchanged — they only need block coordinates. Build verified.
- **Player health system** — Added `Health` (float, default 100), `MaxHealth` (float, default 100), `IsDead` (bool, `Health <= 0`), `TakeDamage(float)`, and `ResetHealth()` to `Player.cs` (available in both client and server constructors). Added `FireHitType.Player = 3` to distinguish player hits from entity hits in combat effects. Added `HitPlayerId` field to `RaycastHit` struct (default -1) for tracking which player was hit by raycasts. Modified `ServerLoop.RaycastPlayers()` to set `HitPlayerId` on the hit result and skip dead players. Modified `ServerLoop.HandleWeaponFire()` to use `FireHitType.Player` for player hits, apply `WeaponDamage` (25f) via `Player.TakeDamage()`, and broadcast `PlayerDamagePacket` to all clients on hit. Dead players are logged with killer info. `ProcessPlayerPhysics()` skips dead players (no input processing or physics). Added `HitPlayerId` field to `WeaponFireEffectPacket` with full serialization. Added `Health` field to `WorldSnapshotPacket.PlayerEntry` for continuous health sync — server includes `Player.Health` in every tick's snapshot, client syncs local player health from server state in `HandleWorldSnapshot()`. `MultiplayerGameState` handles `PlayerDamagePacket` (logging), `FireHitType.Player` in `HandleWeaponFireEffect()` (blood particles), and `DrawHealthBar()` — bottom-center health bar with green/yellow/red color based on ratio, numeric display. Client-side prediction skips `UpdatePhysics` when local player `IsDead`. Build verified.
- **Player respawn system** — Added server-side respawn timer infrastructure to `ServerLoop.cs`. `RespawnDelay = 3f` constant defines the 3-second respawn window. `_respawnTimers` dictionary (`Dictionary<int, float>`) maps player ID to death time — populated in `HandleWeaponFire()` when a kill is detected (`hitPlayer.IsDead` after `TakeDamage()`). New `ProcessRespawns()` method (called as tick step 3, before physics) iterates the dictionary each tick and respawns players whose timer has expired: calls `Player.ResetHealth()`, `Player.SetPosition(DefaultSpawnPosition)`, `Player.SetVelocity(Vector3.Zero)`, and removes the timer entry. Respawn timers are cleaned up on player disconnect in `OnClientDisconnected`. Client-side: added `DrawDeathOverlay()` method to `MultiplayerGameState` — renders a dark red screen tint (`Color(100,0,0,140)`), large "YOU DIED" text in red, and "Respawning..." subtitle. Overlay is drawn in `Draw2D()` when `LocalPlayer.IsDead` is true. Respawn is detected implicitly via `WorldSnapshotPacket` health sync — when server resets health to 100, the next snapshot updates `LocalPlayer.Health` from 0 to 100, `IsDead` becomes false, and the overlay disappears. No new packet types needed. Build verified.
- **Connect to server UI** — Added `_connectWindow` (FishUI `Window`) to `MainMenuStateFishUI` as a "Connect to Server" dialog with three text inputs: Server IP (default "127.0.0.1"), Port (default "7777"), and Player Name (default "Player"), plus a status label for validation errors. "Connect" button validates inputs (non-empty host, port 1-65535, non-empty name with fallback), hides the dialog, switches to `MultiplayerGameState`, and calls `Connect(host, port, playerName)`. "Cancel" button and window close button hide the dialog. Replaced the hardcoded `Multiplayer` button handler (which connected directly to `127.0.0.1:7777` with name "Player") with a handler that opens the connect dialog. Added `CreateConnectWindow()` method called in constructor alongside existing `CreateOptionsWindow()`. Connect window is centered on screen and recentered on window resize in `OnResize()`. Connection status (Connecting → Loading World → Playing) and error messages continue to be displayed by `MultiplayerGameState.Draw2D()` after the state transition. Build verified.
