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
| **Voxel World** | ✅ | Procedural island generation via simplex noise, block types, dual-channel lighting (skylight/block light) |
| **Entity System** | 🔶 | `VoxEntity` base, `VEntPickup`, `VEntNPC`, `EntityManager` with basic physics |
| **Player** | ✅ | `Player`, `FPSCamera`, `ViewModel`, inventory system |
| **Weapons** | ✅ | `Weapon`, `WeaponGun`, `WeaponPicker`, `InventoryItem` |
| **GUI** | ✅ | FishUI-based: `FishUIManager`, `RaylibFishUIGfx`, custom controls (`FishUIItemBox`, `FishUIInventory`, `FishUIInfoLabel`) |
| **Particles** | 🔶 | `ParticleSystem` with smoke effects |
| **Animation** | 🔶 | `AnimLerp`, `LerpManager`, easing functions |
| **Physics** | ✅ | `AABB`, `PhysData`, `PhysicsUtils` with shared collision/movement utilities; used by `EntityManager` and `Player` |
| **NPC/AI** | ⬜ | Basic `VEntNPC` exists, no AI/pathfinding |
| **Scripting** | ⬜ | `Scripting.cs` exists (empty/stub) |
| **Mod System** | ⬜ | Not implemented |

Legend: ✅ Functional | 🔶 Partial/WIP | ⬜ Planned

---

## Features

### High Priority

*No high priority items*

### Medium Priority

- [ ] **Particles: Spark effect** — Add spark particle using textures (`data/textures/spark/1-4.png`), oriented in movement direction, falls slowly with gravity, lives twice as long as fire (1.2-2.0s) **[CPX: 2]**
- [ ] **Pathfinding: Voxel navigation** — A* or similar pathfinding over voxel terrain for ground entities (as a utility in the world class?) **[CPX: 4]**

### Lower Priority

- [ ] **Voxel World: Procedural buildings/structures** **[CPX: 3]**
- [ ] **Mod System: Expose functionality** — Create mod API exposing game systems for external mods **[CPX: 5]**
- [ ] **Mod System: Example mod** — Implement a sample mod demonstrating the API **[CPX: 2]**
- [ ] **NPC AI System** — Complete NPC entities with AI goals system and behavior trees for `VEntNPC` **[CPX: 4]**
- [ ] **Input: Key rebinding system** — Add input mapping/rebinding support to `InputMgr` for customizable controls **[CPX: 3]**

---

## Improvements

### High Priority

*No high priority items*

### Medium Priority

- [ ] **Animation: View model mode transitions** — Add smooth toggle animations when switching view model rotation modes (e.g., hip to ironsight) **[CPX: 2]**
- [ ] **Physics: Player bounding box** — Implement proper bounding box calculation for `Player` based on position and dimensions **[CPX: 1]**
- [ ] **GUI: Disabled inventory items** — Draw inventory items as visually disabled when count reaches 0 **[CPX: 1]**

---

## Documentation

### Medium Priority

- [ ] **README.md update** — Update with newest project changes, architecture overview and project status table **[CPX: 2]**

### Lower Priority

- [ ] API reference documentation
- [ ] Getting started guide
- [ ] Architecture overview

---

## Code Cleanup & Technical Debt

### Code Refactoring

*No Code Refactoring items*

---

## Known Issues / Bugs

### Active Bugs

*No active bugs*

### Uncategorized (Analyze and create TODO entries in above appropriate sections with priority. Do not fix or implement them just yet. Assign complexity points where applicable. Do not delete this section when you are done, just empty it)

- Make smoke particles have the same color as light on their current block position (requires sampling light level at particle position)

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

See [DONE.md](DONE.md) for completed items.
