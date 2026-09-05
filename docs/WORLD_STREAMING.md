# World archive and spatial streaming

Renderer application is resumable under a 2 ms soft deadline and 512-unit limit.
Prepared and ordinary snapshots use the same atomic column publication path;
ordinary snapshots do not generate streamed-column acknowledgements. Block edits
remain behind pending columns, and acknowledgements remain behind earlier edits.
Each call permits one unit of progress; preparation, commit, and world reset can
exceed the soft deadline. Lighting separately checks a 2 ms deadline between
bounded propagation steps, while mesh uploads retain their 1 ms budget.

The renderer's global `IsIdle` includes deferred distant work. Camera-specific
`GetPendingWork(camera).IsSettled` reports whether eligible work has settled and
exposes dirty, deferred, lighting-blocked, in-flight, completed, upload and
transparent-sort state. Benchmark timeouts print this state and partial timings.

Aurora Falls currently treats world and network formats as development-only.
There is one active schema and no legacy reader, converter, or old-client path.

## Column archive

`<runtime-root>/worlds/map.bin` uses archive format 6. A checksummed section
directory stores metadata, independently compressed voxel columns, world plants,
furniture inventories, stable NPC life records, and generated-object tombstones.
Metadata includes spawn positions, generated features, machine intent, progression,
and absolute calendar hours. The hardening changes preserve this format.

Column payloads carry chunk-Y records and run-length encoded block values
(numeric block ID plus state byte), together with the separate packed fog layer.
They use independent fast Deflate compression and checksums. Player profiles
are stored separately by verified identity.

Server startup validates the magic/version before reading. An incompatible file
is moved to `map.bin.incompatible-<timestamp>.bak`, then the requested seed is
used to generate a new archive. Compatible columns are decoded in parallel and
published atomically into the authoritative `ChunkMap`. Saves reuse unchanged
compressed payloads and re-encode only columns whose runtime revision changed.

## Bootstrap and interest streaming

Successful identity authentication reserves a player ID and loads player data without placing the player
in the simulation. The server streams columns nearest-first:

1. Core columns intersecting a 32-block radius around the authoritative focus.
2. One 16-block halo ring used by lighting and boundary-safe prediction.
3. Ordinary interest columns only after bootstrap readiness is accepted.

At most 16 column packets may be delivered but not yet acknowledged as applied.
After startup, interest covers the configured draw distance plus a 32-block
preparation margin and refreshes on chunk-boundary movement, draw-distance
changes, or every 500 milliseconds. Received columns remain cached for the
session.

The client checksums and decodes packets on one bounded worker. Complete columns
are inserted at frame start under count and time budgets. Gameplay begins only
after core and halo columns are applied, lighting is published, core meshes are
resident or explicitly complete-empty, the transparent ordering matches the
current geometry, and `ClientWorldStartPacket` is received.

Unknown client columns are solid prediction boundaries. Raycasts and block edits
stop at them. The authoritative server owns the complete world and continues to
treat genuinely absent chunks as air.

## Reliable transport

Inside the authenticated protocol 2 envelope, reliable UDP payloads use this
17-byte header:

```text
flags:1 | sequence:4 | ackSequence:4 | ackBits:8
```

The transport permits 64 reliable packets in flight and acknowledges a 64-packet
history. Bulk world traffic is bounded and leaves reserved in-flight capacity for
control/gameplay traffic. Receive-state changes generate an ACK-only datagram in
the same update. Retransmission starts at 200 ms, adapts toward twice the measured
RTT, clamps to 100-1000 ms, backs off exponentially, and disconnects after 12
unsuccessful retries.

Reliability does not impose application ordering. Column revisions and action
IDs supply it. All datagrams, including ACK-only traffic, are authenticated before
connection state changes. The 41-byte envelope fits within the 1,200-byte MTU.
See [Multiplayer](../MULTIPLAYER.md) for handshake, replay and queue limits.

Search `<runtime-root>/logs/console.log` for `[WorldStream]`, `[Persistence]`, or `[Network]` to
diagnose checksums, revision resynchronization, backpressure, retries, archive
replacement, and readiness transitions. F5 exposes stream, queue, reliability,
lighting, and meshing progress.

## Local fog mutations

Fog is an independent four-byte voxel layer (`premultiplied RGB + density`) and
may coexist with solids. Block and fog edits increment the same column revision
and share one ordered mutation stream. Debug builds expose administrator-only `/fog fill` and
`/fog clear`; use `/help` for their bounded volume syntax. A missed mixed
revision causes the same full-column resynchronization as a missed block edit.

## Lightweight world objects and recovery

Voxel-column application triggers the matching object snapshot. Snapshots contain
at most 16,384 records, 4 MiB decoded data, 128 parts, 256 records per part, and
64 KiB per part. The server retains at most 16 outstanding snapshots per client,
one per column. Each snapshot retains its immutable payload and next unsent part.
Backpressure pauses transmission; it does not discard the remainder.

`WorldObjectColumnApplied` retires matching epoch/revision work. A missing
application acknowledgement causes a two-second retry. A first-part enqueue
failure remains pending and is retried when capacity returns. Resync requests
coalesce, later changes wait behind an in-flight snapshot, and failed delta
queueing schedules a fresh snapshot. Stream replacement and disconnect clear
outstanding work. Clients validate all records before mutation and acknowledge
ignored duplicate snapshots. Older streams, epochs, and revisions cannot replace
newer state. Deltas require the expected epoch and base revision.

An immature-crop index preserves X/Z/Y processing order without sorting every
world object each second. Wet wheat still advances by 547 fixed-point units per
second and matures on update 120. Growth commits at most 1,024 operations per
transaction. Planting, harvest, maturity, support loss, and restoration maintain
the index.

Delta subscribers receive column metadata without building sorted records. Full
ordered snapshots are built only for snapshot consumers and persistence. The
client coalesces pending object columns and compares position/material/growth
stage before voxel writes; unchanged stages cause no writes or mesh invalidation.
Changed overlay chunks receive one lighting-dirty notification per column apply.
The encoded voxel cache keeps only the newest revision per coordinate; in-flight
sends retain their own immutable payload references.

## Clocks and saving

Server simulation time derives from a long tick count and the unchanged float
physics step. Network and lifecycle timestamps use doubles. `SetTime` replaces
the hour within the current calendar day and rebases NPC fatigue timing. It does
not create elapsed fatigue or sleep recovery. Save/load preserves absolute hours.
NPC dawn, death, removal, navigation failure, and bed removal release sleep
reservations and life-control ownership.

Saves remain synchronous. `[Persistence] world-save` includes duration, byte size,
and column count, including the cost of flushing the temporary file and saving
players. Background saving is a separate concurrency task.
