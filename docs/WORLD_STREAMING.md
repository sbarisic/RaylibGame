# World archive and spatial streaming

Aurora Falls currently treats world and network formats as development-only.
There is one active schema and no legacy reader, converter, or old-client path.

## Column archive

`data/map.bin` is an indexed column archive. Its header stores the schema magic
and version, world seed, and authoritative player, pickup, and NPC spawn
positions. The directory maps each X/Z column to an offset, compressed length,
and checksum. Each payload contains the complete vertical column as chunk-Y
records, `(ushort run length, ushort block ID)` solid runs, and
`(ushort run length, uint packed fog)` runs, compressed independently with fast
Deflate. The current archive schema is version 2; version 1 is deliberately
incompatible and follows the same backup/regeneration path.

Server startup validates the magic/version before reading. An incompatible file
is moved to `map.bin.incompatible-<timestamp>.bak`, then the requested seed is
used to generate a new archive. Compatible columns are decoded in parallel and
published atomically into the authoritative `ChunkMap`. Saves reuse unchanged
compressed payloads and re-encode only columns whose runtime revision changed.

## Bootstrap and interest streaming

Connecting reserves a player ID and loads player data without placing the player
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

Reliable UDP datagrams use this 17-byte header:

```text
flags:1 | sequence:4 | ackSequence:4 | ackBits:8
```

The transport permits 64 reliable packets in flight and acknowledges a 64-packet
history. Bulk world traffic is bounded and leaves reserved in-flight capacity for
control/gameplay traffic. Receive-state changes generate an ACK-only datagram in
the same update. Retransmission starts at 200 ms, adapts toward twice the measured
RTT, clamps to 100-1000 ms, backs off exponentially, and disconnects after 12
unsuccessful retries.

Search `data/console.log` for `[WorldStream]`, `[Persistence]`, or `[Network]` to
diagnose checksums, revision resynchronization, backpressure, retries, archive
replacement, and readiness transitions. F5 exposes stream, queue, reliability,
lighting, and meshing progress.

## Local fog mutations

Fog is an independent four-byte voxel layer (`premultiplied RGB + density`) and
may coexist with solids. Block and fog edits increment the same column revision
and share one ordered mutation stream. Debug builds expose `/fog fill` and
`/fog clear`; use `/help` for their bounded volume syntax. A missed mixed
revision causes the same full-column resynchronization as a missed block edit.
