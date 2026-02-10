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

**Aurora Falls** is a voxel-based 3D game engine built with **Raylib-cs** targeting **.NET 10**.

Three-project architecture: `Voxelgine` (client + Raylib), `VoxelgineEngine` (shared/Raylib-free), `VoxelgineServer` (dedicated headless server).

### Current Architecture

| System | Status | Description |
|--------|--------|-------------|
| **Core Engine** | ✅ | `GameWindow`, `GameState`, `GameSimulation`, `GameConfig`, `InputMgr`, `SoundMgr`, `ResMgr`, `FishDI`, `FishLogging` |
| **Graphics** | ✅ | `ChunkMap`, `Chunk`, GBuffer deferred rendering, `Skybox`, `Frustum` culling |
| **Voxel World** | ✅ | Procedural island generation via simplex noise, block types, dual-channel lighting (skylight/block light) |
| **Entity System** | ✅ | `VoxEntity` base, `VEntPickup`, `VEntNPC`, `VEntSlidingDoor`, `EntityManager` with network IDs, authority flag, spawn properties |
| **Player** | ✅ | `Player`, `FPSCamera` (instance-based), `PlayerManager`, `RemotePlayer`, `ViewModel`, inventory, health/respawn |
| **Weapons** | ✅ | `Weapon`, `WeaponGun` (fire intent/resolve/effects separation), `WeaponPicker`, `InventoryItem` |
| **GUI** | ✅ | FishUI-based: `FishUIManager`, `RaylibFishUIGfx`, custom controls, main menu with connect/host dialogs |
| **Particles** | 🔶 | `ParticleSystem` with smoke effects |
| **Animation** | ✅ | `AnimLerp`, `LerpManager`, easing functions, `NPCAnimator` |
| **Physics** | ✅ | `AABB`, `PhysData`, `PhysicsUtils` + `WorldCollision` + `RayMath` (split across Engine/Voxelgine) |
| **Multiplayer** | ✅ | Client-server authoritative, UDP transport, reliable delivery, client prediction with predicted fire effects, remote player interpolation, entity/block/combat sync, listen server mode — see [MULTIPLAYER.md](MULTIPLAYER.md) for protocol reference |
| **NPC/AI** | ⬜ | Basic `VEntNPC` with pathfinding, no behavior trees |
| **Scripting** | ⬜ | `Scripting.cs` exists (empty/stub) |
| **Mod System** | ⬜ | Not implemented — see [TODO_MODS.md](TODO_MODS.md) |

Legend: ✅ Functional | 🔶 Partial/WIP | ⬜ Planned

---

## Features

### High Priority

*No high priority items — Mod System tracked in [TODO_MODS.md](TODO_MODS.md)*

### Medium Priority

- [ ] **NPC AI System** — Complete NPC entities with AI goals system and behavior trees for `VEntNPC`
	- [ ] Maybe it should be implemented as a behavior tree VM? Like i can give it a list of commands, MOVE_RANDOM_POINT, IS_PLAYER_AROUND, LOOK_AT_PLAYER, ATTACK, ...
	- [ ] NPC should just implement functions that will, for example for MOVE_RANDOM_POINT pick a random point on the map and then pathfind to it. VM "step" of this "instruction" should complete and it should move to next instruction when player is finished walking
	- [ ] Maybe add AI instructions that will do nothing if last one succeeded, and do something else if it failed, for example if MOVE_RANDOM_POINT fails (maybe npc is stuck) then it will try to jump or something, or jump to a separate instruction location (like a jmp instruction) for complex logic

### On Hold

- [ ] **World: Water flow physics** — Water blocks flow to lower adjacent positions if free; on flat surfaces move in a random direction if a neighbor is free (move the block, don't spawn new ones) **[CPX: 4]**
- [ ] **Multiplayer: LAN server browser** — Broadcast UDP discovery on LAN, servers respond with name/player count/map info, client displays list to select and connect **[CPX: 3]**
- [ ] **Input: Key rebinding system** — Add input mapping/rebinding support to `InputMgr` for customizable controls **[CPX: 3]**

---

## Improvements

### High Priority

*No high priority items*

### Medium Priority

*No medium priority items*

### On Hold

- [ ] **GUI: Disabled inventory items** — Draw inventory items as visually disabled when count reaches 0 **[CPX: 1]**

---

## Documentation

### High Priority

*No high priority items*

### Medium Priority

*No medium priority items*

### Lower Priority

- [ ] API reference documentation
- [ ] Getting started guide
- [ ] Architecture overview
- [ ] Multiplayer architecture document — technical description of client-server model, prediction/reconciliation, interpolation, and sync strategies
- [ ] Multiplayer hosting guide — instructions for hosting dedicated/listen servers, CLI arguments, port forwarding, configuration
- [ ] Network protocol reference — document all packet types, binary formats, and field descriptions

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
- See MULTIPLAYER.md for multiplayer architecture

---

## Completed

See [DONE.md](DONE.md) for completed items.
