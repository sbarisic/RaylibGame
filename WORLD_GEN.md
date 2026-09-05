# World generation

This describes the implemented planning and materialization pipeline. The broader
game direction remains in [WORLDBUILDING.md](WORLDBUILDING.md).

## Ownership and formats

`Voxelgine.WorldGeneration` is a portable planning library. It builds validated
`WorldPlan` data from world-local seeded noise and feature catalogs. The engine's
`WorldPlanMaterializer` converts that plan into authoritative `ChunkMap` columns.
Clients receive server columns; they do not regenerate terrain from the seed.

Current world-plan format: 6. Generator profile: 14. Materializer version: 11.
These are separate contracts from world archive format 6 and network protocol 2.
Canonical plan bundles validate their structure-catalog and CeramicFish definition
hashes before materialization. A seed alone does not replace an existing save.

The default settings are 1024 by 1024, with a terrain height of 64. Settings
validate horizontal dimensions from 1 through 4096 and heights from 16 through
64. Compact tests establish determinism and invariants; they do not establish
shipping-scale memory or visual acceptance for every seed.

## Planning pipeline

[WorldPlanGenerator](Voxelgine.WorldGeneration/WorldPlanGenerator.cs) builds:

1. Terrain heights and island mask using seed-local noise.
2. Natural ponds and lakes, with reserved hydrology cells.
3. Structure sites and routes based on template roles and connectors.
4. Village reservations and optional CeramicFish layouts, recording failed
   layout attempts as plan data.
5. Hills that respect hydrology, structure, road, and village exclusions.
6. Biomes: void, grassland, forest, sand, rocky terrain, and wetland.
7. Tree density and deterministically ranked tree positions with ten-block
   minimum spacing and feature exclusions.
8. Validated plan rasters, sites, routes, villages, catalog hashes, and versions.

World-local `SeededNoise` replaces the old generator's global `Noise.Seed`
dependency. Plan output has its own canonical serialization and preview surfaces.
The world preview and CeramicFish village lab inspect this planning data without
making renderer storage authoritative.

## Materialization and publication

[WorldPlanMaterializer](VoxelgineEngine/Engine/World/WorldPlanMaterializer.cs)
validates the plan and active catalog hashes, then asks
[WorldPlanVoxelBuilder](VoxelgineEngine/Engine/World/WorldPlanVoxelBuilder.cs) to
build staging columns. It generates terrain and hydrology, stamps structures and
villages, connects gate roads, then adds trees and foliage. The builder uses
compact `BlockValue` and fog arrays for materialized chunks. The former estimate
based on eagerly allocating a 28-byte light-bearing cell at every position does
not describe this pipeline.

The destination map is replaced only after the staging build succeeds and the
final cancellation check passes. Generated feature records and timing information
are restored, and lighting is reset for client presentation. Cancellation before
publication leaves the existing destination world intact. The map retains numeric
block IDs and shape state; renderer palettes and lighting caches stay separate.

Structure markers materialize stable doors, NPCs, machines, containers, beds,
and plants through the server's generated-object layer. Generated identities and
tombstones prevent removed generated objects from being recreated during load.
CeramicFish village geometry and interior materialization are implemented; the
complete narrative progression described in the worldbuilding proposal is not.

## Server lifecycle and persistence

Server startup loads a compatible `<runtime-root>/worlds/map.bin` when present.
Its archived seed and spawn metadata win over a newly supplied seed. An
incompatible archive is backed up before regeneration. `--force-regen` explicitly
requests replacement; `--world-plan <directory>` selects a validated saved plan
and cannot be combined with an explicit `--seed`.

If no compatible archive exists, the server generates or loads a plan,
materializes it, finds valid spawn positions, and saves the result. Spawn scanning
looks for exposed grass with headroom, nearby supporting ground, and spacing.
The resulting player, pickup, and NPC positions are archive metadata. Persistent
furniture, plants, NPC fatigue, calendar hours, machine intent, and generated
removal records are restored before normal simulation.

Archives contain checksummed sections and independently compressed voxel columns.
Unchanged compressed columns are reused on save. Saving is synchronous and writes
a flushed temporary file before replacement; logs report its duration. Client
bootstrap and interest streaming use those authoritative columns. See
[Streaming](docs/WORLD_STREAMING.md) and [Runtime data](docs/RUNTIME_DATA.md).

## Remaining work and validation

The implemented feature plan includes biomes, structures, connecting routes,
hydrology, and village layouts. Rich resource economies, enemies, quests, and the
full technological progression remain design work. Furniture models and sleep
animations are also follow-up presentation work.

Run world planning, generation, structure, CeramicFish, archive, and streaming
tests in [VoxelgineEngine.Tests](VoxelgineEngine.Tests). Test totals and the full
acceptance procedure live in [Validation](docs/VALIDATION.md). Continue measuring
default-size generation time, peak memory, cancellation during expensive stages,
and broad seed quality before making shipping performance claims. The farming
and replication benchmarks do not establish generation performance.
