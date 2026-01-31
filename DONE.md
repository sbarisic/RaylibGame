# Aurora Falls - Completed Items

Consolidated list of completed features, improvements, and bug fixes.

---

## Features

- **Voxel World** — Procedural island generation, transparent blocks, real-time block creation/destruction, block placement preview
- **Entity System** — Pickup entity (`VEntPickup`), NPC entity base (`VEntNPC`)
- **GUI System** — Migrated to FishUI with Raylib backend (`FishUIManager`, `RaylibFishUIGfx`, `RaylibFishUIInput`). Custom controls: `FishUIItemBox`, `FishUIInventory`, `FishUIInfoLabel`. Removed old GUI code.
- **Physics Utils** — Created `PhysicsUtils` class with shared collision functions (`ClipVelocity`, `MoveWithCollision`, `Accelerate`, `ApplyFriction`). Enhanced `AABB` with `Overlaps()` and helper properties. Refactored `EntityManager` to use shared utilities.
- **Animation System** — Lerp system with comprehensive easing functions (Quad, Cubic, Quart, Quint, Sine, Expo, Circ, Back, Elastic, Bounce)
- **Player Movement** — Quake-style physics (strafe-jumping, bunny-hopping, air control, clip velocity)
- **Rendering** — Frame interpolation for smooth camera/position/view model rendering
- **Unit Testing** — Tests for AABB, Easing, Utils, Noise

---

## Bug Fixes

- **Particle System** — Fixed depth ordering and underwater rendering (physics resistance, blue tint)
- **Transparent Blocks** — Fixed depth sorting for water/glass overlap
- **Unit Tests** — Fixed noise seed test with larger coordinates
