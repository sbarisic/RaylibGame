# Models, shaders, and asset conversion

## Supported model formats

The active runtime model policy is intentionally narrow:

| Format | Status | Use |
|---|---|---|
| Blockbench/Minecraft JSON | Supported | Hierarchical game models, animation transforms, collision triangles |
| OBJ/MTL | Supported | Static models, including the XP orb |
| FBX | Stored, dormant | Convert offline before reactivation |
| IQM | Stored, dormant | Convert offline or add a reviewed future skeletal format |
| `.bbmodel` | Authoring source | Export to runtime JSON/OBJ; do not parse at runtime |

Do not add an FBX or IQM loader merely because source content remains in the
tree. A dormant asset must be converted to JSON, OBJ, or a future explicitly
supported skeletal format before gameplay references it.

For JSON conversion, preserve the Blockbench hierarchy, pivots, rotations,
per-part transforms, texture UVs, and existing game scale. Retain transformed
CPU triangles alongside the FishGfx `Mesh3D`; rendering, collision, picking, and
animation bounds must use the same hierarchy transform. Ray queries use the
tested Moller-Trumbore triangle intersection implementation.

For OBJ conversion, preserve material names, relative MTL/texture references,
normals, UVs, scale, and Y-up orientation. Open the exported file through the
FishGfx OBJ loader before removing an older runtime representation.

FishGfx's `data/textures/voxels/raylibgame` mapping is reference and test data.
It is not a runtime dependency. RaylibGame owns the atlas, model, and palette
assets used by the client.

Cube world voxels use four matching 512x512 atlases: `atlas.png`,
`atlas_normal.png`, `atlas_specular.png`, and `atlas_roughness.png`. The material
maps are linear data, replace atomically on hot reload, and use OpenGL +Y tangent
space. Custom voxel models, items, entities, characters, and the viewmodel keep
their existing shaders until they receive authored tangent/material data.

## Shader contract

Game shaders are GLSL 400 and use FishGfx conventions such as `uModel`,
`uView`, `uProjection`, `uTexture`, `uTime`, and `uResolution`. Scene
post-processing must not synthesize `gl_FragDepth`. FXAA is enabled only when
MSAA is disabled.

Shader and texture file watchers enqueue reload requests only. Compile, upload,
swap, and disposal happen on the graphics thread at frame start. A failed
compile must retain the last valid asset.

## Audio asset policy

Short gameplay effects stay as WAV. Long ambience is streamed from lossless
FLAC. The client project uses recursive WAV/FLAC content rules so adding a file
does not require a hand-written project entry, but a sound is playable only
after its cue is defined in `data/audio/audio-bank.json`.

For a WAV-to-FLAC replacement, record and compare:

- sample rate and channel count;
- exact sample/frame count and duration;
- decoded PCM checksum; and
- loop start/end behavior.

Commit the verified FLAC before removing the PCM WAV. Positional stereo cues
are accepted only when the diagnostic downmix to mono is intentional.

## Visual acceptance

FishGfx output is reviewed for gameplay readability and broad art direction,
not exact Raylib pixels. Establish new deterministic baselines for daylight,
night, water/glass, underwater, emissive blocks, NPCs, viewmodels, particles,
speech overlays, menus, and inventory.

At minimum, an automated render must observe non-background pixels, settled
voxel meshing, and clean resource shutdown. Manual review also covers resize,
high DPI, all window modes, monitor changes, VSync, UI input, shader reload, and
repeated game-state switches.
