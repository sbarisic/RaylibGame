# Aurora Falls - Voxelgine Engine TODO

A list of planned features, improvements, and tasks for this project.

> **CPX (Complexity Points)** - 1 to 5 scale:
> - **1** - Single file control/component
> - **2** - Single file control/component with single function change dependencies
> - **3** - Multi-file control/component or single file with multiple dependencies, no architecture changes
> - **4** - Multi-file control/component with multiple dependencies and significant logic, possible minor architecture changes
> - **5** - Large feature spanning multiple components and subsystems, major architecture changes

> Instructions for the TODO list:
- Move all completed TODO items into a separate Completed document (DONE.md) and simplify by consolidating/combining similar ones and shortening the descriptions where possible

> How TODO file should be iterated:
- First handle the Uncategorized section, if any similar issues already are on the TODO list, increase their priority instead of adding duplicates (categorize all at once)
- When Uncategorized section is empty, start by fixing Active Bugs (take one at a time)
- After Active Bugs, handle the rest of the TODO file by priority and complexity (High priority takes precedance, then CPX points) (take one at a time).

---

## Project Overview

**Aurora Falls** is a voxel-based 3D game engine built with **Raylib-cs** targeting **.NET 9**.

### Current Architecture

| System | Status | Description |
|--------|--------|-------------|
| **Core Engine** | ✅ | `GameWindow`, `GameState`, `GameConfig`, `InputMgr`, `SoundMgr`, `ResMgr` |
| **Graphics** | ✅ | `ChunkMap`, `Chunk`, GBuffer deferred rendering, `Skybox`, `Frustum` culling |
| **Voxel World** | 🔶 | Procedural island generation via simplex noise, block types, lighting system |
| **Entity System** | 🔶 | `VoxEntity` base, `VEntPickup`, `VEntNPC`, `EntityManager` with basic physics |
| **Player** | ✅ | `Player`, `FPSCamera`, `ViewModel`, inventory system |
| **Weapons** | ✅ | `Weapon`, `WeaponGun`, `WeaponPicker`, `InventoryItem` |
| **GUI** | 🔶 | `GUIManager`, `GUIWindow`, `GUIButton`, `GUILabel`, `GUIInputBox`, `GUIImage`, Flexbox layout |
| **Particles** | 🔶 | `ParticleSystem` with smoke effects |
| **Animation** | 🔶 | `AnimLerp`, `LerpManager`, easing functions |
| **Physics** | 🔶 | `AABB`, `PhysData`, collision in `EntityManager` and `Player` |
| **NPC/AI** | ⬜ | Basic `VEntNPC` exists, no AI/pathfinding |
| **Scripting** | ⬜ | `Scripting.cs` exists (empty/stub) |
| **Mod System** | ⬜ | Not implemented |

Legend: ✅ Functional | 🔶 Partial/WIP | ⬜ Planned

---

## Features

### High Priority

*No high priority items*

### Medium Priority

- [ ] **Voxel World: Procedural buildings/structures** — Generate structures on the island using prefabs or procedural rules **[CPX: 4]**
- [ ] **Centralized Physics System** — Consolidate collision detection from `Player` and `EntityManager` into a unified physics system **[CPX: 4]**
- [ ] **NPC AI System** — Implement AI goals system and behavior trees for `VEntNPC` **[CPX: 4]**
- [ ] **Pathfinding: Voxel navigation** — A* or similar pathfinding over voxel terrain for ground entities **[CPX: 4]**
- [ ] **Pathfinding: Air navigation** — 3D pathfinding for flying entities **[CPX: 3]**

### Lower Priority

- [ ] **Block Placement Preview** — Render wireframe outline showing where block would be placed when holding a block item **[CPX: 2]**
- [ ] **Graphics: Lighting system enhancements** — Improve the existing `ComputeLighting` with dynamic lights, shadows **[CPX: 4]**
- [ ] **Graphics: Fullbright/debug modes** — Add toggleable fullbright and debug rendering modes in settings **[CPX: 2]**
- [ ] **Entity: Sliding door entity** — Door that slides into wall when player approaches, toggles collision **[CPX: 2]**
- [ ] **Mod System: Expose functionality** — Create mod API exposing game systems for external mods **[CPX: 5]**
- [ ] **Mod System: Example mod** — Implement a sample mod demonstrating the API **[CPX: 2]**

---

## Improvements

- [ ] **GUI: Complete element set** — Ensure all standard elements (Window, Button, Label, Input, Image) are fully functional and styled consistently **[CPX: 2]**

---

## Documentation **LOW PRIORITY**

- [ ] API reference documentation
- [ ] Getting started guide
- [ ] Architecture overview

---

## Code Cleanup & Technical Debt

### Code Refactoring

*No refactoring items*

---

## Known Issues / Bugs

### Active Bugs

*No active bugs*

### Uncategorized (Analyze and create TODO entries in above appropriate sections with priority. Do not fix or implement them just yet. Assign complexity points where applicable. Do not delete this section when you are done, just empty it)

*No uncategorized items*

---

## Notes

- Try to edit files and use tools WITHOUT POWERSHELL where possible, shell scripts get stuck and then manually terminate
- Maintain the "dependency-free" philosophy - keep the core library minimal
- Do not be afraid to break backwards compatibility if new changes will simplify or improve the project
- Do not use powershell commands unless absolutely necessary
- If you encounter features in systems which are required when working on a current improvement/bugfix, extend that system with the required features as part of the current task instead of creating separate TODO entries
- Problem solutions need to be optimized, performant and well thought out before implementation, avoid quick fixes

---

## Completed

### Features

- [x] **Voxel World: Procedurally generated island** — Simplex noise based terrain generation
- [x] **Voxel World: Transparent blocks** — Support for transparent block rendering
- [x] **GUI System: Core elements** — Window, Button, Label, Input, Image elements implemented
- [x] **Entity System: Pickup entity** — `VEntPickup` for weapons, ammo, armor pickups
- [x] **Entity System: NPC entity** — `VEntNPC` base class exists
- [x] **Animation System: Base lerp system** — `AnimLerp`, `LerpManager` with easing functions
- [x] **Unit Testing** — Tests for AABB, Easing, Utils, Noise (made Easing class public)
- [x] **Quake-like player movement** — Strafe-jumping, bunny-hopping, air control via proper Quake acceleration model
- [x] **Voxel World: Real-time block creation/destruction** — Left-click destroy, right-click place with automatic mesh rebuilding
- [x] **Collision: Quake-style clip velocity** — ClipVelocity slides along surfaces preserving momentum, multi-plane clipping for corners
- [x] **Rendering: Frame interpolation** — Interpolated camera, position, and view model between physics frames for smooth rendering

### Improvements

- [x] **Animation: Easing functions expansion** — Extended `Easing` with Quad, Cubic, Quart, Quint, Sine, Expo, Circ, Back, Elastic, Bounce (In/Out/InOut)

### Fixed Bugs

- [x] **Particle System: Depth ordering** — Sorted particles back-to-front for proper alpha blending
- [x] **Particle System: Underwater rendering** — Particles now detect water, apply physics resistance and blue tint
- [x] **Unit Test: Noise seed test** — Fixed test using larger coordinates for seed differentiation
- [x] **Transparent blocks: Depth sorting** — Water/glass now render correctly when overlapping via face-level depth sorting
