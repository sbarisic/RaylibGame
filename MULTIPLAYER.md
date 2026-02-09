# Aurora Falls - Multiplayer Reference

Technical reference for the multiplayer system architecture, network protocol, and design decisions.

---

## Architecture Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| **Network model** | Client-server authoritative | Server owns all game state; prevents cheating, simplifies conflict resolution. One player hosts (listen server) or dedicated headless server. |
| **Transport** | UDP with custom reliability layer | Low latency for position/input updates (unreliable channel), guaranteed delivery for world changes/chat/inventory (reliable channel). Raw `System.Net.Sockets.UdpClient` — no external networking library. |
| **Tick model** | Server-authoritative fixed timestep | Server runs 0.015s (66.6 Hz) lockstep loop. Clients send inputs at tick rate, server processes and broadcasts state. |
| **Client prediction** | Client-side prediction with server reconciliation | Client predicts local player movement using same Quake physics code. Server sends authoritative position; client replays unacknowledged inputs on correction. |
| **Entity interpolation** | Buffered interpolation for remote entities | Remote players and entities rendered with ~100ms buffer, interpolating between received snapshots. |
| **World sync** | Full world transfer on join, delta updates during play | New clients receive GZip-compressed world data. During play, only block changes sent as deltas. |
| **Serialization** | Binary with `BinaryWriter`/`BinaryReader` | Compact, fast, no external dependencies. Consistent with existing serialization. |
| **Player identity** | Integer player ID (0-9) + display name | Server assigns IDs on connect. Max 10 players. |
| **Session model** | Listen server + dedicated headless mode | Listen server: one player hosts and plays. Headless: `VoxelgineServer` runs without Raylib. |

---

## Network Protocol

### Channels

| Channel | Delivery | Use Cases |
|---------|----------|-----------|
| **Unreliable** | Fire-and-forget UDP | Player position/velocity/angle snapshots, input states |
| **Reliable ordered** | Sequence numbers + ACK | Block changes, entity spawn/remove, connect/disconnect, chat, inventory, world chunks |
| **Reliable unordered** | ACK without ordering | Sound events, particle events |

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

---

## State Flow

### Client Connect Flow

```
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
```

### Server Tick Flow

1. Receive all client InputStates for this tick
2. Apply each player's input to their player state (same Quake physics)
3. Run entity UpdateLockstep (EntityManager)
4. Run world updates (lighting, etc.)
5. Broadcast WorldSnapshot + PlayerSnapshots + EntitySnapshots
6. Increment server tick counter

### Client Tick Flow

1. Read local input, send InputState to server
2. Predict local player movement (apply input locally with Quake physics)
3. Receive server snapshots
4. Reconcile local prediction (replay unACKed inputs from server-confirmed state)
5. Interpolate remote players/entities between snapshots
6. Render from local camera

---

## Systems Impact

| System | Status | Notes |
|--------|--------|-------|
| **FPSCamera** | ✅ | Instance-based, owned by `Player.Camera` |
| **Player** | ✅ | `PlayerManager` with `PlayerId`, remote player support |
| **InputMgr** | ✅ | `IInputSource` abstraction, `NetworkInputSource` for server |
| **GameState** | ✅ | `GameSimulation` (headless) + `MultiplayerGameState` (client) |
| **EntityManager** | ✅ | Network IDs, authority flag, spawn properties |
| **ChunkMap** | ✅ | Block change tracking for delta sync |
| **WeaponGun** | ✅ | Fire intent/resolve/effects separation |
| **ParticleSystem** | ✅ | Client-only, triggered by network events |
| **DayNightCycle** | ✅ | Authority flag, server time sync |
| **PhysicsUtils** | ✅ | Split: pure math (Engine) + world collision (Voxelgine) |
| **Project structure** | ✅ | Three-project split: Voxelgine, VoxelgineEngine, VoxelgineServer |
