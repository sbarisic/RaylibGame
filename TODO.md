# Aurora Falls - outstanding work

The client uses FishGfx and FishUI on Windows x64. Portable planning lives in
`Voxelgine.WorldGeneration`; authoritative simulation lives in `VoxelgineEngine`.
The dedicated server has no graphics or audio dependency. Current ownership and
contracts are documented in [Architecture](docs/ARCHITECTURE.md) and
[Multiplayer](MULTIPLAYER.md). Completed records are in [DONE.md](DONE.md).

## Known issues to investigate

- Complete the full camera-path frame-time and manual texture/lighting/shadow/fog
  acceptance matrix before enabling greedy meshing by default. Profile the
  feature-rich village planner: selected seeds still have large allocation and
  time costs. Revisit render-queue storage reuse only with a faster measured design.
  See [Voxel performance](docs/VOXEL_PERFORMANCE.md).

- NPC chat response: saying "hello" reportedly does not produce the expected
  reaction. Keep this open until reproduced and verified through gameplay.

## Correctness and operational follow-up

- Review persistent identity recovery UX and expose key/trust management in the
  UI. Local keys, manual backups, and explicit command-line trust reset are the
  current implementation; there is no account service or legacy-profile import.
- Add sustained WAN loss/jitter, adversarial load, multi-client long-running soak,
  and default-size world performance captures beyond focused regression tests.
- Measure and design background saving with explicit snapshot ownership. Saves
  remain synchronous and report duration; do not introduce unsynchronized world
  access to remove a measured stall.
- Continue default-size generation memory and broad seed-quality acceptance.

## Presentation and gameplay

- Add furniture models and sleep animations; preserve the implemented lifecycle
  and bed-reservation rules while improving presentation.
- Water flow: move water into available lower/adjacent cells without multiplying
  it. Define deterministic server behavior and budgets before implementation.
- LAN server discovery and browser.
- Configurable key bindings.
- Disabled inventory-item presentation.
- Broader progression and world content: [Worldbuilding](WORLDBUILDING.md).
- Mod system and supported APIs: [Mod proposal](TODO_MODS.md).

## Documentation and acceptance

- Expand the API reference and newcomer tutorial as stable public interfaces form.
- Complete manual visual, DPI/window-mode, audio-listening, monitor-change, and
  shader-reload acceptance where an automated smoke is insufficient.
- Keep current test evidence in [Validation](docs/VALIDATION.md); historical test
  counts and migration accomplishments are not current acceptance claims.

## Uncategorized

New reports belong here until reproduced and prioritized. Move verified completed
work to DONE.md; retain unresolved behavior and unavailable acceptance gates.

## Maintenance constraints

Keep the core dependency surface small. Preserve unrelated local work, numeric
block IDs, the 0.015-second physics delta, world ownership, and existing movement.
Prefer responsibility-based files below 1,000 lines. Dependency documentation
belongs to its owning submodule and is not part of first-party documentation edits.
