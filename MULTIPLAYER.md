# Multiplayer reference

The server owns gameplay state. The Windows FishGfx client predicts movement and
renders snapshots; the portable dedicated server and the hosted server run the
same engine. There are at most ten connected gameplay slots. Physics retains the
0.015-second fixed step, numeric block IDs, and existing movement rules.

## Identity and connection establishment

Current network protocol: **2**. Both endpoints must run the same protocol.
There is no unauthenticated gameplay connection path. `Connect` remains a
reserved legacy packet identifier; it cannot allocate a player.

1. The client sends a bounded hello containing the protocol, display name,
   P-256 signing public key, ephemeral P-256 ECDH public key, and 32-byte nonce.
2. The server returns its signing and ephemeral public keys, the client nonce,
   a fresh 32-byte server nonce, and an ECDSA-SHA256 signature over the hello
   and unsigned challenge, with a protocol-specific transcript label.
3. The client verifies the signature and its saved server pin, then signs that
   same transcript. The server verifies this proof before allocating a slot or
   loading a player profile. Names must contain 1-48 printable characters.
4. SHA-256 of the encoded signing public key identifies the player. The server
   rejects a banned identity, an identity already connected, or a display name
   already used by another connection (case-insensitive).
5. Ephemeral ECDH plus HKDF-SHA256 derives separate keys for each direction.
   The client pins the server only after receiving an authenticated acceptance.

Handshake datagrams begin with `0xFA`, a kind byte, then ushort-length-prefixed
fields. Pending handshakes expire after ten seconds; the server retains at most
256 overall and four per source IP. Malformed-handshake diagnostics are limited
to one per second. Invalid or expired proofs cannot create players.

Signing keys are persistent local PKCS#8 files. There is no account service or
name-based ownership. Back up keys, profiles, and server permissions together.
See [Runtime data](docs/RUNTIME_DATA.md) for paths and explicit trust reset.

## Authenticated UDP and reliability

Established datagrams use:

```text
0xFB:1 | session transcript ID:16 | directional counter:8 | payload | HMAC tag:16
```

The tag is truncated HMAC-SHA256. It covers the envelope, counter, batch,
reliability headers, acknowledgements, and packet payloads. Authentication and a
1,024-counter replay window run before reliability or receive-deadline updates.
Packets may arrive out of order within that window. This provides authenticity
and integrity; it does not encrypt gameplay contents.

The 1,200-byte datagram budget includes the 41-byte authentication envelope.
Reliable payloads have a 17-byte header (`flags`, sequence, ACK sequence, 64 ACK
bits); fragmented payloads also reserve the five-byte fragment header. Control
and gameplay share a 256-fragment queue; bulk has 128 slots. There are at most 64
reliable fragments in flight, with bulk limited to 48. Retries adapt from 200 ms
within 100-1,000 ms, back off, and eventually disconnect on delivery failure.

Reliability suppresses duplicates and retries delivery. It does **not** promise
application ordering. Item-use sequence IDs, inventory revisions, command
history, and world column revisions supply ordering where required. Unreliable
input and movement snapshots also carry authenticated envelopes.

Incoming storage is bounded to 4,096 datagrams or 8 MiB. Each server tick drains
at most 512 datagrams, with a ceiling of 64 per endpoint and fair rotation.
Overflow is dropped before reliability processing, so it is not acknowledged and
reliable senders can retry. Transport deadlines and interpolation timestamps use
monotonic doubles; physics deltas and interpolation factors remain floats.

## Gameplay transactions and permissions

Wheat, baskets, beds, block placement/removal, and weapon use share one ordered
item-use queue. Each request carries an action ID, simulated command tick, and
primary/secondary channel. The server checks recorded selection, input, aim,
reach, world state, and inventory before mutation. Every completed action returns
`ItemUseResult`, including rejected requests. A tick/channel can execute once.

Inventory-click IDs are allocated when sent, with one click in flight and at
most 32 predictions. Unsent predictions may be removed during rebasing without
skipping IDs. `InventoryState.AcknowledgedActionId` acknowledges only inventory
clicks; zero means an unsolicited update. Crafting and item-use IDs do not
acknowledge clicks. Duplicate crafting and container requests cannot execute twice.
Transfers validate capacity before either store changes.

Container sessions bind the player, furniture identity, generation, and both
inventory revisions. Opening another container resolves the previous cursor.
Closing returns the cursor, persists the furniture record, and informs remaining
viewers. Death, removal, disconnect, distance, or blocked access closes the
session. Connection teardown clears pending transactions and cursor ownership.

Global day/night and NPC commands require administrator permission, as do debug
spawn/place and Debug-only give/fog/structure commands. Normal movement, items,
crafting, machines, and container interaction remain available. The host identity
is supplied by application composition; loopback addresses grant no privileges.
Dedicated-console `op`, `deop`, `ban`, and `unban` update identity-based policy.
Bans are persisted before disconnection. See [Hosting](VoxelgineServer/README.md).

## Spatial and object replication

After authentication, the server reserves the player while core and halo columns
load. `ClientWorldReady` and `ClientWorldStart` gate entry into gameplay. Inputs
carry the newest command and up to three prior commands; authoritative command
history drives reconciliation and ordered item use.

Object snapshots identify their stream, column, epoch, revision, snapshot, and
multipart indexes. Complete contents are validated before index replacement,
including keys already owned by another column. Older or duplicate snapshots
cannot roll back state; duplicates are acknowledged without reapplication.
Retired streams cannot replace the active stream.

Each client has at most 16 outstanding object snapshots. Unsent parts survive
backpressure, one snapshot remains in flight per column, and subsequent changes
coalesce as pending work. Application acknowledgements retire snapshots. Missing
acknowledgements retry after two seconds; even a first-part enqueue failure
retains work. Failed delta enqueueing schedules a fresh snapshot. Disconnect or
stream replacement releases the work. See [Streaming](docs/WORLD_STREAMING.md).

## Packet identifiers

The table mirrors [PacketType](VoxelgineEngine/Engine/Net/Packet.cs). Binary field
order is defined by each packet's `Write` and `Read` methods in
[Engine/Net](VoxelgineEngine/Engine/Net). There are no distinct reliable ordered
and reliable unordered transport channels.

| ID | Packet | Contract |
|---|---|---|
| 0x01 | `Connect` | Reserved legacy identifier; no gameplay acceptance |
| 0x02 | `ConnectAccept` | Authenticated slot, server tick, seed |
| 0x03 | `ConnectReject` | Rejection reason |
| 0x04 | `Disconnect` | See packet serialization contract |
| 0x05 | `PlayerJoined` | See packet serialization contract |
| 0x06 | `PlayerLeft` | See packet serialization contract |
| 0x10 | `InputState` | Up to four input commands with tick, keys, aim, selection |
| 0x11 | `PlayerSnapshot` | See packet serialization contract |
| 0x12 | `WorldSnapshot` | Authoritative player states and consumed command ticks |
| 0x20 | `BlockChange` | Column revision and bounded block/state batch |
| 0x21 | `BlockPlaceRequest` | Action ID, command tick/channel, coordinate, block/state |
| 0x22 | `BlockRemoveRequest` | Action ID, command tick/channel, coordinate |
| 0x23 | `FogChange` | Column revision and fog batch |
| 0x40 | `WorldStreamBegin` | See packet serialization contract |
| 0x41 | `WorldColumn` | See packet serialization contract |
| 0x42 | `WorldBootstrapComplete` | See packet serialization contract |
| 0x43 | `WorldColumnApplied` | See packet serialization contract |
| 0x44 | `WorldColumnResyncRequest` | See packet serialization contract |
| 0x45 | `ChunkInterest` | See packet serialization contract |
| 0x46 | `ClientWorldReady` | See packet serialization contract |
| 0x47 | `ClientWorldStart` | See packet serialization contract |
| 0x30 | `EntitySpawn` | See packet serialization contract |
| 0x31 | `EntityRemove` | See packet serialization contract |
| 0x32 | `EntitySnapshot` | See packet serialization contract |
| 0x33 | `EntitySpeech` | See packet serialization contract |
| 0x34 | `WorldInteractRequest` | See packet serialization contract |
| 0x35 | `WorldObjectPlaceRequest` | Action ID, command tick/channel, coordinate, placement kind |
| 0x36 | `WorldObjectColumn` | Stream, epoch/revision, snapshot parts and checksum |
| 0x37 | `WorldObjectDelta` | Stream, epoch, base/new revisions, bounded operations |
| 0x38 | `WorldObjectResyncRequest` | Stream, coordinate, client epoch/revision |
| 0x39 | `WorldObjectColumnApplied` | Stream, coordinate, epoch and installed revision |
| 0x60 | `WeaponFire` | Action ID, command tick/channel, weapon type |
| 0x61 | `WeaponFireEffect` | See packet serialization contract |
| 0x62 | `PlayerDamage` | See packet serialization contract |
| 0x50 | `ChatMessage` | See packet serialization contract |
| 0x51 | `ConsoleCommandRequest` | See packet serialization contract |
| 0x52 | `ConsoleCommandResult` | See packet serialization contract |
| 0x70 | `DayTimeSync` | See packet serialization contract |
| 0x80 | `Ping` | See packet serialization contract |
| 0x81 | `Pong` | See packet serialization contract |
| 0x90 | `InventoryState` | Click acknowledgement, inventory/selection revisions, cursor and 60 slots |
| 0x91 | `InventoryActionRequest` | Click ID, expected revision, kind, slot |
| 0x92 | `ItemUseResult` | Use ID, command tick, result, inventory revision |
| 0x93 | `InfrastructureState` | See packet serialization contract |
| 0x94 | `CraftRequest` | Separate action ID and recipe |
| 0x95 | `CraftResult` | Separate action ID and outcome |
| 0x96 | `ContainerState` | Session, furniture key, both revisions, open flag and slots |
| 0x97 | `ContainerActionRequest` | Session/action IDs, both expected revisions, click and slot |
| 0x98 | `ContainerClose` | Session ID |
| 0xA0 | `SoundEvent` | See packet serialization contract |
| 0xB0 | `KillFeed` | See packet serialization contract |
| 0xC0 | `DebugSpawnEntityRequest` | See packet serialization contract |
| 0xC1 | `DebugPlaceBlockRequest` | See packet serialization contract |
