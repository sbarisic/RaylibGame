# Gameplay and multiplayer hardening - 2026-09-05

## Delivered changes

The twelve review findings are addressed in the implementation and regression
coverage:

| Finding | Result |
| --- | --- |
| Placement exhausted item-use IDs | Wheat, baskets and beds use the ordered command-history queue and `ItemUseResult`, including rejection and duplicate handling. |
| Large farms exceeded the transaction limit | Deterministically ordered growth is split into transactions of at most 1,024 operations. |
| Prediction rebasing skipped click IDs | Click IDs are assigned on send. Unsolicited inventory, crafting and item-use updates do not acknowledge clicks. |
| Global mutation lacked authorization | Global commands and debug mutation packets require the composition-provided host identity or a persisted administrator grant. |
| Display names controlled profiles and ownership | Verified persistent P-256 identities own fresh profiles and cursors; duplicate identities and ambiguous display names are rejected. |
| Object snapshots rolled back or partially replaced indexes | Snapshots validate before mutation, ignore older/duplicate revisions, and reject cross-column identity conflicts. Deltas require an installed snapshot. |
| Object sends lost parts and ignored application acknowledgements | Resumable per-client work retains parts through backpressure, retries missing acknowledgements after two seconds, and bounds outstanding snapshots to 16. |
| Container close/access handling lost synchronization | Centralized close resolves cursors, updates furniture and other viewers, and runs on switching, death, disconnect, removal and access loss. |
| NPC sleep ownership leaked | Dawn, death, detach, bed removal and navigation cancellation release bed and life-control ownership. |
| Explicit time changes disagreed with persisted calendar time | Time changes preserve the current calendar day and rebase NPC fatigue timing. |
| Rejected transfers partially mutated stores | Transfers validate both candidate stores before committing; rejection preserves contents and revisions. |
| Bans only disconnected the current session | Identity-based bans are persisted before disconnect, with local `op`, `deop` and `unban` operations. |

Network protocol 2 adds a nonce/transcript-bound signed handshake, ephemeral ECDH,
HKDF-SHA256 directional keys, whole-datagram authentication and replay rejection
before reliability processing. Server keys are pinned after successful connection;
changing a pin requires an explicit local trust reset. There is no unauthenticated
gameplay compatibility path. Authentication does not encrypt gameplay traffic.

Receive storage is capped at 4,096 datagrams or 8 MiB, with a server-tick budget of
512 datagrams and 64 per peer, in fair rotation. Pending handshakes are limited to
256 overall and four per source IP, expiring after ten seconds. Other changes
include an immature-crop index, coalesced/differential object overlays, separate
delta notifications, newest-revision column caching, Info logging by default on
dedicated servers, and double monotonic timestamps. Physics still uses the existing
0.015-second float step; numeric block IDs and world archive format 6 are unchanged.

## Release measurements

Machine: Windows x64, AMD Ryzen 5 3600XT, 6 cores / 12 logical processors, .NET SDK
10.0.400 and .NET runtime 10.0.11. The original engine came from commit
`8769d479fe210e5fd374ba61da352ffa50608542`, extracted into an isolated ignored
artifact directory. The same [probe source](../BenchmarkSuite1/Measure/Program.cs)
was compiled against the original and updated engine. Each workload used five
warm-up iterations and 100 samples, with fixture setup excluded. Builds and tests
were not running concurrently with these measurements.

Values below are milliseconds, shown as **median / p95 / p99**. Allocations are
mean bytes allocated on the measured thread per sample; they include probe
overhead and do not measure other threads. Short runs expose JIT/GC/OS noise,
especially at p99; these are local measurements, not general performance promises.

| Workload | Before ms | After ms | Allocated bytes before / after |
| --- | --- | --- | --- |
| 1,024 plants, all growing | 3.7053 / 6.7595 / 41.1858 | 1.8327 / 4.6609 / 29.3672 | 1,252,311 / 1,097,129 |
| 10,000 plants, 1,024 growing | 2.6650 / 3.8150 / 29.3878 | 0.8539 / 1.6134 / 26.8342 | 2,478,595 / 1,081,092 |
| 1,025 plants, all growing | Transaction-limit exception | 0.5735 / 1.2836 / 1.4658 | unavailable / 1,279,858 |
| 10,000 plants, all growing | Transaction-limit exception | 9.5150 / 21.8514 / 26.5967 | unavailable / 11,193,877 |
| Unchanged-stage overlay, 1,024 plants | 0.0397 / 0.0626 / 3.4195 | 0.1043 / 0.1401 / 4.2790 | 87,765 / 173 |
| Repeated encoding of one revised column | 0.1304 / 0.2245 / 0.2727 | 0.0990 / 0.1352 / 2.3607 | 42,952 / 42,776 |
| Receive-burst network tick | 1.0305 / 1.6743 / 1.9279 | 0.1165 / 0.1841 / 0.8336 | 2,818,069 / 30,005 |
| Empty-world simulation tick | 0.0015 / 0.0025 / 0.0037 | 0.0016 / 0.0021 / 0.0032 | 653 / 653 |
| Synchronous one-column save | 1.8728 / 2.4395 / 2.6787 | 1.5783 / 1.9447 / 2.1695 | 9,387 / 9,392 |

Interpretation and counters:

- The farming probe includes object-stream notifications, with no connected
  players. Its input crops are reset before each sample. The all-growing
  10,000-plant case now works but remains a substantial periodic workload.
- The old overlay branch models the original remove/rewrite/dirty loop using
  counters; it does not execute GPU uploads or mesh rebuilding. Comparison costs
  more CPU in this probe, but writes and dirty notifications fall from 2,048 each
  to zero for unchanged stages. A full frame-rate improvement is not measured.
- After 105 revisions of one coordinate, encoded-cache entries fall from 105 to
  one. Outstanding sends retain their immutable payloads. Encoding p99 regressed
  in this short run even though median/p95 improved.
- The receive test presents 4,096 one-byte invalid datagrams from eight peers.
  The old server drains all of them; the new tick processes 512 and leaves 3,584
  datagrams / 3,584 bytes. This measures the deliberate work budget, not equal-work
  throughput. Regression tests separately verify the 8 MiB and per-peer ceilings.
- Empty-world ticks are a scheduling baseline, not a populated-world simulation
  profile. The small save comparison is storage-sensitive; no asynchronous save
  optimization was implemented. In the final gate, live 16-column smoke saves
  took 12.0 ms hosted and 18.0 / 16.4 ms on the dedicated server before / after
  restart.

Raw local evidence is under `artifacts/hardening-baseline/before-final.json` and
`after-final.json`. [Building](BUILDING.md) describes the probe options and
BenchmarkDotNet filters. BenchmarkDotNet workloads are included for longer runs;
the table above comes from the bounded comparative probe.

## Validation evidence

All three suites ran sequentially in each configuration, with zero failures or
skips in the final runs:

| Suite | Debug passed | Release passed |
| --- | --- | --- |
| Engine | 423 | 423 |
| Client | 234 | 234 |
| Audio | 29 | 29 |
| Total | 686 | 686 |

This adds 48 tests to the original 638-test baseline. Debug TRX files and logs are
under `artifacts/hardening-validation/verified-{engine,client,audio}-debug`.
The complete Release gate passed under
`artifacts/validation/20260905-123909-727`; its outer log is
`artifacts/hardening-validation/release-gate-verified.log`.

The Release solution build had zero warnings and errors. Client and dedicated
server publish audits passed, including the server's exclusion of client,
graphics and audio binaries. Asset validation passed for 907 content files and
65 direct references; all recursive dependency pins passed unchanged.

All 11 isolated automatic render modes passed: menu options, host, join,
developer, gameplay, transition, NPC, effects, voxel materials, world preview
and CeramicFish village lab. The runs initialized miniaudio and shut down
cleanly. The material-preview diagnostic screenshot was also inspected. These
checks do not replace the manual acceptance work listed below.

The final gate's before/after repository snapshots matched, including the index
and all recursive submodules. An earlier run passed functional checks but detected
staging during execution; existing tracked file contents were unchanged. The
gate was rerun with that staging preserved. Final documentation and whitespace
checks passed after this report was reconciled.

The filtered benchmark entrypoint listed exactly the three requested hardening
benchmark classes.

The isolated hosted/dedicated preflight and final gate passed authenticated bootstrap decoding,
gameplay activation, host/admin command acceptance, ordinary-player denial,
persisted bans and administrator grants across process restart, local unban,
reconnection to the same identity profile, disconnect and save. Each server and
client had a separate temporary runtime root. The harness is part of
`scripts/Test-All.ps1` so this behavior remains a repeatable gate.

## Documentation and retained work

All 18 existing first-party Markdown documents were reviewed and updated. This
report is the nineteenth document. Current architecture, renderer, packet table,
hosting, key storage, trust reset, legacy backups, world planning/materialization,
native/audio contracts, controls, dependency pins, build and validation guidance
now describe the implementation. Dependency documentation was left untouched during the gameplay stage; the subsequent graphics/UI refresh is recorded below.
Historical accomplishments in `DONE.md` are retained in an explicitly historical
block; mod-system and worldbuilding proposals are labelled as proposals.

`scripts/Test-Documentation.ps1` checks local file links, protocol/archive
references outside historical sections, and the packet table against source.
It does not verify external links or Markdown heading anchors.

Furniture models, sleep animations, the separate NPC interaction report,
background saves and new gameplay remain in [TODO](../TODO.md). This run does not
establish WAN soak behavior, Linux execution, fresh-machine setup, manual visual
quality, high-DPI/window-mode behavior, every audio device, or the full five-run
graphical performance procedure in [Validation](VALIDATION.md). Those acceptance
checks remain open; automatic render smokes are reported separately.

## FishGfx and FishUI implementation and publication

The subsequent graphics/UI audit is implemented in FishUI `18285683769e38ac0842af1f63efc9d033b5bacd` and FishGfx `f8f7f9afe994d41e27d6f551b79116bf134bb9e9` (implementation commit `1560b87a89cb45449977bf8515167e3afc289d8f`). Both repositories are published on `origin/master`; the nested pointer matches. Upstream FishUI CI removal was fast-forwarded and preserved.

The updated dependency fixes font-atlas rollback, resource binding invalidation, binary sizes, cancellation cleanup, Unicode editing, stable tab order, label layout, numeric ranges, bounded animation/particle updates, and disabled voxel culling. An additional full-geometry-page capacity bug was fixed. Warm text measurement and context selection now allocate zero in the focused benchmarks; wrapping and glyph batches allocate substantially less. Exact median/p95/p99 results, experiment boundaries, API migrations and follow-up work are in [FishGfx's report](../thirdparty/FishGfx/HARDENING.md).

FishGfx passed 446 tests and FishUI passed 245 tests in each of Debug and Release. Supported solution builds, package audits, actual OpenGL 4.0/4.6 galleries, windowing, voxel, node-editor and both Raylib UI frame modes passed. These checks preserve the earlier staged gameplay work, which is included in the parent publication. All first-party Markdown in both dependencies was reviewed; their local-link checks now run from the parent documentation gate.

The supported-solution package audits reported no vulnerable packages. GitHub separately reported an open high-severity alert for Newtonsoft.Json 12.0.3 in FishGfx's excluded .NET Framework `Test` project. BUG-038 records its patch/JSON-loader follow-up under the separate legacy migration. No Unity editor/player, Linux/macOS, other-GPU, WAN soak or exhaustive manual input/visual acceptance is claimed. Generated captures, benchmark baselines and runtime roots remain ignored validation artifacts.

### Final parent verification

The Debug integration gate passed in `artifacts/validation/20260905-141004-691` (423 engine, 234 client and 29 audio tests). The Release gate passed in `artifacts/validation/20260905-141638-958`. A final bitmap-font char compatibility regression was then reproduced and fixed upstream; all 446 FishGfx tests passed again in Debug and Release. The final parent Release gate at FishGfx `f8f7f9afe994d41e27d6f551b79116bf134bb9e9` passed in `artifacts/validation/20260905-142130-433`, again with all 686 parent tests, a zero-warning Release solution build, client/server publish audits, authenticated hosted/dedicated restart scenarios, all 11 automatic render modes and matching before/after recursive repository snapshots.

The final parent package audit reported no vulnerable packages in the supported solution. The excluded legacy FishGfx alert remains as described above. Documentation checks passed for 19 parent, six FishGfx and 17 FishUI Markdown files; current protocol, archive, packet IDs and nested pins match source. Final report edits were followed by documentation and whitespace checks. The publication includes the previously staged gameplay hardening and the reviewed dependency changes; it excludes generated captures, temporary baseline checkouts, private identities and runtime data.

## Subsequent voxel performance delivery

The [voxel performance report](VOXEL_PERFORMANCE.md) records the subsequent
generation, meshing, scheduling and geometry-retention changes at FishGfx
`302661197f4d52e8ac2f479e41348142a78b4855`. Its final Debug and Release gates pass
691 parent tests per configuration, with 456 FishGfx and 245 FishUI tests tested
separately in both configurations. Earlier counts and revisions above describe
the preceding hardening delivery. Greedy meshing remains opt-in; the new report
separates measured component gains from outstanding sustained visual acceptance.
