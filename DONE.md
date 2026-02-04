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
- **Graphics: Fullbright mode** — Added toggleable fullbright rendering via debug menu. Uses `BlockLight.FullbrightMode` flag.
- **Graphics: Underwater overlay** — Added water overlay when player camera is submerged (texture-based or fallback blue tint).
- **Graphics: Improved lighting system** — Separated skylight and block light channels for day/night support. Added ambient light minimum, sky light multiplier, proper cross-chunk propagation, and per-block-type light emission levels.
- **Graphics: Shadow support** — Added ray-traced shadow casting for block lights using 3D DDA algorithm. Entities can now emit light with `LightEmission` property. Added `ShadowTracer`, `PointLight` struct, and `ComputeLightingWithEntities()` for shadow-aware lighting.
- **Graphics: Lighting performance optimizations** — Replaced heap allocations with stack-based arrays in `SetPlacedBlock`, added sky exposure caching per chunk, moved direction arrays to static readonly fields, eliminated array allocations in propagation loops.
- **GUI: Inventory item box textures** — Added state-based textures (normal, selected, hover, pressed) for `FishUIItemBox` from `data/textures/gui/`.
- **Graphics: Glowstone light emission** — Fixed lighting recomputation when placing/removing light-emitting or opaque blocks.
- **Audio: Block placement sounds** — Added sound effects for placing and breaking blocks.
- **Audio: Swimming sound effect** — Added swim sound when player is actively swimming in water.
- **Physics: Jump height** — Increased player jump impulse by 10% (5.5 → 6.05).
- **Graphics: Glass backface rendering** — Added double-sided rendering for glass/ice blocks, skipping backfaces between adjacent same-type blocks.
- **Physics Utils** — Created `PhysicsUtils` class with shared collision functions (`ClipVelocity`, `MoveWithCollision`, `Accelerate`, `AirAccelerate`, `ApplyFriction`, `ApplyGravity`). Enhanced `AABB` with `Overlaps()` and helper properties. Refactored `EntityManager` and `Player` to use shared utilities.
- **Animation System** — Lerp system with comprehensive easing functions (Quad, Cubic, Quart, Quint, Sine, Expo, Circ, Back, Elastic, Bounce)
- **NPC Animation System** — Added keyframe-based animation for JSON models (`NPCAnimationClip`, `NPCAnimator`). Supports walk, idle, attack, crouch animations. Added `NPCPreviewState` for testing animations from main menu.
- **Player Movement** — Quake-style physics (strafe-jumping, bunny-hopping, air control, clip velocity, swimming)
- **Physics: Water buoyancy** — Added proper buoyancy force so player floats in water instead of sinking quickly
- **Rendering** — Frame interpolation for smooth camera/position/view model rendering
- **Unit Testing** — Tests for AABB, Easing, Utils, Noise

---

## Documentation

- **Full codebase documentation** — Updated README.md with comprehensive architecture, controls, and project status; added XML documentation to core classes (Player, VoxEntity, ChunkMap, Chunk, PlacedBlock, BlockInfo, GameState, PhysicsUtils, FishUIManager)

---

## Bug Fixes

- **Water exit boost** — Fixed by applying 15% velocity boost when player exits water with upward momentum
- **JSON model UV mapping** — Fixed UV coordinate calculation in MeshGenerator; now uses texture size instead of GlobalScale for normalization
- **Particle System** — Fixed depth ordering and underwater rendering (physics resistance, blue tint)
- **Transparent Blocks** — Fixed depth sorting for water/glass overlap
- **Unit Tests** — Fixed noise seed test with larger coordinates
- **GUI: Main menu button clipping** — Fixed button borders being cut off by adding padding to StackLayout
- **Lighting propagation** — Fixed struct copy bug in PlacedBlock.SetSkylight/SetBlockLightLevel; fixed opaque block face lighting to sample from adjacent air blocks instead of the opaque block itself
- **Cross-chunk light propagation** — Fixed neighbor chunks not being marked dirty when light propagates across chunk boundaries; glowstone near edges now properly updates adjacent chunk meshes
- **Chunk mesh lighting seams** — Fixed by splitting lighting computation into reset and compute phases; all chunks now reset before any propagation to prevent cross-chunk values from being overwritten

---

## Code Refactoring

- **Player class file split** — Split `Player.cs` into 6 partial class files: `Player.cs` (core), `Player.Physics.cs`, `Player.Input.cs`, `Player.GUI.cs`, `Player.Rendering.cs`, `Player.Serialization.cs`
- **Chunk class file split** — Split `Chunk` into 6 partial class files: `Chunk.Base.cs` (core), `Chunk.Lighting.cs`, `Chunk.Rendering.cs`, `Chunk.Serialization.cs`, `Chunk.GenMesh.cs` (opaque), `Chunk.GenMeshTransparent.cs` (transparent)
