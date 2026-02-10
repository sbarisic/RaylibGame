# Aurora Falls - Completed Items

Consolidated list of completed features, improvements, and bug fixes.

---

## Features

- **Voxel World** — Procedural island generation, transparent blocks, real-time block creation/destruction, block placement preview
- **Entity System** — Pickup entity (`VEntPickup`), NPC entity base (`VEntNPC`), sliding door entity (`VEntSlidingDoor`)
- **GUI System** — Migrated to FishUI with Raylib backend (`FishUIManager`, `RaylibFishUIGfx`, `RaylibFishUIInput`). Custom controls: `FishUIItemBox`, `FishUIInventory`, `FishUIInfoLabel`. Removed old GUI code.
- **GUI: Main menu refactor** — Refactored main menu to use FishUI `ScrollablePane` with properly positioned buttons, tooltips, and `OnButtonPressed` event pattern.
- **GUI: Main menu title logo** — Added `ImageBox` displaying game logo at top of main menu.
- **GUI: In-game debug menu** — Added FishUI debug window (F1 key) with debug toggle, save/load game, regenerate world, and main menu buttons.
- **Debug: NOCLIP toggle** - Added NOCLIP checkbox to F1 debug menu allowing player to fly through blocks without collision.
- **Graphics: Fullbright mode** — Added toggleable fullbright rendering via debug menu. Uses `BlockLight.FullbrightMode` flag.
- **Graphics: Underwater overlay** — Added water overlay when player camera is submerged (texture-based or fallback blue tint).
- **Graphics: Improved lighting system** — Separated skylight and block light channels for day/night support. Added ambient light minimum, sky light multiplier, proper cross-chunk propagation, and per-block-type light emission levels.
- **Graphics: Shadow support** — Added ray-traced shadow casting for block lights using 3D DDA algorithm. Entities can now emit light with `LightEmission` property. Added `ShadowTracer`, `PointLight` struct, and `ComputeLightingWithEntities()` for shadow-aware lighting.
- **Graphics: Lighting performance optimizations** — Replaced heap allocations with stack-based arrays in `SetPlacedBlock`, added sky exposure caching per chunk, moved direction arrays to static readonly fields, eliminated array allocations in propagation loops. **Additional pass:** Replaced HashSet with flat array for visited tracking, replaced per-light Queue allocations with reusable fixed-size arrays and head/tail indices, cached chunk world positions to avoid repeated `GetWorldPos` calls, optimized `QueueBoundaryBlocksForExternalLight` with bounding box intersection culling to limit iteration scope.
- **Graphics: View model lighting** � View model now samples light level at player position and renders with appropriate brightness.
- **Graphics: Day/night cycle** � Added `DayNightCycle` class with time-based skylight adjustment, sky color transitions (dawn/day/dusk/night), configurable day length. Integrated into `GameState` with debug menu time controls (Dawn/Noon/Dusk/Night buttons) and HUD time display.
- **Weapons: Tracer lines** � Added tracer line effect from muzzle to hit point, fades out over 0.15s with additive blending
- **Particles: Blood effect** � Added blood particles (`SpawnBlood`) ejected from hit normal, falls with gravity, fades out after 6-10s. Spawns when shooting NPCs.
- **Combat: Body part hit detection** � Added `VEntNPC.RaycastBodyPart()` that detects which mesh (head, body, leg_r, etc.) was hit using animation-aware mesh collision. Gun prints hit part name.
- **Combat: Hit detection with position/normal** � Gun raycast now checks entities and world, uses closer hit with precise position/normal for particle spawning
- **Raycasting: Entity picking utility** � Ray-AABB intersection (`Raycast.cs`) with hit position/normal for shooting, pickups, and interactions. Added `EntityManager.Raycast()` and `RaycastAll()` methods.
- **Graphics: Sun/Moon rendering** � Added sun and moon textures rendered in screen-space with proper position calculation based on day/night cycle. Sun changes color at horizon, moon appears at night.
- **Pathfinding: Voxel navigation** � Added A* pathfinder (`VoxelPathfinder`) for 3D voxel terrain with configurable entity height/width, jump/fall distances. Created `PathFollower` component for path following with waypoint management. Integrated into `VEntNPC` with debug path visualization. Added `ChunkMap.FindPath()` and `ChunkMap.CreatePathfinder()` utility methods.
- **GUI: Inventory item box textures** — Added state-based textures (normal, selected, hover, pressed) for `FishUIItemBox` from `data/textures/gui/`.
- **Graphics: Glowstone light emission** — Fixed lighting recomputation when placing/removing light-emitting or opaque blocks.
- **Audio: Block placement sounds** — Added sound effects for placing and breaking blocks.
- **Audio: Swimming sound effect** — Added swim sound when player is actively swimming in water.
- **Audio: Gun shooting sound** � Added shoot1 sound combo for weapon firing.
- **Weapons: Automatic fire** - Gun fires continuously while left mouse held (via `SupportsAutoFire` property).
- **Physics: Player bounding box** - Added `BBox` property with automatic recalculation on position change.
- **Weapons: Hammer swing animation** - Added `ApplySwing()` method to ViewModel and `LerpFloat` animation class.
- **Physics: Jump height** — Increased player jump impulse by 10% (5.5 → 6.05).
- **Graphics: Glass backface rendering** — Added double-sided rendering for glass/ice blocks, skipping backfaces between adjacent same-type blocks.
- **Physics Utils** — Created `PhysicsUtils` class with shared collision functions (`ClipVelocity`, `MoveWithCollision`, `Accelerate`, `AirAccelerate`, `ApplyFriction`, `ApplyGravity`). Enhanced `AABB` with `Overlaps()` and helper properties. Refactored `EntityManager` and `Player` to use shared utilities.
- **Animation System** — Lerp system with comprehensive easing functions (Quad, Cubic, Quart, Quint, Sine, Expo, Circ, Back, Elastic, Bounce)
- **NPC Animation System** — Added keyframe-based animation for JSON models (`NPCAnimationClip`, `NPCAnimator`). Supports walk, idle, attack, crouch animations. Added `NPCPreviewState` for testing animations from main menu.
- **NPC Animation: Save/Load clips** � Added JSON serialization for `NPCAnimationClip` with `Save()`/`Load()` methods, `EasingSerializer` for easing function mapping, and `LoadClip()`/`LoadAllClips()` on `NPCAnimator`.
- **NPC Animation: Layered playback** � Added `AnimationLayer` class and layer-based API (`PlayOnLayer`, `StopLayer`, `PauseLayer`, `ResumeLayer`, `SetLayerWeight`) to `NPCAnimator` for playing multiple animation clips simultaneously with additive blending.
- **Player Movement** — Quake-style physics (strafe-jumping, bunny-hopping, air control, clip velocity, swimming)
- **Physics: Water buoyancy** — Added proper buoyancy force so player floats in water instead of sinking quickly
- **Rendering** — Frame interpolation for smooth camera/position/view model rendering
- **Unit Testing** — Tests for AABB, Easing, Utils, Noise
- **Weapons: Require aim to fire** — Gun now requires right-click (aim/ironsight) to be held before firing. Moved aim handling from base `InventoryItem.Tick()` to `WeaponGun.Tick()` override with `IsAiming` property.
- **Particles: Fire effect** — Added `SpawnFire()` method with fire textures (1-4.png). Fire rises upward with random drift, semi-transparent, short-lived (0.6-1.0s), shrinks over lifetime, supports initial force/direction for wall impact effects. Added `ParticleType` enum for type-specific behavior.
- **Particles: Spark effect** — Added `SpawnSpark()` with spark textures (1-4.png). Sparks orient along movement direction via `DrawBillboardPro`, fall slowly with gravity, shrink over 1.2-2.0s lifetime, additive blend, emissive.
- **Weapons: Gun fire particles** — Gun now spawns fire particles instead of smoke on impact, using the wall normal as initial force direction.
- **Entities: Door model** — `VEntSlidingDoor` uses `door/door.json` Blockbench model via `CustomModel` with procedurally generated wood texture, facing direction serialization, and server spawn.
works for any connected client.
- **Block Models: Replace OBJ with JSON/Blockbench models** — Replaced OBJ-based custom model baking with JSON/Blockbench `CustomModel` pipeline for barrel, campfire, and torch. Created `torch.json` from `torch.bbmodel`. Custom model blocks render separately with own textures via per-chunk tracking and `DrawWithMatrix()`. Fixed campfire/torch not rendering as 3D models. Removed debug logging from `GenMesh`.
- **Door: Hinge rotation** — Converted `VEntSlidingDoor` from linear sliding to Y-axis hinge rotation. Door now rotates around the left edge using a composed matrix (translate hinge to origin → rotate → translate back → facing rotation → world position). Replaced slide fields with `OpenAngleDeg`/`OpenSpeed`/`OpenProgress`. Collision disables immediately when door starts opening. Updated serialization and spawn calls.
- **Foliage: Random grass variety** — Added `Foliage` as a custom model block type with 3 grass variants (`grass1-3.json`). Deterministic variant selection per block position via spatial hash. Added noise-based foliage placement in world generation on grass surface blocks.
- **World Gen: Improved ponds** — Ponds now use noise-modulated radius for irregular organic shapes (0.55–1.0× base radius per position), are larger (5–10 radius, 2–4 depth), and have a containment pass that seals basin floor with stone and sides with sand to prevent water spilling through gaps.
- **ViewModel: Submerged lowering** — Viewmodel rotates 25° downward when player is in water, with smooth interpolation in/out. Detects water via `ChunkMap.IsWaterAt` at player eye position.
- **Chunk: Block particle emission** — Campfire blocks emit fire particles every ~0.25s. `ChunkMap.EmitBlockParticles` iterates visible chunks' `CachedCustomModelBlocks`, spawning fire at campfire positions via `ParticleSystem.SpawnFire`.
- **Multiplayer: Client-server architecture** — UDP transport with custom reliability layer (`ReliableChannel`), packet fragmentation/batching, RTT measurement. Server-authoritative `GameSimulation` at 66.6 Hz. Up to 10 players. Protocol: 24+ packet types with binary serialization. See [MULTIPLAYER.md](MULTIPLAYER.md) for protocol reference.
- **Multiplayer: Client prediction & reconciliation** — Client-side movement prediction using Quake physics, server reconciliation with input replay, visual correction smoothing. Remote player/entity snapshot interpolation (100ms buffer via `SnapshotBuffer<T>`).
- **Multiplayer: World sync** — GZip-compressed full world transfer on connect (fragmented, FNV-1a checksum). Delta block changes during play with server authority validation.
- **Multiplayer: Combat** — Server-authoritative weapon fire resolution (world + entity + player AABB raycast). Predicted fire effects (instant local tracers/particles). Kill feed, player health/respawn (3s timer), player damage sync.
- **Multiplayer: Listen server mode** — Host Game hosts local `ServerLoop` on background thread + connects `MultiplayerGameState` as client. Dedicated headless server via `VoxelgineServer`. Player state persistence (`PlayerDataStore`), world auto-save (5 min), server console commands.
- **Multiplayer: Chat & UI** — Connect/Host dialogs, player list (Tab), network stats HUD (F5), death/disconnect overlays, text chat with `/commands`, connection status indicator with ping color coding.
- **Multiplayer: Player chat commands** — `/commands` now sent to server via chat (works for all players, not just host). Server intercepts and routes to `HandlePlayerCommand`. Added `/comehere` command (all NPCs navigate to sender). Added "NPC Come Here" button to F1 debug menu. Added `SendServerMessageTo` for per-player server feedback.
- **Multiplayer: Remote players** — `RemotePlayer` with humanoid `CustomModel`, `NPCAnimator` (idle/walk/attack), head pitch, held item rendering, billboard name tags with distance fade and obstruction check.
- **Multiplayer: Procedural world gen** — Noise-based tree placement (6–10 block trunks, leaf canopy), water bodies (sand shoreline, depth tapering), procedural roads (Bresenham paths), terrain height variation (2D noise displacement), dynamic spawn point selection.

---

## Documentation

- **Full codebase documentation** — Updated README.md with comprehensive architecture, controls, and project status; added XML documentation to core classes (Player, VoxEntity, ChunkMap, Chunk, PlacedBlock, BlockInfo, GameState, PhysicsUtils, FishUIManager)
- **WORLDBUILDING.md multiplayer review** — Added cooperative gameplay sections (resource dynamics, base building, combat, social dynamics)

---

## Bug Fixes

- **Animation attachment points** — Fixed child model parts (hands) not following parent transforms (torso) during animations by implementing parent-child hierarchy in `CustomMesh` with `GetCombinedAnimationMatrix()`
- **Water exit boost** — Fixed by applying 15% velocity boost when player exits water with upward momentum
- **JSON model UV mapping** — Fixed UV coordinate calculation in MeshGenerator. Minecraft/Blockbench JSON models use 0-16 UV coordinate space that maps to the full texture regardless of resolution. Now divides by 16 instead of texture size. Fixed face UV vertex mappings to correctly map texture corners (UV.Y=0 → top, UV.Y=1 → bottom).
- **Particle System** — Fixed depth ordering and underwater rendering (physics resistance, blue tint)
- **Transparent Blocks** — Fixed depth sorting for water/glass overlap
- **Unit Tests** — Fixed noise seed test with larger coordinates
- **GUI: Main menu button clipping** — Fixed button borders being cut off by adding padding to StackLayout
- **Lighting propagation** — Fixed struct copy bug in PlacedBlock.SetSkylight/SetBlockLightLevel; fixed opaque block face lighting to sample from adjacent air blocks instead of the opaque block itself
- **Cross-chunk light propagation** — Fixed neighbor chunks not being marked dirty when light propagates across chunk boundaries; glowstone near edges now properly updates adjacent chunk meshes
- **Chunk mesh lighting seams** — Fixed by splitting lighting computation into reset and compute phases; all chunks now reset before any propagation to prevent cross-chunk values from being overwritten
- **Physics: Corner collision creep/stick** — Fixed player creeping into blocks and getting stuck when holding forwards into a corner. Replaced blind 10% movement fraction in `QuakeMoveWithCollision` with binary-search collision fraction (`FindCollisionFraction`) and proper multi-plane crease handling that projects velocity along the intersection of two blocking planes
- **Multiplayer: Prediction tick desync** — Fixed sluggish movement at high ping caused by `LocalTick` freezing during world loading. Server now tracks per-player `LastInputTick` for independent tick comparison.
- **Multiplayer: Block placement propagation** — Fixed `InventoryUpdatePacket` dropping before simulation existed. Client buffers pre-simulation packets; server re-sends inventory after world transfer.
- **Multiplayer: Viewmodel rendering** — Fixed viewmodel at wrong screen position (moved overlay to after `EndMode3D`), broken arm orientation (rewrote rotation to clean 3-quaternion composition), direction vectors zero during tick.
- **Multiplayer: Various network fixes** — Block placing/destroying echo loop, missing remote player avatars on join, raycast face hit precision, weapon effect particle types, `SoundMgr` double-init crash, cross-chunk skylight propagation at chunk boundaries.
- **Multiplayer: Render distance & lighting** — Reduced render distance to 128 blocks. Deferred relighting for chunks outside render distance via `NeedsRelighting` flag.
- **NPC Pathfinding: Gets stuck on blocks** — Added wall proximity cost to A* (paths prefer 1-block clearance from walls), fixed `GetRandomWanderTarget` always returning first random direction without walkability check, improved stuck recovery order (recalculate path → jump → wander), added waypoint progress tracking to stuck detection.
- **NPC: No animation or facing direction in multiplayer** — `GetEntityAnimationState` relied on `Animator` which is null on the headless server (no GPU model loading); fixed to derive walk/idle state from velocity. Client now derives NPC look direction from synced velocity via `SetLookDirection`.
- **NPC AI: Behavior tree VM** — Added instruction-based AI program system (`AIInstruction`, `AIStep`, `AIRunner`, `AIPrograms`). NPC behavior defined as a list of steps with success/failure branching (like a simple VM). Instructions: `Idle`, `MoveRandom`, `MoveToPlayer`, `IsPlayerNearby`, `LookAtPlayer`, `Goto`. Default program: approach nearby players, otherwise wander randomly. Replaced hardcoded idle wandering in `VEntNPC`.
- **Graphics: Entity lighting** — All entities and remote players now sample block light level at their feet and tint their model color accordingly. Added `GetEntityLightColor()` to `VoxEntity`, `Draw(Color tint)` to `CustomModel`.
- **AI: Event system** — Added event-driven interrupts to the AI VM. New `AIEvent` enum (OnPlayerTouch, OnPlayerSight, OnAttacked), `AIInstruction.EventHandler` marker instruction, and `AIRunner.RaiseEvent()` with per-event cooldown. Events interrupt the current program and jump to the handler; handler uses Goto to resume. `VEntNPC.OnPlayerTouch` and `OnAttacked` raise events; sight check runs periodically in the runner. Updated `DefaultWander` program with touch/attack handlers.
- **NPC: Speech bubbles** — NPCs can display text bubbles above their head via `VEntNPC.Speak(text, duration)`. Rendered in 2D screen space with distance-based scaling/fading. Synced to clients via `EntitySpeechPacket`. Added `VoxEntity.Draw2D()` virtual and `EntityManager.Draw2D()` loop. Added `/speak` player command.
- **AI: Wait & Speak instructions** — Added `Wait(time)` (exact duration, no randomness) and `Speak(text, duration)` AI instructions. Added `AIStep.TextParam` for string parameters, `AIStep.SpeakText()` factory. Server auto-broadcasts `EntitySpeechPacket` via dirty flag when any NPC speaks. Implemented `FunkyBehavior` AI program demonstrating the new instructions with event handlers.
- **AI: AsyncSpeak & MoveToPlayer stop distance** — Added `AsyncSpeak` instruction (fire-and-forget speech), `AIStep.Param2` for secondary parameters, `MoveToPlayerAt(radius, stopDistance)` factory. Fixed MoveToPlayer navigating to exact player position instead of stopping short — now computes a nav target `stopDistance` blocks from the player and includes an early-out if already in range.

---

## Performance

- **Chunk: GenMesh/GenMeshTransparent optimization** — Added padded 18³ block cache (`BuildPaddedCache`) pre-fetching 1-block border from neighbors, converting all per-face lookups to O(1) array access. Added `NonAirBlockCount` tracking for empty chunk early-out (skip entire 16³ iteration). Eliminated redundant `GetBlock`/`IsOpaque` calls by caching 6 neighbor references and flags once per block. Added `CalcAOColorPadded` using integer offsets into padded cache instead of expensive world-space `WorldMap.GetBlock()` calls (TranslateChunkPos + Dictionary lookup per call).
- **Lighting: Fixed-size array BFS queues** — Replaced `Queue<T>` in skylight/block-light propagation with pre-allocated flat arrays and head/tail indices, eliminating resize copies and GC pressure.
- **Lighting: Hoisted GetWorldPos in PropagateSkylight** — Cached chunk world origin before BFS loop instead of calling `GetWorldPos` per boundary block.
- **Lighting: Generation-counter visited tracking** — Replaced `bool[]` + `Array.Clear(4096)` per light source with `int[]` generation stamps, eliminating O(n) clear per light propagation call.
- **BlockInfo: Pre-computed lookup tables** — Replaced `switch`-based `IsOpaque`/`EmitsLight`/`GetLightEmission`/`IsRendered`/`IsSolid` with static `bool[]`/`byte[]` arrays built once at startup, reducing branch overhead in hot BFS loops.
- **PlacedBlock: class → struct with InlineArray** — Converted `PlacedBlock` from heap-allocated class to value-type struct with `[InlineArray(6)]` inline light storage; eliminates 2 heap objects per block (~2.6× memory reduction, zero GC tracking, contiguous cache-friendly layout).
- **Network: Skip redundant entity snapshots** — `BroadcastEntitySnapshots` now tracks last-sent state (position, velocity, animation) per entity and skips unchanged snapshots. Stale entries pruned when entities are removed.
- **Multiplayer: Prediction/reconciliation optimization** — Increased correction threshold (0.01→0.1), added velocity divergence check, visual error smoothing via `_correctionSmoothOffset`. Pre-allocated replay list to eliminate per-reconciliation GC allocation.
- **Multiplayer: World generation optimization** — Pre-created chunk grid (bypass `SetPlacedBlock`), parallel noise/surface passes, integer bit shifts.

---

## Testing

- **Multiplayer unit tests** — 64 network tests covering packet serialization, `ClientPrediction` thresholds/reconciliation, `ClientInputBuffer` operations, `ReliableChannel` wrap/ACK/retransmission, and `SnapshotBuffer` interpolation.
- **Network diagnostics** — Categorized logging (`ServerWriteLine`/`ClientWriteLine`), packet logger toggle, `NetworkSimulation` class with configurable latency/loss/jitter and F1 debug menu controls.

---

## Code Refactoring

- **Upgraded to .NET 10** — Migrated `Voxelgine` and `VoxelgineEngine` targets from .NET 9 to .NET 10.
- **Player class file split** — Split `Player.cs` into 6 partial class files: `Player.cs` (core), `Player.Physics.cs`, `Player.Input.cs`, `Player.GUI.cs`, `Player.Rendering.cs`, `Player.Serialization.cs`
- **Chunk class file split** — Split `Chunk` into 6 partial class files: `Chunk.Base.cs` (core), `Chunk.Lighting.cs`, `Chunk.Rendering.cs`, `Chunk.Serialization.cs`, `Chunk.GenMesh.cs` (opaque), `Chunk.GenMeshTransparent.cs` (transparent)
- **ChunkMap class file split** — Split `ChunkMap.cs` (1615 lines) into 6 partial class files: `ChunkMap.cs` (core fields, block access, utility), `ChunkMap.Generation.cs` (world gen), `ChunkMap.Lighting.cs` (lighting computation), `ChunkMap.Rendering.cs` (draw, transparent faces, particles), `ChunkMap.Collision.cs` (raycasting, collision, pathfinding), `ChunkMap.Serialization.cs` (save/load). Applied optimizations: eliminated LINQ in `RaycastRay` (removed List/Where/OrderBy/FirstOrDefault, replaced with single-pass closest-hit loop), simplified `IsSolid` (removed redundant if/return pattern), cleaned dead code in `EmitBlockParticles` (removed always-false branch).
- **Particles: Transparent block sorting** � Fixed particles rendering in front of glass/water by reordering draw calls: particles now render before transparent blocks
- **Logging: Replace Console.* with IFishLogging** � Replaced all `Console.WriteLine` calls with `IFishLogging.WriteLine` via DI across all engine files. Removed unused Flexbox library. Updated README.md.
- **Mod System: Architecture planning** � Analyzed entire codebase (DI, entities, world, player, weapons, particles, sound, resources, input, GUI, scripting, pathfinding, day/night) and created comprehensive [TODO_MODS.md](TODO_MODS.md) with mod system architecture, API design, implementation plan, and ~30 prioritized subtasks.
- **Multiplayer System: Architecture & implementation** — Full client-server multiplayer from architecture planning through implementation. Core refactoring: FPSCamera static→instance, `PlayerManager`, `IInputSource` abstraction, `GameSimulation` separation, three-project split (`VoxelgineEngine`/`VoxelgineServer`), `EntityManager` network IDs, `WeaponGun` fire/hit separation, `DayNightCycle` authority, `ViewModel` JSON model migration. `ServerLoop` split into 6 partial class files. `BlockInfo.IsRendered()` centralized for mesh/lighting checks. See [MULTIPLAYER.md](MULTIPLAYER.md) for protocol reference.
