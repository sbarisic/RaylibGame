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
| **EntityManager** | Single instance, no network IDs | Add network entity IDs, sync entity spawn/remove |
| **ChunkMap** | ✅ Block change tracking via `BlockChange` struct and `_blockChangeLog` in `ChunkMap` | `GetPendingChanges()` / `ClearPendingChanges()` for network delta sync; `SetPlacedBlock()` logs old→new type changes |
| **WeaponGun** | Raycast is local, affects entities directly | Server-authoritative hit detection; client sends fire intent, server validates |
| **ParticleSystem** | Visual only, no gameplay impact | Client-only, triggered by network events (fire effects, blood, etc.) |
| **SoundMgr** | Positional audio is local | Client-only, triggered by network events |
| **DayNightCycle** | Time progresses locally | Server owns time, syncs to clients periodically |
| **PhysicsUtils** | Shared static utilities | No changes needed — same code runs on server and client |
| **PhysData** | Physics constants | Server sends PhysData on connect so all clients match |
| **GameFrameInfo** | Frame interpolation struct | Extend for remote player interpolation |
| **FishDI** | 7 singletons, local services | Add network services (NetServer/NetClient) as DI singletons |
| **Project structure** | ✅ Phase 1 complete: 16 Raylib-free files moved to `VoxelgineEngine` (DI interfaces, `FishDI`, `PhysData`, `InputMgr`, `IInputSource`, `NetworkInputSource`, `Noise`, `SpatialHashGrid`, `ThreadWorker`, `FishLogging`, `Debug`, `SettingsHiddenAttribute`, `OnKeyPressedEventArg`). Phase 2 pending: mixed Raylib/logic classes. | `VoxelgineEngine.csproj` has `Microsoft.Extensions.Hosting` + `TextCopy` NuGet, `AllowUnsafeBlocks`. `Voxelgine` references `VoxelgineEngine`. `VoxelgineServer` references `VoxelgineEngine`. |
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
- [ ] **Project split phase 2: Split mixed Raylib/logic classes** — Remaining files with Raylib dependencies that contain shared logic (e.g., `AABB`, `GameFrameInfo`, `PhysicsUtils`, `Raycast`, `GameSimulation`, `PlayerManager`, `EntityManager`, pathfinding, animations) need Raylib types extracted or abstracted before they can move. Includes extracting math-only types from Raylib wrappers. **[CPX: 5]**

### Medium Priority

- [ ] **EntityManager: Network entity IDs**
- [ ] **Player.Serialization: Network snapshot format** — Extend player serialization with a lightweight `WriteSnapshot(BinaryWriter)`/`ReadSnapshot(BinaryReader)` that writes only the frequently-changing state (position, velocity, camera angle, animation state, current weapon index) in a compact binary format. The existing `Write()`/`Read()` remain for save files. Snapshot format includes a tick number for ordering. **[CPX: 2]**
- [ ] **VoxEntity: Network serialization** — Add `WriteSnapshot(BinaryWriter)`/`ReadSnapshot(BinaryReader)` to `VoxEntity` base class for position, velocity, and type-specific state. Each subclass (`VEntPickup`, `VEntNPC`, `VEntSlidingDoor`) overrides to include its own state (NPC animation, door open state, pickup item type). **[CPX: 3]**

### Lower Priority

- [ ] **WeaponGun: Separate fire intent from hit resolution** — Refactor `WeaponGun` so that pulling the trigger generates a "fire intent" (origin, direction, weapon type) rather than immediately raycasting and applying damage. In single-player, the intent is immediately resolved locally. In multiplayer, the intent is sent to the server. Server performs authoritative raycast and broadcasts the result. Client plays fire effects (sound, muzzle flash, tracer) on intent, but hit effects (blood, damage numbers) only on server confirmation. **[CPX: 3]**
- [ ] **DayNightCycle: External time source** — Add `SetTime(float hours)` and `bool IsAuthority` flag to `DayNightCycle`. When `IsAuthority` is false (client in multiplayer), time does not advance locally — it only updates via `SetTime()` from server sync. Single-player sets `IsAuthority = true`. **[CPX: 1]**

---

## Networking Infrastructure

> Low-level networking: transport, connection management, packet serialization. No game logic here.

### High Priority

- [ ] **UDP transport layer** — Create `UdpTransport` class wrapping `System.Net.Sockets.UdpClient` with async send/receive. Support binding to a port (server) or connecting to an endpoint (client). Provide `SendTo(byte[] data, IPEndPoint target)` and event-based `OnDataReceived(byte[] data, IPEndPoint sender)`. Handle socket exceptions gracefully. Place in `Voxelgine/Engine/Net/`. **[CPX: 3]**
- [ ] **Packet serialization framework** — Create `Packet` base class with `PacketType` byte ID, `Write(BinaryWriter)`, and static `Read(BinaryReader)` factory. Implement a `PacketRegistry` that maps type IDs to deserializer functions. Provide `Packet.Serialize()` → `byte[]` and `Packet.Deserialize(byte[])` → `Packet`. All packet types from the protocol table above get stub classes (implement data fields and serialization as each feature needs them). **[CPX: 3]**
- [ ] **Reliable delivery layer** — Implement reliability on top of UDP: sequence numbers, ACK/NAK, retransmission with timeout. Each packet has a header: `[reliable flag (1 byte)] [sequence number (2 bytes)] [ack bitfield (4 bytes)] [packet type (1 byte)] [payload]`. Unreliable packets skip sequence/ACK. Reliable packets are stored in a send buffer until ACKed. Detect and discard duplicates on receive. **[CPX: 4]**
- [ ] **Connection manager** — Create `NetConnection` class representing a connection to a remote endpoint: tracks RTT (round-trip time via Ping/Pong), sequence numbers, ACK state, connection state (Connecting/Connected/Disconnected), and timeout detection (no data for 10s = timeout). Server holds `Dictionary<IPEndPoint, NetConnection>` for all clients. Client holds a single `NetConnection` to the server. **[CPX: 3]**

### Medium Priority

- [ ] **Bandwidth management** — Implement send rate limiting and packet batching. Combine multiple small packets into a single UDP datagram (up to MTU ~1200 bytes). Prioritize unreliable snapshots over reliable messages when near bandwidth limit. Track bytes/sec sent and received per connection for diagnostics. **[CPX: 3]**
- [ ] **Packet fragmentation** — Large packets (world data transfer) exceed MTU. Implement fragmentation: split large reliable packets into numbered fragments, reassemble on receive, ACK only when all fragments arrive. Used primarily for `WorldData` packets during client connect. **[CPX: 3]**

---

## Server Implementation

> Server-side game logic. Processes client inputs, runs authoritative simulation, broadcasts state.

### High Priority

- [ ] **NetServer core** — Create `NetServer` class: binds UDP port, accepts connections (validates protocol version, assigns player ID, rejects if full), manages connected players, processes incoming packets via `PacketRegistry`, provides `Broadcast(Packet)` and `SendTo(int playerId, Packet)`. Register as DI singleton. Integrate with game loop — `NetServer.Tick()` called each lockstep update. **[CPX: 4]**
- [ ] **Server game loop** — Create `ServerLoop` (or extend `Program.cs`) for dedicated server mode. CLI argument `--server --port 7777` launches headless: no Raylib window, no rendering, no audio. Runs `GameSimulation` with `NetServer`. Tick loop: receive inputs → apply to player states → run simulation → broadcast snapshots. Use existing fixed timestep accumulator pattern. **[CPX: 4]**
- [ ] **Server player management** — On `Connect` packet: create `Player` instance with assigned ID, set spawn position, add to `PlayerManager`, send `ConnectAccept` + `WorldData` + existing `PlayerJoined` for all current players, broadcast `PlayerJoined` to others. On `Disconnect`/timeout: remove player, broadcast `PlayerLeft`, despawn player entity. **[CPX: 3]**
- [ ] **Server input processing** — Receive `InputState` packets from clients each tick. Buffer inputs per player (handle late/missing inputs — repeat last known input if missing). Apply each player's input to their `Player` instance using the same physics code (`Player.UpdatePhysics`). Server is the single authority on player positions. **[CPX: 3]**
- [ ] **Server world transfer** — On client connect, serialize `ChunkMap` via existing `ChunkMap.Write()` into a `MemoryStream`, fragment into `WorldData` packets (~1KB each), send reliably to the connecting client. Send `WorldDataComplete` with total size and checksum. Client reassembles and loads. Rate-limit transfer to avoid flooding. **[CPX: 3]**

### Medium Priority

- [ ] **Server block change authority** — Receive `BlockPlaceRequest`/`BlockRemoveRequest` from clients. Validate: is block position reachable from player position? Is player allowed to place this block type? If valid, apply to `ChunkMap`, broadcast `BlockChange` to all clients. If invalid, silently reject (client prediction will be corrected). **[CPX: 2]**
- [ ] **Server combat authority** — Receive `WeaponFire` from clients. Server performs authoritative raycast against world and all player AABBs / entity AABBs using same raycasting code. Apply damage to hit entity/player. Broadcast `WeaponFireEffect` (for tracer/particle effects on all clients) and `PlayerDamage` if a player was hit. **[CPX: 3]**
- [ ] **Server entity synchronization** — Each tick, for entities that moved, build `EntitySnapshot` packets and broadcast. On entity spawn/remove, send `EntitySpawn`/`EntityRemove` reliably. Clients only spawn/remove entities when told by server. NPC AI runs on server only. **[CPX: 3]**
- [ ] **Server time sync** — Server owns `DayNightCycle`. Broadcast `DayTimeSync` periodically (every ~5 seconds) and on manual time changes. Clients set their local time to match. **[CPX: 1]**

### Lower Priority

- [ ] **Listen server mode** — Allow a player to host and play simultaneously. The hosting client runs `NetServer` + `GameSimulation` + rendering. Local player's input bypasses networking (applied directly to simulation). Remote players connect normally. `Program.cs` flag `--host --port 7777` enables this mode alongside normal client rendering. **[CPX: 3]**
- [ ] **Server console/admin commands** — Headless server reads stdin for commands: `kick <player>`, `ban <player>`, `say <message>`, `time <hours>`, `save`, `quit`. For listen server, these are available via the debug menu (F1). **[CPX: 2]**

---

## Client Implementation

> Client-side networking, prediction, interpolation, and rendering of remote players.

### High Priority

- [ ] **NetClient core** — Create `NetClient` class: connects to server endpoint, sends `Connect` packet, handles `ConnectAccept`/`ConnectReject`, processes incoming packets via `PacketRegistry`, provides `Send(Packet)`. Register as DI singleton. Integrate with game loop — `NetClient.Tick()` called each lockstep update. Track connection state (Connecting/Loading/Playing/Disconnected). **[CPX: 3]**
- [ ] **Client input sending** — Each tick, serialize local `InputState` + camera angle + current tick number into an `InputState` packet and send unreliably to server. Store sent inputs in a circular buffer (last ~128 ticks) for prediction reconciliation. **[CPX: 2]**
- [ ] **Client-side prediction** — Apply local input immediately to local player using same `Player.UpdatePhysics()` code. When server `PlayerSnapshot` arrives for local player, compare server position with predicted position at that tick. If difference exceeds threshold (~0.01 units), snap to server position and replay all inputs from that tick to current tick (reconciliation). Preserves responsive movement feel. **[CPX: 4]**
- [ ] **Client world loading** — Receive `WorldData` fragments, reassemble into `MemoryStream`, decompress via `ChunkMap.Read()`. Show loading progress bar during transfer. Transition to gameplay state when `WorldDataComplete` received and world fully loaded. **[CPX: 3]**
- [ ] **Remote player rendering** — Create `RemotePlayer` class (or extend `Player` with `IsRemote` flag) that has position, velocity, camera angle, and a 3D model (reuse NPC model or create player model). Interpolate between received `PlayerSnapshot` positions using a 100ms interpolation buffer. Render remote players in `GameState.Draw3D()`. Remote players do not have `FPSCamera`, `ViewModel`, or local input. **[CPX: 4]**

### Medium Priority

- [ ] **Remote player interpolation buffer** — Implement `SnapshotBuffer<T>` that stores timestamped snapshots and interpolates between the two surrounding the render time (current time - interpolation delay). Use for remote player positions, angles, and entity positions. Leverages `Vector3.Lerp` and angle lerp. **[CPX: 3]**
- [ ] **Client block change handling** — Receive `BlockChange` from server, apply to local `ChunkMap`. For local player block actions: apply optimistically (client prediction), revert if server rejects (no `BlockChange` received within timeout). Trigger chunk mesh rebuild on change. **[CPX: 2]**
- [ ] **Client entity synchronization** — Receive `EntitySpawn`: create entity locally with given network ID and properties. Receive `EntityRemove`: remove entity. Receive `EntitySnapshot`: update entity position in interpolation buffer. Client does not run entity AI — only interpolates visual positions. **[CPX: 3]**
- [ ] **Client combat effects** — Receive `WeaponFireEffect`: play tracer line, muzzle flash, impact particles (fire/blood) at the positions specified by server. Receive `PlayerDamage`: show damage indicator on HUD if local player was hit. Local player's own weapon fire plays immediate effects for responsiveness, server confirmation adds hit effects. **[CPX: 2]**

### Lower Priority

- [ ] **Client disconnect handling** — Detect server timeout (no packets for 10s) or explicit `Disconnect` packet. Show "Connection Lost" overlay. Option to reconnect or return to main menu. Clean up all network state, remote players, and restore single-player-like state. **[CPX: 2]**
- [ ] **Network statistics HUD** — Debug overlay (toggle with key) showing: ping, packet loss %, bytes/sec in/out, server tick rate, prediction error count, interpolation buffer health. Useful for development and player diagnostics. **[CPX: 2]**

---

## Synchronization

> Specific sync strategies for individual game systems.

### High Priority

- [ ] **Player position sync** — Server broadcasts `WorldSnapshot` (all player positions in one packet) at tick rate. Clients use this for remote player interpolation. Local player uses prediction + reconciliation (see Client Implementation). Position, velocity, and camera angle are the minimum sync fields. **[CPX: 3]**
- [ ] **World block sync** — Server tracks `ChunkMap.BlockChangeLog`. Each tick, collect pending changes, broadcast `BlockChange` packets to all clients, clear log. On client connect, full world transfer (see Server world transfer). During play, only deltas. **[CPX: 2]**

### Medium Priority

- [ ] **Entity state sync** — Server sends `EntitySnapshot` for entities that moved this tick (position + velocity delta check). Clients interpolate. Spawn/remove are reliable events. NPC animation state synced as a compact enum (idle/walk/attack) so clients play matching animations. **[CPX: 3]**
- [ ] **Inventory sync** — Server tracks each player's inventory. Item pickup/use/drop are server-authoritative. Server sends `InventoryUpdate` packets on change. Client displays inventory from server data. Local prediction for immediate feedback (item count decrement on use), server corrects if needed. **[CPX: 3]**

### Lower Priority

- [ ] **Particle/sound sync** — Server sends compact event packets for gameplay-relevant effects (weapon fire, block break, explosion). Clients spawn particles and play sounds locally based on these events. Ambient particles (smoke) remain client-only. **[CPX: 2]**
- [ ] **Day/night sync** — Server periodically sends current time. Client smoothly transitions to server time (lerp, not snap, to avoid visual pop). **[CPX: 1]**

---

## Gameplay Features

> Multiplayer-specific gameplay features built on top of the networking layer.

### High Priority

- [ ] **Player health system** — Add `Health` and `MaxHealth` to `Player` (default 100). Server applies damage from combat hits. Broadcast health changes to the damaged player's client. On health ≤ 0, trigger death: respawn at spawn point after delay, drop inventory items. **[CPX: 3]**
- [ ] **Player respawn system** — On death, server sets respawn timer (3 seconds). During timer, client shows death screen overlay. On respawn, server teleports player to spawn point, resets health, broadcasts new position. **[CPX: 2]**

### Medium Priority

- [ ] **Text chat** — Client sends `ChatMessage` to server, server broadcasts to all. Display chat messages in a scrollable FishUI panel anchored to bottom-left. Fade out after 10 seconds. Toggle chat input with Enter key. Support `/commands` for admin actions. **[CPX: 3]**
- [ ] **Player name tags** — Render player display names above remote player models using billboard text. Scale with distance, fade at long range, hidden when obstructed by blocks. **[CPX: 2]**
- [ ] **Kill feed** — Display a temporary message when a player kills another player (e.g., "PlayerA killed PlayerB with Gun"). Server sends kill event, clients display in top-right corner with fade-out timer (FishUI Toast notify). **[CPX: 1]**

### Lower Priority

- [ ] **Scoreboard** — Tab key shows overlay with all connected players: name, kills, deaths, ping. Server tracks stats and broadcasts periodically. **[CPX: 2]**
- [ ] **Player model** — Create or assign a visible 3D model for remote players (could reuse/adapt NPC model). Animate walk cycle based on velocity. Show held weapon model in hand. **[CPX: 3]**

---

## UI & Menus

### High Priority

- [ ] **Connect to server UI** — FishUI dialog accessible from main menu: text input for server IP/hostname and port, "Connect" button. Show connection status (Connecting → Loading World → Playing). Display error messages on failure. **[CPX: 2]**
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
7. **Project split phase 2** → split mixed Raylib/logic classes, complete `VoxelgineEngine` migration
8. **Entity network IDs** → unblocks entity sync
9. **Serialization extensions** → unblocks network packets
10. **UDP transport + packet framework** → networking foundation (in `VoxelgineEngine`)
11. **Reliable delivery layer** → required for reliable messages
12. **Connection manager** → required for server/client
13. **NetServer + NetClient cores** → server and client networking
14. **Server game loop + player management** → playable `VoxelgineServer`
15. **Client input sending + world loading** → client can connect and see world
16. **Player position sync + remote rendering** → see other players
17. **Client-side prediction + reconciliation** → responsive local movement
18. **Remote player interpolation** → smooth remote player movement
19. **Block sync + entity sync** → full world synchronization
20. **Weapon fire authority** → combat works in multiplayer
21. **Player health + respawn** → PvP gameplay
22. **Chat + UI** → social features and menus
23. **Listen server mode** → host-and-play
24. **Bandwidth management + fragmentation** → production-ready networking
25. **Testing tools** → network simulation, loopback
26. **Documentation** → guides and references

---

## Completed

- **FPSCamera: Convert from static to instance-based** — Refactored `FPSCamera` to instantiable class with instance fields. `Player` owns `Camera` field, created in constructor with config sensitivity. Updated all references in `Player.cs`, `Player.Input.cs`, `Player.Physics.cs`, `GameWindow.cs`, `Program.cs`. Build verified.
- **Player: Add player ID and PlayerManager** — Added `int PlayerId` property to `Player` (constructor parameter, default 0). Created `PlayerManager` class with `Dictionary<int, Player>`, `AddPlayer()`, `AddLocalPlayer()`, `RemovePlayer()`, `GetPlayer()`, `GetAllPlayers()`, `GetLocalPlayer()`, and `LocalPlayer` convenience property. `GameState` now holds `PlayerManager Players` with `LocalPlayer` shortcut property. All `Ply` references updated across `GameState.cs`, `GameWindow.cs`, `EntityManager.cs`, `VEntSlidingDoor.cs`. Single-player creates player with ID 0. Build verified.
- **InputMgr: Abstract input source** — Created `IInputSource` interface with `Poll(float gameTime)` method. Implemented `LocalInputSource` (wraps Raylib keyboard/mouse polling via `GameConfig` key mappings) and `NetworkInputSource` stub (stores last received `InputState` from network). Refactored `InputMgr` to take `IInputSource` in constructor with `SetInputSource()` for runtime swapping. Moved Raylib polling out of `InputMgr` — it now has zero Raylib dependency. `GameWindow` creates `LocalInputSource` and passes it to `InputMgr`. All existing consumers unchanged. Build verified.
- **ChunkMap: Block change tracking** — Created `BlockChange` readonly struct (`X`, `Y`, `Z`, `OldType`, `NewType`) in `Voxelgine/Graphics/Chunk/BlockChange.cs`. Added `_blockChangeLog` (`List<BlockChange>`) field to `ChunkMap` with `GetPendingChanges()` (returns `IReadOnlyList<BlockChange>`) and `ClearPendingChanges()` methods. `SetPlacedBlock()` now reads the old block type before modification and logs the change if the type differs. `SetPlacedBlockNoLighting()` (internal chunk operations) intentionally excluded from logging. World generation changes can be cleared after generation. Build verified.
- **GameState: Separate simulation from presentation** — Created `GameSimulation` class (`Voxelgine/Engine/GameSimulation.cs`) that owns authoritative game state: `ChunkMap Map`, `PlayerManager Players`, `EntityManager Entities`, `DayNightCycle DayNight`, `PhysData PhysicsData`. `GameState` now holds `GameSimulation Simulation` with delegate properties (`Map`, `Players`, `LocalPlayer`, `DayNight`, `Entities`) for backward compatibility, plus client-only systems (`ParticleSystem`, `SoundMgr`, `FishUIManager`, rendering). `VoxEntity` stores `GameSimulation` instead of `GameState` (`GetSimulation()`/`SetSimulation()`). `EntityManager.Spawn()` takes `GameSimulation`. Removed unused `IGameWindow` dependency from `EntityManager`. Removed `using Voxelgine.States` from entity classes. Headless server can now run `GameSimulation` without any presentation layer. Build verified.
- **Project split phase 1: Move Raylib-independent code to VoxelgineEngine** — Moved 16 Raylib-free files from `Voxelgine` to `VoxelgineEngine`, maintaining directory structure. **Moved files:** DI interfaces (`IFishLogging`, `IFishConfig`, `IFishDebug`, `IFishClipboard`), `FishDI` (DI container), `PhysData` (physics constants), `InputMgr`/`IInputSource`/`NetworkInputSource` (input abstraction), `Noise` (simplex noise), `SpatialHashGrid`, `ThreadWorker`, `FishLogging`, `Debug`, `SettingsHiddenAttribute`, `OnKeyPressedEventArg`. **VoxelgineEngine.csproj** updated with `AllowUnsafeBlocks`, `Microsoft.Extensions.Hosting` 10.0.2, `TextCopy` 6.2.1 NuGet packages. `SettingsHiddenAttribute` made `public` for cross-assembly visibility. Removed placeholder `EngineShared.cs`. **Cannot move yet (Phase 2):** Files referencing Raylib types (`AABB`, `GameFrameInfo`, `DayNightCycle`), files depending on `ChunkMap`/`VoxEntity`/`Player` (`PhysicsUtils`, `Raycast`, `GameSimulation`, `PlayerManager`, `EntityManager`, pathfinding), animation files depending on `IFishEngineRunner` (defined in `IFishProgram.cs` which references `Voxelgine.States`). Full solution build verified (0 errors).
