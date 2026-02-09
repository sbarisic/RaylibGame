# Aurora Falls - Mod System TODO

Planned tasks for implementing the mod system API.

> **CPX (Complexity Points)** - 1 to 5 scale:
> - **1** - Single file control/component
> - **2** - Single file control/component with single function change dependencies
> - **3** - Multi-file control/component or single file with multiple dependencies, no architecture changes
> - **4** - Multi-file control/component with multiple dependencies and significant logic, possible minor architecture changes
> - **5** - Large feature spanning multiple components and subsystems, major architecture changes

> Instructions for the TODO list:
- Move all completed TODO items into a separate Completed document (DONE.md) and simplify by consolidating/combining similar ones and shortening the descriptions where possible

> How this TODO should be iterated:
> - First handle the Uncategorized section, if any similar issues already are on the TODO list, increase their priority instead of adding duplicates
> - When Uncategorized section is empty, start by fixing Active Bugs (take one at a time)
> - After Active Bugs, handle the rest of the TODO by priority and complexity (High priority takes precedence, then CPX points) (take one at a time)
> - Core Infrastructure tasks should be completed before API Layers; API Layers before UI & Tooling; etc.

---

## Design Overview

### Architecture Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| **Mod type** | C# plugin assemblies | Full type safety, IDE support, access to all .NET 9 features. Single mod technology — no Lua. |
| **Plugin loading** | `AssemblyLoadContext` | .NET 9 supports safe assembly loading/unloading, mod isolation |
| **Mod discovery** | `mods/` directory with JSON manifests | Simple convention-based discovery, each mod in its own subfolder |
| **API exposure** | Interface-based mod API (`IModAPI`) | Mods receive a single API entry point exposing all subsystems; decouples mod code from engine internals |
| **Event system** | Publish/subscribe event bus | Mods subscribe to game events (block changes, entity spawns, player actions, tick); avoids tight coupling |
| **Mod lifecycle** | Init → Load → Enable → Tick → Disable → Unload | Clear lifecycle with hooks at each stage |
| **Multiplayer execution** | Server-authoritative, client presentation | Gameplay mods (world/entity/player changes) run on the server. Client receives changes via existing sync. Client-side mods handle presentation only (particles, sounds, GUI). Mod manifest declares `side` (server/client/both). |
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
| **Networking** | `ServerLoop`, `MultiplayerGameState` | Register custom packet types, send/receive mod data between server and clients |

### Mod Folder Structure

```
mods/
├── my_mod/
│   ├── mod.json          # Mod manifest (name, version, author, entry point, dependencies)
│   ├── MyMod.dll         # C# mod assembly
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
  "side": "both",
  "dependencies": [],
  "gameVersion": "0.1.0"
}
```

`side` values: `"server"` (gameplay logic, runs on server only), `"client"` (presentation, runs on client only), `"both"` (default, runs on both).

---

## Core Infrastructure

### High Priority

- [ ] **Event Bus system** — Create a publish/subscribe event bus (`GameEventBus`) that engine systems fire events into and mods can subscribe to. Events: `OnBlockPlaced`, `OnBlockRemoved`, `OnEntitySpawned`, `OnEntityRemoved`, `OnPlayerDamaged`, `OnPlayerMoved`, `OnTick`, `OnLockstepTick`, `OnGameStateChanged`, `OnWorldGenerated`, `OnWorldLoaded`, `OnWorldSaved`. Each event carries relevant data (position, block type, entity ref, etc.). The event bus should be a DI singleton. Engine code must be updated to fire events at appropriate points (`ChunkMap.SetPlacedBlock`, `EntityManager.Spawn`, `GameState`, `Player`, etc.) **[CPX: 5]**
- [ ] **Mod API interface** — Define `IMod` interface and `ModBase` abstract class that mods implement. `IMod` defines lifecycle methods: `OnInit(IModAPI api)`, `OnEnable()`, `OnDisable()`, `OnTick(float dt)`. `IModAPI` is the single entry point mods use to access all subsystems (world, entities, player, etc.) — mods never reference engine internals directly. Create `IModAPI` interface with properties for each sub-API (`IWorldAPI`, `IEntityAPI`, `IPlayerAPI`, etc.). Include `ModSide` enum (`Server`/`Client`/`Both`) for multiplayer-aware mod loading. Place in a new `VoxelgineEngine/Engine/Modding/` directory (Raylib-free so server can load mods too) **[CPX: 3]**
- [ ] **Mod Loader** — Create `ModLoader` class that discovers mod folders in `mods/`, reads `mod.json` manifests, validates dependencies, and loads C# assemblies via `AssemblyLoadContext`. Handle dependency ordering (topological sort). Filter mods by `side` field based on whether running as server or client. Report errors via `IFishLogging`. Register as DI singleton. Integrate into `Program.cs` startup after resource initialization but before game state creation. Server loads server+both mods; client loads client+both mods **[CPX: 4]**
- [ ] **Mod Lifecycle Manager** — Create `ModManager` that manages the lifecycle of loaded mods: calls `OnInit` → `OnEnable` during startup, `OnTick` each frame, `OnDisable` → unload during shutdown. Handle mod enable/disable at runtime. Track mod state (Loaded/Enabled/Disabled/Error). Integrate with `GameWindow` tick loop (client) and `ServerLoop` tick (server) **[CPX: 3]**

### Medium Priority

- [ ] **Mod manifest parsing** — Create `ModManifest` class and JSON deserialization for `mod.json` files. Validate required fields (id, name, version, entryPoint). Support optional fields (dependencies, description, author, gameVersion, side). Default `side` to `"both"` if not specified. Use `System.Text.Json` **[CPX: 1]**
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
- [ ] **Config API** (`IConfigAPI`) — Expose: `GetModConfig<T>(modId)`, `SaveModConfig(modId, config)`. Per-mod JSON config files in `mods/<modId>/config.json`. Use `System.Text.Json` for serialization **[CPX: 2]**
- [ ] **Pathfinding API** (`IPathfindingAPI`) — Expose: `FindPath(start, end, entitySize)`, `CreatePathFollower(entity)`. Backed by `VoxelPathfinder`, `PathFollower` **[CPX: 2]**
- [ ] **Logging API** (`IModLogging`) — Per-mod logging with `[ModID]` prefix. Each mod receives its own `IModLogging` instance. Backed by `IFishLogging` **[CPX: 1]**

---

## UI & Tooling

### Medium Priority

- [ ] **In-game mod list** — Add a "Mods" button to the main menu that opens a FishUI window listing all discovered mods (name, version, author, status). Allow enabling/disabling mods. Show mod load errors **[CPX: 3]**

### Lower Priority

- [ ] **In-game mod console** — Add a toggleable console window (e.g., tilde key) that shows mod log output. Use `FishUIManager` for the console GUI. Useful for debugging mods **[CPX: 3]**
- [ ] **Mod template generator** — Command-line tool or script that scaffolds a new mod folder with `mod.json`, starter C# project file, and `data/` directory **[CPX: 2]**

---

## Documentation

### Medium Priority

- [ ] **Mod API reference** — Document all `IModAPI` interfaces with XML docs and generate a reference page. Include code examples for common operations **[CPX: 2]**
- [ ] **Modding getting started guide** — Step-by-step guide: creating a mod folder, writing `mod.json`, implementing `IMod`, building and loading a C# mod. Cover server-side vs client-side mod patterns **[CPX: 2]**

### Lower Priority

- [ ] **Example: Sample mod** — Implement a sample mod demonstrating the API **[CPX: 2]**
- [ ] **Example: Custom block mod** — Demonstrate registering a new block type with custom texture and properties **[CPX: 1]**
- [ ] **Example: Custom entity mod** — Demonstrate spawning a custom entity type with behavior **[CPX: 2]**

---

## Known Issues / Bugs

### Active Bugs

*No active bugs*

### Uncategorized

- Implement basic mod loading infrastructure/system in VoxelgineEngine

---

## Notes

- `BlockType` is currently a `ushort` enum — supporting dynamic/mod-defined block types will require an ID registry system mapping names to runtime IDs
- Static classes (`ResMgr`, `GraphicsUtils`, `CustomModel`) need wrapper APIs since mods shouldn't access engine statics directly
- The event bus is the foundation — most API layers depend on it for notifying mods about game events
- Maintain the "dependency-free" philosophy for the mod API — mods should only reference the API assembly, not engine internals
- Mod API interfaces should be in `VoxelgineEngine` (Raylib-free) so both the client and dedicated server can load mods
- Consider publishing the mod API as a NuGet package in the future for easy mod development
- Multiplayer: gameplay mods (block/entity/player changes) execute on the server; clients see changes via existing sync packets (block changes, entity snapshots, etc.). Client-side mods (particles, GUI, sounds) are presentation only.
- Server and client must have the same set of `"both"` mods to ensure custom block type IDs and entity type registrations match
- The existing `Scripting.cs` (MoonSharp/Lua) is dead code — can be removed when mod system work begins
- Custom packet types for mod-to-mod communication should use the existing reliable/unreliable channels with a reserved packet ID range

---

## Suggested Implementation Order

1. **Event Bus** → foundation for all mod hooks
2. **Mod API interface** (`IMod`, `IModAPI`, `ModSide`) → defines the contract
3. **Mod Manifest parsing** → needed by loader
4. **Mod Loader** → discovers, filters by side, and loads mods
5. **Mod Lifecycle Manager** → manages mod state on both client and server
6. **World API** → most fundamental game system
7. **Entity API** → second most important
8. **Player API** → mods need player interaction
9. **Resource API** → mods need to load assets
10. **Remaining API layers** (items, particles, sound, GUI, input, day/night, config, pathfinding, logging)
11. **Sandboxing** → error isolation and hardening
12. **UI & Tooling** → quality of life
13. **Documentation & Examples** → last but important

---

## Completed

*No completed items yet.*
