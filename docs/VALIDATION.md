# Validation

Run the complete local gate from the repository root for both configurations:

```powershell
pwsh -NoProfile -File scripts/Test-All.ps1 -Configuration Debug -CleanRuntimeData
pwsh -NoProfile -File scripts/Test-All.ps1 -Configuration Release -CleanRuntimeData
```

The script runs engine, client, and audio suites sequentially, then a Release
solution build, client/server publish audits, an isolated hosted/dedicated
multiplayer restart exercise, and automatic render smoke modes.
Add `-IncludeFishGfx` when validating a renderer dependency change. It also checks
assets, pinned submodules, and first-party Markdown links/protocol/archive/packet
references. Dependency Markdown is excluded; explicitly marked historical blocks
are excluded from current schema checks.

Each run owns `artifacts/validation/<timestamp>/`, with separate runtime roots per
smoke mode. Before/after tracked-content snapshots ensure the gate does not alter
existing edits or recursive submodules. Do not edit the repository while this
snapshot-protected gate is running. Test totals belong to that run's logs; older
migration totals are historical, not current acceptance evidence.

## Required regression coverage

- More than 32 placements followed by mining, building, and firing; rejected and
  duplicate actions; wire IDs remain contiguous after prediction rebasing.
- Full, partial-capacity, and rejected transfers with exact item conservation;
  two container viewers, cursor return, switching, death, disconnect and access loss.
- Persistent identity and policy, signed transcript binding, invalid/replayed
  proofs, tampered/replayed datagrams, duplicate identities/names, and admin gates.
- Duplicate/older object snapshots, cross-column identity conflicts, reordered
  deltas, first-part failure, partial enqueue, lost application acknowledgements,
  coalesced resync and reconnect cleanup.
- Farms of 1,024, 1,025 and 10,000 growing plants; maturity still occurs at second
  120. Unchanged stages produce no overlay writes or dirty notifications.
- NPC dawn/death/removal/navigation/bed cleanup, calendar changes across save/load,
  fatigue rebasing, and double timestamps beyond float uptime precision.
- Queue, handshake, snapshot, and encoded-cache bounds.

## Performance and acceptance limits

[Building](BUILDING.md) documents the filterable benchmarks and comparative probe.
Use Release, identical fixtures, and isolated original/current checkouts. Keep raw
measurements with machine and runtime information. Distinguish network tick and
component timing from a complete simulation/frame measurement; report tail
regressions and unsupported baseline workloads as well as gains.

World saving remains synchronous. `[Persistence] world-save` includes its duration,
bytes, and columns; capture those stalls from hosted and dedicated runs. Background
saving requires a separate design for immutable snapshots and ownership.

Graphical acceptance uses Release/x64 at 1920x1080, VSync disabled, 4x MSAA,
160-block draw distance, Medium shadows and fog. For stationary, rotation,
resident movement, streaming movement and local fog, use a 30-second warm-up and
five 60-second captures. Record frame median/p95/p99, allocation/queue metrics,
connect-to-playable time and save stalls. These are acceptance procedures, not
claims that this task measured every scenario.

Run hosted and dedicated multiplayer with separate temporary runtime roots.
Automated render smokes require non-background pixels and clean shutdown, but do
not establish manual visual quality, high-DPI/window-mode behavior, every audio
device, WAN conditions, Linux execution, or a fresh-machine checkout. Report any
unavailable checks separately and retain them as open acceptance work.

See the dated [hardening report](HARDENING_REPORT.md) for measured workloads,
validation evidence and the limits of that evidence.

The documentation gate now includes first-party FishGfx/FishUI local links and the current nested FishUI revision. For the graphics-specific suite, real-context probes and filtered benchmarks, see [FishGfx hardening validation](../thirdparty/FishGfx/HARDENING.md). Run nested suites sequentially before the parent integration gate.
