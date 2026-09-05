# Voxel performance work — 2026-09-05

This change covers terrain materialization, meshing, renderer application and
geometry retention. It preserves generated voxel contents, numeric block IDs,
world ownership, the physics timestep and rendering quality settings. Plan format
6, generator profile 14, materializer 11, archive format 6 and protocol 2 are unchanged.

## Implementation

- Terrain profiles are calculated per horizontal cell. Up to four workers own
  independent chunk buffers; coordinate-ordered assembly precedes serial feature
  stamping. Hill and tree-density planning reuse a feature-exclusion bit raster.
  The unused tree derivation during plan validation has been removed.
- Renderer application processes resumable column transactions under a 2 ms soft
  deadline and 512-unit ceiling. Later block changes cannot overtake columns.
  Ordinary column notifications do not acknowledge streamed revisions. Lighting
  has a separate 2 ms deadline, checked every 64 consumed operations. Publication
  remains transactional. Preparation, commit and reset are indivisible units and
  can exceed these soft limits; uploads retain their existing 1 ms budget.
- Meshing reuses visibility/model selections and unique corner light/AO samples.
  Cube tangents are precomputed. Dirty chunks are indexed by horizontal column;
  camera-bounded queries retain distant work for later scheduling.
- `VoxelMeshing` in game configuration accepts `Culled` and `GreedyOpaque` and
  takes effect when a scene is created. **Culled remains the default.** Greedy
  merges only matching opaque cube faces with uniform lighting/AO. It preserves
  per-voxel texture scale, custom models, cutout geometry and transparent ordering.
- Transparent scratch capacity follows visible faces. Page grouping preserves
  submission order in one pass. Empty GPU pages retain one spare per pool and
  become reclaimable after two seconds, at most one page per renderer update.
  Upload work and queued references protect storage from deletion.
- Render queues now implement idempotent disposal, including cleanup after a
  retained resource throws. Bucket-capacity reuse was tested and omitted: it
  reduced allocations by only 10.4% and slowed the measured submission workload.

## Reproduce the measurements

From the repository root:

```powershell
dotnet run --project BenchmarkSuite1 -c Release -- --voxel-generation 256 5 --features --stage-profile
dotnet run --project BenchmarkSuite1 -c Release -- --voxel-generation 1024 5 --features --stage-profile
dotnet run --project thirdparty/FishGfx/FishGfx.Benchmarks -c Release -- mesh
dotnet run --project thirdparty/FishGfx/FishGfx.Benchmarks -c Release -- schedule
dotnet run --project thirdparty/FishGfx/FishGfx.Benchmarks -c Release -- group
dotnet run --project thirdparty/FishGfx/FishGfx.Benchmarks -c Release -- sort-
dotnet run --project thirdparty/FishGfx/FishGfx.Benchmarks -c Release -- geometry
```

Generation uses seeds 123456, 66791 and 8142 at height 64. `--features` loads the
checked-in structure and CeramicFish catalogs; omitting it isolates natural
terrain/hydrology/vegetation. The optional staging callback excludes authoritative
publication; the aggregate materialization measurement includes it. The probe
hashes voxel coordinates, types and state bytes outside the timed region.
Working-set peaks are cumulative process peaks, not per-stage retained memory.
With five samples, p95/p99 are maxima; they are not statistically stable tail
estimates. A 2048-square run is an optional memory stress workload.

Baseline: parent `eb90d0bc7198a90dc0a8e9e007b8da0a0d5be8bb`, FishGfx
`f8f7f9afe994d41e27d6f551b79116bf134bb9e9`, in an isolated checkout with the same
measurement code. Timed runs were sequential on Windows x64/.NET 10, using an AMD
Radeon RX 9070 XT for native checks. Generated output belongs under ignored
`artifacts/voxel-performance-20260905`, not in commits.

## Measured results

Feature-rich 1024-square materialization, milliseconds, five measured samples:

| Seed | Baseline median | Candidate median | Reduction | Baseline / candidate p95 |
|---|---:|---:|---:|---:|
| 123456 | 2429.02 | 1286.99 | 47.0% | 2666.24 / 1347.79 |
| 66791 | 2225.08 | 1085.58 | 51.2% | 2291.88 / 1403.37 |
| 8142 | 2391.76 | 1064.34 | 55.5% | 2464.28 / 1083.23 |

All six feature-rich voxel fingerprints (three seeds at 256 and 1024) match the
baseline. Materialization allocations fall by less than 1%; this is principally
a CPU improvement. Feature-rich planning remains significant: the 256-square
8142 fixture takes about 15 seconds and allocates about 7 GB cumulatively. This
work does not claim an equivalent whole-startup speedup or a solved village planner.

CPU meshing results below are medians of five independent runs, each with at least
300 ms warm-up and 30 samples. Output buffers are pooled in both builds.

| Workload | Baseline median ms | Candidate median ms | Reduction |
|---|---:|---:|---:|
| Solid chunk, culled | 1.37478 | 1.08299 | 21.2% |
| Four-layer slab, culled | 0.65502 | 0.47924 | 26.8% |
| Checkerboard, culled | 10.11080 | 7.12762 | 29.5% |
| Sort 100,000 faces, 1% visible | 0.10328 | 0.04286 | 58.5% |
| Schedule with 65,536 deferred chunks | 0.41506 | 0.00447 | 98.9% |
| Group 4,096 opaque entries over 128 pages | 3.44290 | 0.25200 | 92.7% |
| Group 4,096 shadow entries over 128 pages | 6.24988 | 0.16174 | 97.4% |

Meshing allocations remain 160 bytes per operation. Greedy reduces a uniform
solid chunk from 9,216 vertices to 36, but its merge pass costs additional CPU
time; fragmented geometry does not receive the same benefit. The feature remains
opt-in pending the full visual/frame-time promotion gates. Sorting all-visible
geometry showed variable tails and requires separate interpretation from the
large gains when most faces are invisible.

An isolated repeat of the all-visible 100,000-face case measured 13.95118 /
15.16014 / 15.37858 ms median/p95/p99 before and 13.29406 / 15.39916 /
15.79356 ms after: less than 5% tail variation, without a material full-visible
speedup claim. Scheduling allocations rise from 48 to 392 bytes per query due to
bounded iterator state; grouping rises from 4,120 to 4,192 bytes per operation.
The rejected queue-reuse experiment measured 0.05492 → 0.08486 ms median and
160,624 → 144,000 bytes. Final queue clearing retains the original allocation
behavior; disposal is a lifetime fix, not a claimed speedup.

The native ownership probe reduces eight released 1 MiB geometry pages to one
spare (87.5% fewer retained vertex pages). It verifies that a queued allocation
survives trimming before its final release. These are allocator counters, not a
vendor-reported total VRAM measurement.

## Validation and remaining gates

Fixed-camera, fixed-time 1920×1080 captures on GL 4.6 differ from the original
culled renderer in 1,445 channels, each by one byte. Candidate culled and greedy
showcase captures match exactly. A separate two-material merged-platform fixture
differs in 899 pixels (0.0434%), concentrated at tile edges; visual inspection
confirms per-block repetition. These checks cover selected views, not all normal
maps, shadows, fog or camera paths.

The plan's assumption of voxel support on OpenGL 4.0 was incorrect. The existing
renderer explicitly requires 4.3. Exact 4.0 rejects voxel rendering as expected;
the general FishGfx primitive gallery has a separate 4.0 fallback. This work does
not lower that minimum or introduce a renderer/backend rewrite.

Regression coverage includes baseline fingerprints across terrain worker counts
1/2/4, feature/fog equivalence, cancellation before publication, legacy solid
sampling, greedy coverage, negative chunk coordinates, mixed materials, lighting
deadline convergence, deferred work, stable page grouping and queue disposal.

The streaming benchmark now uses camera-eligible settlement and reports partial
measurements plus work counters on timeout. It measures the standalone FishGfx
world, not the parent generation pipeline or full gameplay rendering.

Five paired startup-streaming runs gave median run summaries of 3.41 / 12.94 /
18.99 ms frame median/p95/p99 before and 3.43 / 12.19 / 18.03 ms after. Median
frame time is effectively unchanged; the measured tails improve by about 5–6%.
These short startup runs are separate from the sustained capture procedure below.
Greedy ready buffers are compacted after merging so queue byte accounting does
not conceal a large culled backing array behind a small logical result.

Full acceptance still uses the settings and five 60-second captures per camera
workload in [Validation](VALIDATION.md). Focused automated checks must not be
reported as exhaustive manual visual acceptance, another GPU vendor, Linux, old
OpenGL hardware, WAN soak or a 2048-square stress run.

## Final integration results

FishGfx `302661197f4d52e8ac2f479e41348142a78b4855` is published on
`origin/master`. Its nested FishUI revision remains
`18285683769e38ac0842af1f63efc9d033b5bacd`; upstream CI removal is preserved.

Sequential Debug and Release suites passed with 456 FishGfx, 245 FishUI,
428 engine, 234 client and 29 audio tests in each configuration. Both supported
Release solution builds completed with zero warnings and errors. The parent
integration gates completed in:

- Debug: `artifacts/validation/20260905-181029-674`.
- Release: `artifacts/validation/20260905-181808-840`.

Both gates passed client/server publication and layout audits, authenticated
hosted/dedicated bootstrap and restart scenarios, permission and persisted-ban
checks, and all 11 automatic render smoke modes. Runtime roots were isolated;
before/after recursive repository snapshots matched. These automatic modes do
not replace the outstanding sustained and manual visual checks above.

Supported-solution NuGet vulnerability audits reported no vulnerable packages.
The excluded legacy FishGfx `Test` project's Newtonsoft.Json 12.0.3 alert remains
documented as BUG-038; this work does not claim to resolve it. Documentation checks
cover 20 parent, seven FishGfx and 17 FishUI Markdown files, including local links,
current protocol/archive references and nested pins. Generated captures, runtime
data and temporary baseline checkout changes are excluded from publication.
