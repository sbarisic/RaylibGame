[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/sbarisic/RaylibGame)

# Aurora Falls - Voxelgine Engine

**Aurora Falls** is a voxel-based sandbox engine and game written in modern **C# (.NET 9)**, featuring real-time 3D rendering with **Raylib-cs**, a modular entity system, and a fully interactive world.

Players can explore, build, and modify a procedurally generated floating island environment, interact with blocks and entities, and use a variety of tools and weapons.

![Screenshot 1](img/39Kudu88Xu.png)

![Screenshot 2](img/5LoczCbPHp.png)

---

## Features

- **Procedural World Generation** — Floating islands generated via simplex noise with grass, dirt, stone, sand, and water
- **Block System** — Place, destroy, and interact with 20+ block types including transparent blocks (water, glass, ice)
- **Dual-Channel Lighting** — Separate skylight and block light propagation with real-time updates
- **Quake-Style Physics** — Strafe-jumping, bunny-hopping, air control, water swimming with buoyancy
- **Entity System** — Base entity class with pickup items, NPCs, and interactive doors
- **Particle System** — Smoke and visual effects with depth-sorted rendering
- **FishUI-Based GUI** — Custom inventory, item boxes, and in-game menus
- **Save/Load System** — GZip-compressed world and player state persistence
- **Hot-Reload Shaders** — Edit shaders at runtime for rapid iteration
- **Frame Interpolation** — Smooth camera and position rendering independent of physics tick rate

---

## Architecture

### Solution Structure

```
RaylibGame.sln
├── Voxelgine/              # Main game/engine project
│   ├── Engine/             # Core systems
│   ├── Graphics/           # Rendering and chunk management
│   ├── GUI/                # FishUI integration
│   ├── States/             # Game states (menu, gameplay)
│   └── data/               # Assets (textures, models, sounds, shaders)
└── UnitTest/               # Unit tests for core systems
```

### Core Systems

| System | Files | Description |
|--------|-------|-------------|
| **Program** | `Program.cs` | Entry point, game loop with fixed timestep physics |
| **GameWindow** | `GameWindow.cs` | Window management, render targets, state switching |
| **GameState** | `States/GameState.cs` | Main gameplay state, world/player/entity management |
| **InputMgr** | `InputMgr.cs` | Keyboard/mouse input abstraction |
| **SoundMgr** | `SoundMgr.cs` | Positional audio, sound combos (randomized effects) |
| **ResMgr** | `ResMgr.cs` | Resource loading (textures, models, shaders) with hot-reload |
| **GameConfig** | `GameConfig.cs` | JSON-based configuration (resolution, vsync, sensitivity) |

### Graphics Pipeline

| Component | Files | Description |
|-----------|-------|-------------|
| **ChunkMap** | `Graphics/ChunkMap.cs` | Spatial hash grid of chunks, world queries, lighting computation |
| **Chunk** | `Graphics/Chunk.cs` | 16³ block storage, mesh generation, transparent face caching |
| **PlacedBlock** | `Graphics/Chunk/PlacedBlock.cs` | Block type + dual light values (skylight/blocklight) |
| **BlockLayout** | `Graphics/Chunk/BlockLayout.cs` | Face visibility and UV calculation |
| **GBuffer** | `Graphics/GBuffer.cs` | Deferred rendering targets |
| **Frustum** | `Graphics/Frustum.cs` | View frustum culling |
| **Skybox** | `Engine/Skybox.cs` | Procedural sky rendering |

### Entity System

| Component | Files | Description |
|-----------|-------|-------------|
| **VoxEntity** | `Engine/Entities/VoxEntity.cs` | Base class for all entities (position, velocity, model) |
| **EntityManager** | `Engine/Entities/EntityManager.cs` | Entity spawning, physics, player collision |
| **VEntPickup** | `Engine/Entities/VEntPickup.cs` | Collectible items with rotation animation |
| **VEntNPC** | `Engine/Entities/VEntNPC.cs` | NPC entities with JSON model support |
| **VEntSlidingDoor** | `Engine/Entities/VEntSlidingDoor.cs` | Interactive animated doors |

### Player & Physics

| Component | Files | Description |
|-----------|-------|-------------|
| **Player** | `Engine/Player.cs` | Player state, input handling, physics, inventory |
| **FPSCamera** | `Engine/FPSCamera.cs` | First-person camera with mouse look |
| **ViewModel** | `Engine/ViewModel.cs` | First-person weapon/tool rendering |
| **PhysData** | `Engine/Physics/PhysData.cs` | Physics constants (gravity, friction, speeds) |
| **PhysicsUtils** | `Engine/Physics/PhysicsUtils.cs` | Shared physics: ClipVelocity, collision, acceleration |
| **AABB** | `Engine/Physics/AABB.cs` | Axis-aligned bounding box for collision |

### GUI System

| Component | Files | Description |
|-----------|-------|-------------|
| **FishUIManager** | `GUI/FishUI/FishUIManager.cs` | Main UI manager wrapping FishUI library |
| **RaylibFishUIGfx** | `GUI/FishUI/RaylibFishUIGfx.cs` | Raylib graphics backend for FishUI |
| **RaylibFishUIInput** | `GUI/FishUI/RaylibFishUIInput.cs` | Raylib input backend for FishUI |
| **FishUIItemBox** | `GUI/FishUI/Controls/FishUIItemBox.cs` | Inventory slot with icon rendering |
| **FishUIInventory** | `GUI/FishUI/Controls/FishUIInventory.cs` | Hotbar/inventory display |
| **FishUIInfoLabel** | `GUI/FishUI/Controls/FishUIInfoLabel.cs` | Debug/info text overlay |

### Animation & Effects

| Component | Files | Description |
|-----------|-------|-------------|
| **AnimLerp** | `Engine/Animations/AnimLerp.cs` | Interpolation with 30+ easing functions |
| **LerpManager** | `Engine/Animations/LerpManager.cs` | Global animation instance management |
| **ParticleSystem** | `Engine/ParticleSystem.cs` | Billboard particles with depth sorting |

### Weapons & Items

| Component | Files | Description |
|-----------|-------|-------------|
| **InventoryItem** | `Engine/Weapons/InventoryItem.cs` | Base item with block placement logic |
| **Weapon** | `Engine/Weapons/Weapon.cs` | Base weapon class |
| **WeaponGun** | `Engine/Weapons/WeaponGun.cs` | Firearm implementation |
| **WeaponPicker** | `Engine/Weapons/WeaponPicker.cs` | Block picker tool |

---

## Controls

| Key | Action |
|-----|--------|
| **WASD** | Move |
| **Mouse** | Look around |
| **Space** | Jump / Swim up |
| **Shift** | Walk (slow) / Swim down / Ledge safety |
| **C** | Toggle noclip mode |
| **1-4** | Select hotbar slot |
| **Left Click** | Use item / Break block |
| **Right Click** | Place block |
| **F1** | Toggle debug menu |
| **F3** | Toggle debug mode |
| **F5** | Quick save |
| **Esc** | Return to main menu |

---

## Building

### Requirements

- .NET 9 SDK
- Visual Studio 2022+ or VS Code with C# extension

### Build & Run

```bash
# Clone the repository
git clone https://github.com/sbarisic/RaylibGame.git
cd RaylibGame

# Build and run
dotnet run --project Voxelgine
```

### Run Tests

```bash
dotnet test
```

---

## Project Status

| System | Status | Notes |
|--------|--------|-------|
| Core Engine | ✅ Complete | Window, input, audio, resources |
| Graphics | ✅ Complete | Chunks, lighting, frustum culling |
| Voxel World | ✅ Complete | Generation, block types, dual lighting |
| Player | ✅ Complete | Movement, physics, inventory |
| Physics | ✅ Complete | Quake-style with water buoyancy |
| GUI | ✅ Complete | FishUI-based menus and HUD |
| Entity System | 🔶 Partial | Base entities work, AI pending |
| Particles | 🔶 Partial | Smoke effects, more types planned |
| Animation | 🔶 Partial | Lerp system complete, NPC anims pending |
| NPC/AI | ⬜ Planned | Entity exists, no behavior/pathfinding |
| Scripting | ⬜ Planned | Stub exists |
| Mod System | ⬜ Planned | Not started |

---

## License

This project is for educational and experimental purposes.

---

## Screenshots

![Screenshot 3](img/m2n3Uh6ucn.png)

![Screenshot 4](img/r2g4zhselE.png)
