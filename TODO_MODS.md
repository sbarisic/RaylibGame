# Aurora Falls - Mod System TODO

Planned tasks for implementing the mod system API.

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
> - Core Infrastructure tasks should be completed before API Layers; API Layers before Scripting; etc.

---

## Design Overview

### Architecture Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| **Primary mod type** | C# plugin assemblies | Full type safety, IDE support, access to all .NET 9 features |
| **Secondary mod type** | Lua scripts via MoonSharp | Already partially integrated (`Scripting.cs`, `AnimatedEntity` has `[MoonSharpUserData]`), good for simple/quick mods |
| **Plugin loading** | `AssemblyLoadContext` | .NET 9 supports safe assembly loading/unloading, mod isolation |
| **Mod discovery** | `mods/` directory with JSON manifests | Simple convention-based discovery, each mod in its own subfolder |
| **API exposure** | Interface-based mod API (`IModAPI`) | Mods receive a single API entry point exposing all subsystems; decouples mod code from engine internals |
| **Event system** | Publish/subscribe event bus | Mods subscribe to game events (block changes, entity spawns, player actions, tick); avoids tight coupling |
| **Mod lifecycle** | Init → Load → Enable → Tick → Disable → Unload | Clear lifecycle with hooks at each stage |
| **Service integration** | Extend `FishDI` for mod service registration | Mods can register/resolve services using existing DI infrastructure |

### Systems to Expose

| System | Engine Class(es) | Mod API Surface |
|--------|-------------------|-----------------|
| **World** | `ChunkMap`, `Chunk`, `BlockInfo`, `BlockType` | Get/set blocks, query chunks, world generation hooks, lighting |
| **Entities** | `EntityManager`, `VoxEntity`, `VEntPickup`, `VEntNPC`, `VEntSlidingDoor` | Spawn/remove entities, register custom entity types, query entities, raycasting |
| **Player** | `Player`, `FPSCamera` | Position, velocity, camera, inventory access |
| **Items/Weapons** | `InventoryItem`, `Weapon`, `WeaponGun`, `WeaponPicker` | Register custom items/weapons, inventory manipulation |
| **Particles** | `ParticleSystem` | Spawn smoke/fire/blood/custom particles, register particle types |
| **Sound** | `SoundMgr` | Play sounds, register sound combos |
| **Resources** | `ResMgr` | Register textures, models, shaders; texture collections |
| **Input** | `InputMgr`, `InputKey` | Query input state, register custom key bindings |
| **GUI** | `FishUIManager`, FishUI controls | Create custom GUI windows/controls |
| **Day/Night** | `DayNightCycle` | Query/set time, sky properties |
| **Config** | `GameConfig` | Per-mod configuration load/save |
| **Logging** | `IFishLogging` | Per-mod prefixed logging |
| **Pathfinding** | `VoxelPathfinder`, `PathFollower` | Path queries for custom entities |

### Mod Folder Structure

```
mods/
├── my_mod/
│   ├── mod.json          # Mod manifest (name, version, author, entry point, dependencies)
│   ├── MyMod.dll         # C# mod assembly (or)
│   ├── main.lua          # Lua script entry point
│   └── data/             # Mod-specific assets (textures, models, sounds)
│       ├── textures/
│       ├── models/
│       └── sounds/
```

### Mod Manifest Format (`mod.json`)

```json
{
  "id": "my_mod",
  "name": "My Mod",
  "version": "1.0.0",
  "author": "Author Name",
  "description": "A sample mod",
  "entryPoint": "MyMod.dll",
  "modClass": "MyMod.MyModMain",
  "dependencies": [],
  "gameVersion": "0.1.0"
}
```

---

## Core Infrastructure

### High Priority

- [ ] **Event Bus system** — Create a publish/subscribe event bus (`GameEventBus`) that engine systems fire events into and mods can subscribe to. Events: `OnBlockPlaced`, `OnBlockRemoved`, `OnEntitySpawned`, `OnEntityRemoved`, `OnPlayerDamaged`, `OnPlayerMoved`, `OnTick`, `OnLockstepTick`, `OnGameStateChanged`, `OnWorldGenerated`, `OnWorldLoaded`, `OnWorldSaved`. Each event carries relevant data (position, block type, entity ref, etc.). The event bus should be a DI singleton. Engine code must be updated to fire events at appropriate points (`ChunkMap.SetPlacedBlock`, `EntityManager.Spawn`, `GameState`, `Player`, etc.) **[CPX: 5]**

- [ ] **Mod API interface** — Define `IMod` interface and `ModBase` abstract class that mods implement. `IMod` defines lifecycle methods: `OnInit(IModAPI api)`, `OnEnable()`, `OnDisable()`, `OnTick(float dt)`. `IModAPI` is the single entry point mods use to access all subsystems (world, entities, player, etc.) — mods never reference engine internals directly. Create `IModAPI` interface with properties for each sub-API (`IWorldAPI`, `IEntityAPI`, `IPlayerAPI`, etc.). Place in a new `Voxelgine/Engine/Modding/` directory **[CPX: 3]**

- [ ] **Mod Loader** — Create `ModLoader` class that discovers mod folders in `mods/`, reads `mod.json` manifests, validates dependencies, and loads C# assemblies via `AssemblyLoadContext`. Handle dependency ordering (topological sort). Report errors via `IFishLogging`. Register as DI singleton. Integrate into `Program.cs` startup after resource initialization but before game state creation **[CPX: 4]**

- [ ] **Mod Lifecycle Manager** — Create `ModManager` that manages the lifecycle of loaded mods: calls `OnInit` → `OnEnable` during startup, `OnTick` each frame, `OnDisable` → unload during shutdown. Handle mod enable/disable at runtime. Track mod state (Loaded/Enabled/Disabled/Error). Integrate with `GameWindow` tick loop **[CPX: 3]**

### Medium Priority

- [ ] **Mod manifest parsing** — Create `ModManifest` class and JSON deserialization for `mod.json` files. Validate required fields (id, name, version, entryPoint). Support optional fields (dependencies, description, author, gameVersion). Use Newtonsoft.Json (already in project) **[CPX: 1]**

- [ ] **Mod sandboxing and error handling** — Wrap all mod API calls in try/catch to prevent mod crashes from taking down the engine. Log mod errors with mod ID prefix. Consider `AssemblyLoadContext` isolation for mod assemblies to prevent type conflicts **[CPX: 3]**

---

## API Layers

### High Priority

- [ ] **World API** (`IWorldAPI`) — Expose: `GetBlock(x,y,z)`, `SetBlock(x,y,z,BlockType)`, `GetLightLevel(pos)`, `IsWaterAt(pos)`, `GetChunk(index)`, `ComputeLighting()`, `RegisterBlockType(name, properties)` (for custom blocks — requires extending `BlockType` enum to support dynamic IDs). Fire `OnBlockPlaced`/`OnBlockRemoved` events. Backed by `ChunkMap` **[CPX: 4]**

- [ ] **Entity API** (`IEntityAPI`) — Expose: `SpawnEntity(type, position)`, `RemoveEntity(entity)`, `GetAllEntities()`, `GetEntitiesInRadius(pos, radius)`, `RegisterEntityType<T>(name)` (for custom entity types extending `VoxEntity`), `Raycast(origin, dir, maxDist)`. Fire `OnEntitySpawned`/`OnEntityRemoved` events. Backed by `EntityManager` **[CPX: 3]**

- [ ] **Player API** (`IPlayerAPI`) — Expose: `GetPosition()`, `SetPosition(pos)`, `GetVelocity()`, `GetForward()`, `GetCameraAngle()`, `IsNoClip`, `AddInventoryItem(item)`, `GetInventory()`, `PlaySound(combo, pos)`. Read-only access to player state; write access only for position/velocity/inventory. Backed by `Player` **[CPX: 2]**

### Medium Priority

- [ ] **Resource API** (`IResourceAPI`) — Expose: `RegisterTexture(name, path)`, `RegisterModel(name, path)`, `RegisterShader(name, vertPath, fragPath)`, `RegisterSoundCombo(name, pathPattern, count, volume)`, `GetTexture(name)`, `GetModel(name)`. Mod resource paths are relative to the mod's `data/` folder. Backed by `ResMgr` and `SoundMgr` **[CPX: 3]**

- [ ] **Item/Weapon API** (`IItemAPI`) — Expose: `RegisterItem(name, properties)`, `RegisterWeapon(name, properties)`, custom `OnLeftClick`/`OnRightClick`/`OnSelected`/`OnDeselected` callbacks. Allow mods to create new `InventoryItem` subclasses or use a data-driven item definition. Backed by `InventoryItem`, `Weapon` **[CPX: 3]**

- [ ] **Particle API** (`IParticleAPI`) — Expose: `SpawnSmoke(pos, vel, color)`, `SpawnFire(pos, force, color, scale)`, `SpawnBlood(pos, normal, scale)`, `SpawnTracer(start, end, color)`. Future: `RegisterParticleType(name, properties)` for custom particle types. Backed by `ParticleSystem` **[CPX: 2]**

- [ ] **Sound API** (`ISoundAPI`) — Expose: `PlayCombo(name, listenerPos, listenerDir, soundPos)`, `RegisterCombo(name, pathPattern, count, volume)`, `SetMasterVolume(vol)`. Backed by `SoundMgr` **[CPX: 2]**

- [ ] **GUI API** (`IGuiAPI`) — Expose: `CreateWindow(title, pos, size)`, `CreateButton(text, pos, size)`, `CreateLabel(text, pos, size)`, `CreateCheckbox(pos, size)`, `AddToScreen(control)`, `RemoveFromScreen(control)`. Allow mods to create FishUI controls and add them to the game GUI. Backed by `FishUIManager` **[CPX: 3]**

- [ ] **Input API** (`IInputAPI`) — Expose: `IsKeyDown(key)`, `IsKeyPressed(key)`, `GetMousePosition()`, `GetMouseDelta()`, `RegisterAction(name, defaultKey)` for custom input actions that can be rebound. Backed by `InputMgr` **[CPX: 2]**

### Lower Priority

- [ ] **Day/Night API** (`IDayNightAPI`) — Expose: `GetTimeOfDay()`, `SetTimeOfDay(hours)`, `GetSkyColor()`, `GetSkyLightMultiplier()`, `IsPaused`, `TimeScale`. Backed by `DayNightCycle` **[CPX: 1]**

- [ ] **Config API** (`IConfigAPI`) — Expose: `GetModConfig<T>(modId)`, `SaveModConfig(modId, config)`. Per-mod JSON config files in `mods/<modId>/config.json`. Use Newtonsoft.Json for serialization **[CPX: 2]**

- [ ] **Pathfinding API** (`IPathfindingAPI`) — Expose: `FindPath(start, end, entitySize)`, `CreatePathFollower(entity)`. Backed by `VoxelPathfinder`, `PathFollower` **[CPX: 2]**

- [ ] **Logging API** (`IModLogging`) — Per-mod logging with `[ModID]` prefix. Each mod receives its own `IModLogging` instance. Backed by `IFishLogging` **[CPX: 1]**

---

## Lua Scripting

### Medium Priority

- [ ] **Expand Lua scripting integration** — Extend `Scripting.cs` to expose all mod API interfaces to MoonSharp Lua scripts. Register API wrapper types with `[MoonSharpUserData]`. Lua mods use same `mod.json` manifest but with `"entryPoint": "main.lua"`. Create Lua-friendly wrapper functions for common operations. Build on existing `AnimatedEntity` MoonSharp pattern **[CPX: 4]**

- [ ] **Lua mod lifecycle** — Implement Lua mod loading: read `main.lua`, execute in a sandboxed `Script` context, call conventional functions (`mod_init()`, `mod_enable()`, `mod_tick(dt)`, `mod_disable()`). Each Lua mod gets its own `Script` instance **[CPX: 3]**

### Lower Priority

- [ ] **Lua hot-reload** — Support reloading Lua scripts at runtime without restarting the game. Monitor `.lua` files with `FileSystemWatcher` (similar to existing shader hot-reload in `ResMgr`). Re-execute script and call `mod_init()` on reload **[CPX: 3]**

---

## UI & Tooling

### Medium Priority

- [ ] **In-game mod list** — Add a "Mods" button to the main menu that opens a FishUI window listing all discovered mods (name, version, author, status). Allow enabling/disabling mods. Show mod load errors **[CPX: 3]**

### Lower Priority

- [ ] **In-game mod console** — Add a toggleable console window (e.g., tilde key) that shows mod log output and allows executing Lua commands. Use `FishUIManager` for the console GUI. Useful for debugging mods **[CPX: 3]**

- [ ] **Mod template generator** — Command-line tool or script that scaffolds a new mod folder with `mod.json`, starter C# project file or `main.lua`, and `data/` directory **[CPX: 2]**

---

## Documentation

### Medium Priority

- [ ] **Mod API reference** — Document all `IModAPI` interfaces with XML docs and generate a reference page. Include code examples for common operations **[CPX: 2]**

- [ ] **Modding getting started guide** — Step-by-step guide: creating a mod folder, writing `mod.json`, implementing `IMod`, building and loading a C# mod, and writing a Lua mod **[CPX: 2]**

### Lower Priority

- [ ] **Example: Sample mod** — Implement a sample mod demonstrating the API **[CPX: 2]**

- [ ] **Example: Custom block mod** — Demonstrate registering a new block type with custom texture and properties **[CPX: 1]**

- [ ] **Example: Custom entity mod** — Demonstrate spawning a custom entity type with behavior **[CPX: 2]**

- [ ] **Example: Lua script mod** — Demonstrate a Lua mod that spawns particles on block break **[CPX: 1]**

---

## Known Issues / Bugs

### Active Bugs

*No active bugs*

### Uncategorized

- Restructure TODO_MODS.md, the plan is to have C# only mods. Multiplayer is planned too, so plan for that.

---

## Notes

- The existing `Scripting.cs` already calls `UserData.RegisterAssembly()` and exposes `AnimatedEntity` — build on this foundation
- `BlockType` is currently a `ushort` enum — supporting dynamic/mod-defined block types will require an ID registry system mapping names to runtime IDs
- Static classes (`ResMgr`, `GraphicsUtils`, `CustomModel`) need wrapper APIs since mods shouldn't access engine statics directly
- The event bus is the foundation — most API layers depend on it for notifying mods about game events
- Maintain the "dependency-free" philosophy for the mod API — mods should only reference the API assembly, not engine internals
- Mod API interfaces should be in a separate assembly/namespace (`Voxelgine.ModAPI`) so mod authors can reference it independently
- Consider publishing the mod API as a NuGet package in the future for easy mod development

---

## Suggested Implementation Order

1. **Event Bus** → foundation for all mod hooks
2. **Mod API interface** (`IMod`, `IModAPI`) → defines the contract
3. **Mod Manifest parsing** → needed by loader
4. **Mod Loader** → discovers and loads mods
5. **Mod Lifecycle Manager** → manages mod state
6. **World API** → most fundamental game system
7. **Entity API** → second most important
8. **Player API** → mods need player interaction
9. **Resource API** → mods need to load assets
10. **Remaining API layers** (items, particles, sound, GUI, input, day/night, config, pathfinding, logging)
11. **Lua scripting expansion** → builds on C# API
12. **UI & Tooling** → quality of life
13. **Sandboxing** → hardening
14. **Documentation & Examples** → last but important

---

## Completed

*No completed items yet.*
