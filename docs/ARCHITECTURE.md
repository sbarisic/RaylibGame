# Architecture and migration invariants

## Target boundary

| Project | Target | Responsibilities | Forbidden dependencies |
|---|---|---|---|
| `VoxelgineEngine` | `net10.0` | Shared contracts, math, input, networking, and backend-neutral types | FishGfx, FishUI, Raylib, GLFW, audio-native code |
| `VoxelgineServer` | `net10.0` | Authoritative simulation and dedicated-server host | Client assembly and every graphics/audio runtime |
| `Voxelgine` | `net10.0-windows10.0.17763.0`, x64 final target | Window, input, rendering, UI, assets, and client presentation | Server-only ownership |
| `Voxelgine.Audio` | Windows x64 | Managed audio policy and native miniaudio interop | Gameplay and renderer types |

Backend-neutral simulation, entity, player, world, AI, and dedicated-server
logic lives in `VoxelgineEngine`. Both client and server consume those compiled
definitions through a project reference; neither project links source from the
other. The server publish audit rejects the client assembly, FishGfx, FishUI,
GLFW, legacy renderer binaries, VoxelAudioNative, and other audio/graphics
dependencies.

Shared code communicates presentation data with `System.Numerics`, `AABB`,
`Rgba32`, `GameCameraState`, `FrameTiming`, `PhysicalKey`, and
`PhysicalMouseButton`. It emits `GameAudioEvent` through `IGameAudioSink`.
Domain entities retain asset identifiers, transforms, tint, and animation
state; GPU resources are owned by client render components.

## Compatibility invariants

The renderer migration must not change:

- save bytes or world serialization semantics;
- packet bytes, prediction rules, or local-echo suppression;
- numeric `BlockType` values;
- authoritative collision, fixed-step timing, or simulation behavior; or
- existing input bindings when legacy configuration names are loaded.

The client may establish new screenshot baselines after visual review. Exact
Raylib pixel parity is not an invariant.

## World ownership

`ChunkMap` is the only authority for block storage, generation, saves,
networking, collision, and authoritative mutations. Its observation surface is
deliberately backend-neutral:

```text
ChunkMap mutation
  |- BlockChanged: one event for one authoritative edit
  `- WorldReset: one event after bulk generation or deserialization
          |
          v
FishGfxVoxelScene (client only)
  |- immutable 4096-cell ChunkSnapshot copies
  |- explicit BlockType-to-palette mapping
  |- VoxelWorld rendering mirror
  |- VoxelLighting
  `- VoxelRenderer
```

The mirror is rebuilt with `SetChunk` and incrementally updated with `SetVoxel`.
It cannot write saves or packets. Renderer state never enters `Chunk`,
`ChunkSnapshot`, or a domain entity.

## Client frame and render ownership

The Windows host performs work in this order:

```text
begin input frame -> poll window events -> apply queued asset reloads
-> variable/fixed updates -> synchronize voxel/lighting/mesh work
-> begin RenderFrame -> execute render graph -> present
-> optional limiter when VSync is disabled
```

The render graph owns one RGBA8/depth-stencil scene target. It renders sky,
opaque geometry, transparent geometry, clears depth while preserving color for
the viewmodel, resolves 4x MSAA when enabled, applies the subtle scene
post-process, then draws crisp FishUI and overlays to the backbuffer. FXAA is
used only when MSAA is disabled.

`GameAssetStore` owns textures, shaders, meshes, models, fonts, and retained CPU
model data. File watchers only enqueue changes. Compilation, GPU upload,
atomic replacement, and disposal happen at frame start on the graphics thread;
a failed reload leaves the last valid resource live.

## Lifetime and thread rules

- FishGfx objects are created, replaced, and destroyed on the graphics thread.
- Game states, voxel rendering, targets, and assets are disposed before the
  window/context closes.
- Audio completion and diagnostics are polled by the game thread; the mixer
  never calls managed code.
- The audio device and cue bank are process-scoped. World ambience and voices
  are stopped or faded on state changes.
- Raylib compatibility code is not permitted in production. The final scan must
  find no `Raylib_cs`,
  `Raylib.`, or `Rlgl.` references outside historical documentation and the
  FishGfx test asset directory named `raylibgame`.
