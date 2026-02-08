# Aurora Falls - Multiplayer Completed Tasks

Completed tasks from [TODO_MULTIPLAYER.md](TODO_MULTIPLAYER.md), consolidated and simplified.

---

## Core Refactoring

- **FPSCamera instance refactoring** — Converted from static to instance-based. `Player` owns `Camera` field. All static references updated.
- **Player ID + PlayerManager** — Added `PlayerId` to `Player`. Created `PlayerManager` with dictionary-based player tracking, `AddPlayer()`, `RemovePlayer()`, `GetPlayer()`, `LocalPlayer`.
- **InputMgr abstraction** — Created `IInputSource` interface. `LocalInputSource` (Raylib) and `NetworkInputSource` (network). `InputMgr` has zero Raylib dependency.
- **ChunkMap block change tracking** — `BlockChange` struct + `_blockChangeLog` in `ChunkMap`. `GetPendingChanges()`/`ClearPendingChanges()` for network delta sync.
- **GameState simulation separation** — Created `GameSimulation` (owns `ChunkMap`, `PlayerManager`, `EntityManager`, `DayNightCycle`, `PhysData`). Headless server runs `GameSimulation` without presentation.
- **Project split phase 1** — Moved 16 Raylib-free files to `VoxelgineEngine` (DI, physics, input, noise, threading, logging).
- **Project split phase 2** — Split mixed Raylib/logic classes: `AABB` (pure) + `AABBExtensions` (Raylib bridge), `PhysicsUtils` (math) + `WorldCollision` (ChunkMap), `RayMath` extracted, animations moved to engine.
- **EntityManager network IDs** — `NetworkId` on `VoxEntity`, auto-assigned by `EntityManager.Spawn()`. O(1) lookup dictionary. `SpawnWithNetworkId()`, `IsAuthority` flag.
- **Player/Entity network serialization** — `WriteSnapshot()`/`ReadSnapshot()` on both `Player` and `VoxEntity` with subclass extension points.
- **WeaponGun fire/hit separation** — `FireIntent` → `ApplyFireEffects()` → `ResolveFireIntent()` → `ApplyHitEffects()`. Multiplayer: client sends intent, server resolves, client renders effects.
- **DayNightCycle external time** — `IsAuthority` flag. Clients receive time via `SetTime()` with smooth lerp interpolation (handles 24h wraparound).

## Networking Infrastructure

- **UDP transport** — `UdpTransport` wrapping `System.Net.Sockets.UdpClient`. Async receive loop, thread-safe send.
- **Packet serialization** — `PacketType` enum (24+ types), `Packet` base class, `PacketRegistry`, `BinaryExtensions`. All packet classes with binary serialization.
- **Reliable delivery** — `ReliableChannel` with 9-byte header (reliable flag, sequence, ack sequence, ack bitfield). Duplicate detection, retransmission, piggybacked ACKs.
- **Connection manager** — `NetConnection` per endpoint. RTT measurement (exponential smoothing), timeout detection, `ConnectionState` lifecycle.
- **Bandwidth management** — `BandwidthTracker` (bytes/sec), `PacketBatcher` (MTU-sized batching with 0xFF marker), per-connection outgoing queue with reliable/unreliable prioritization.
- **Packet fragmentation** — `PacketFragmenter` for large reliable packets. Fragment wire format `[0xFE][groupId][index][total][payload]`.

## Server Implementation

- **NetServer core** — UDP bind, protocol version validation, player ID assignment (0-9), max 10 players. Thread-safe receive queue, system packet handling, `SendTo`/`Broadcast`/`BroadcastExcept`/`Kick`.
- **Server game loop** — `ServerLoop` with fixed 66.6 Hz timestep (Stopwatch-based). DI setup, `GameSimulation` ownership, 12-step tick pipeline.
- **Server player management** — Headless `Player` constructor (no GUI/sound/rendering). Per-player `InputMgr` + `NetworkInputSource`. State restore from `PlayerDataStore` on connect, save on disconnect.
- **Server input processing** — `InputStatePacket` → `NetworkInputSource` → `InputMgr` → `Player.UpdatePhysics()`. Camera angle sync. Missing inputs repeat last state.
- **Server world transfer** — `WorldTransferManager` fragments GZip world data (1024B fragments, 8/tick rate limit). FNV-1a checksum verification.
- **Server block change authority** — `HandleBlockPlaceRequest`/`HandleBlockRemoveRequest` with reach validation, inventory check, sound broadcast.
- **Server combat authority** — `HandleWeaponFire` with anti-cheat validation. Server-authoritative raycast (world + entities + player AABBs). Damage, kill detection, `WeaponFireEffectPacket` broadcast.
- **ServerLoop partial class split** — 6 files: core, Connections, Packets, Combat, Broadcasting, Entities.
- **World generation optimization** — Pre-created chunk grid (bypass `SetPlacedBlock`), parallel noise/surface passes, integer bit shifts.
- **Dynamic spawn points** — `FindSpawnPoints()` scans world surface. Force world regeneration via CLI flag.
- **Listen server mode** — Removed single-player `GameState`. Host Game is sole gameplay mode. `ServerLoop` on background thread + `MultiplayerGameState` client.
- **Server world save** — `SaveWorld()` on shutdown + periodic auto-save (5 min). `data/map.bin`.
- **Player state persistence** — `PlayerDataStore` binary files in `data/players/`. Position, health, velocity, inventory (DataVersion 2).
- **Server console commands** — Thread-safe command queue. 9 commands: kick, ban, say, time, save, quit/stop, status, players, help. Chat relay.

## Client Implementation

- **NetClient core** — `ClientState` lifecycle (Disconnected→Connecting→Loading→Playing). Thread-safe receive queue, system packet handling, timeout detection.
- **Client input sending** — `ClientInputBuffer` (128-entry circular buffer). `LocalTick` initialized from server tick.
- **Client-side prediction** — `ClientPrediction` (128-entry prediction buffer, correction threshold 0.01). `PredictionReconciler` replays unACKed inputs from server-confirmed state.
- **Client world loading** — `WorldReceiver` collects fragments, FNV-1a checksum verification. Progress tracking for UI.
- **Remote player rendering** — `RemotePlayer` with `CustomModel` (humanoid), `NPCAnimator`, server-driven animation state (idle/walk/attack), head pitch, held item rendering.
- **Remote player interpolation** — Generic `SnapshotBuffer<T>` (32-entry ring buffer). 100ms interpolation delay. Position/velocity/angle lerp with angle wrapping.
- **Client block change handling** — Optimistic local application + `SendPendingBlockChanges()` to server. Clear pending changes after network tick to prevent echo.
- **Client entity sync** — Entity spawn factory (`CreateEntityByType`), `SnapshotBuffer<EntitySnapshot>` interpolation, animation state mapping. Pending entity packet buffer for pre-world packets.
- **Client combat effects** — `HandleWeaponFireEffect` with blood (NPC/player), sparks (non-NPC entities), fire (world). Tracer rendering.
- **Client disconnect handling** — "Connection Lost" overlay with reconnect (R) and menu (ESC) options. Scene remains visible frozen.
- **Network statistics HUD** — F5 toggle. Ping (color-coded), bandwidth in/out, tick, prediction stats, interpolation buffer counts.

## Synchronization

- **Player position sync** — `WorldSnapshotPacket` at tick rate. Local: prediction + reconciliation. Remote: snapshot interpolation.
- **World block sync** — Full world transfer on join, delta `BlockChangePacket` during play.
- **Entity state sync** — `EntitySpawnPacket` on join, `EntitySnapshotPacket` at tick rate, `EntityRemovePacket` on despawn.
- **Inventory sync** — `ServerInventory` per player, `InventoryUpdatePacket` for corrections. Client prediction with server authority.
- **Particle/sound sync** — `SoundEventPacket` for block break/place. Remote player footstep detection (client-side). Weapon sounds via `WeaponFireEffectPacket`.
- **Day/night sync** — `DayTimeSyncPacket` every 5s. Client lerps to target time (handles 24h wraparound).

## Gameplay Features

- **Player health system** — `Health`/`MaxHealth`/`IsDead`/`TakeDamage`/`ResetHealth`. `FireHitType.Player`. Health synced via `WorldSnapshotPacket`. Health bar UI.
- **Player respawn system** — 3-second server-side timer. `ProcessRespawns()` resets health/position/velocity. Client death overlay with "YOU DIED".
- **Player model** — Humanoid model with server-driven animation state (idle/walk/attack). Head pitch rotation, held item at `hand_r`.
- **Player name tags** — Billboard screen-space rendering. Distance scaling, alpha fade (30–50 units), block obstruction check.
- **Kill feed** — `KillFeedPacket` (S→C). FishUI `ToastNotification` display with fade-out.
- **Player out-of-bounds death** — Server kills players below Y=-50 via `CheckPlayerBounds()`. Respawn system handles recovery.

## UI & Menus

- **Connect to server UI** — FishUI dialog with IP, port, player name inputs. Validation.
- **Host game UI** — FishUI dialog with port, player name, seed inputs. Background `ServerLoop` thread.
- **MultiplayerGameState FishUI refactor** — All HUD elements converted from Raylib to FishUI controls (loading screen, health bar, kill feed toast, net stats panel, death/connection-lost overlays, HUD info label).

## Documentation

- **WORLDBUILDING.md multiplayer review** — Added cooperative gameplay sections (resource dynamics, base building, combat, social dynamics). Updated design pillars and world anchor.

## Resolved Bugs

- **Block placing/destroying broken** — `WeaponPicker.OnLeftClick` not calling `base.OnLeftClick(E)`, and block change echo loop from server broadcasts being re-sent as new requests.
- **New player can't see existing player avatars** — `PlayerJoinedPacket` arrived before `_simulation` existed. Fixed with `_pendingPlayerJoins` buffer.
- **Raycast returns block center not face hit** — Added `ChunkMap.RaycastPrecise()` for exact ray-plane intersection on hit block face.
- **Fire/spark effects invisible on block hits** — `RaycastPrecise` face normal plane mapping was inverted. Hit points were on far side of block.
- **Single-player weapon effects not showing** — `MultiplayerGameState` always non-null. Added `IsActive` property check.
- **Non-NPC entity hits wrong particles** — Blood particles spawned for all entity hits. Fixed to use sparks for non-NPC entities.
- **SoundMgr double initialization crash** — Guarded `InitAudioDevice()` with `IsAudioDeviceReady()` check.
- **Dead code after single-player removal** — Removed dead methods, stale comments, unused usings across 7 files.
- **Lighting computation parallelized** — 8-phase 2×2×2 index parity coloring for `ChunkMap.ComputeLighting()`.
