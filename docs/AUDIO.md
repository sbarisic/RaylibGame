# Audio architecture

## Why miniaudio

The client uses miniaudio rather than OpenAL. OpenAL could provide device
playback and basic spatial sources, but it would still require a separate
streaming, resource, mixing, and higher-level voice policy layer. Miniaudio
already supplies the device engine, resource manager, streaming, sound groups,
basic 3D spatialization, and node graph needed by this game.

Steam Audio is a future acoustics and spatialization layer, not the playback
engine. It can later process a source's dry mono signal before the destination
bus. The current build has no OpenAL or `phonon.dll` runtime dependency.

## Layers and ownership

```text
gameplay -> IGameAudioSink / GameAudioEvent
              |
              v
Voxelgine.Audio
  AudioSystem, AudioCueBank, AmbienceController, safe handles
              |
              v
VoxelAudioNative.dll
  va_* C ABI, engine, resource manager, voices, streams, buses, node seam
              |
              v
pinned miniaudio source
```

The native interface exposes opaque engine, clip, stream, and generation-checked
voice handles. No `ma_*` structure crosses the DLL boundary because miniaudio
does not guarantee a stable binary layout for those structures. Managed interop
uses source-generated `LibraryImport` declarations and safe handles.

The real-time mixer thread performs no managed callback, managed allocation,
logging, or file loading. Native completion, error, underrun, and statistics
queues are consumed by `AudioSystem.Update` on the game thread. Missing files,
unknown cues, and an unavailable device degrade to silence and are logged once.

The native `va_source_processor` route is the extension seam. Basic miniaudio
spatialization is active today. A future Steam Audio node must replace that
processor for a source and disable miniaudio spatialization for the same source
to avoid processing position twice.

## Cue bank

Runtime cue policy is data-driven in
`Voxelgine/data/audio/audio-bank.json`. Variant paths are resolved relative to
the bank file. A cue defines:

- one or more variants;
- bus, cue gain, and per-variant gain/pitch range;
- 2D or 3D spatial mode and min/max distance;
- streaming and looping behavior; and
- an instance limit per variant.

The buses are `Master`, `Sfx`, `Ambience`, `Music`, `Voice`, and `Ui`. Short
effects use shared decoded clips. Long ambience uses streams. Gameplay cues
retain six voices per variant; a seventh play steals the oldest voice.

| Cue group | Playback | Default range |
|---|---|---|
| Walk, jump, crash, swim | Buffered mono 3D | 1-24 |
| Block place/break | Buffered mono 3D | 1-32 |
| Gunshot | Buffered mono 3D | 2-96 |
| Campfire | Streamed mono 3D | 2-48 |
| Wind, birds, underwater | Streamed stereo 2D | Not applicable |

Spatial stereo assets are downmixed to mono during load and reported
diagnostically. Listener position, orientation, and velocity come from the
rendered camera/local player once per frame. Coordinate conversion between the
game's Y-up/+Z-forward convention and miniaudio is centralized and covered by
left/right orientation tests. Attenuation is inverse-distance and Doppler
defaults to 1.0.

## Ambience

`AmbienceController` samples the world at 5 Hz and crossfades layers over one
second:

- wind follows smoothed outdoor/direct-skylight exposure;
- birds additionally require daylight;
- underwater follows the listener's current voxel material; and
- at most the four nearest campfires inside 48 units are active.

Rain cues are registered in the bank but remain silent until gameplay exposes a
weather-intensity signal. World-scoped streams fade out on state transitions;
the audio device remains alive for the client process.

The ten long ambience loops are committed as lossless FLAC files. Short effects
remain WAV. When replacing a loop, verify decoded PCM, duration, sample rate,
channel count, and loop boundary before removing the source WAV.

## Build and test

The managed project invokes CMake incrementally and places
`VoxelAudioNative.dll` in build and publish output:

```powershell
dotnet build Voxelgine.Audio/Voxelgine.Audio.csproj -c Release -p:Platform=x64
dotnet test Voxelgine.Audio.Tests/Voxelgine.Audio.Tests.csproj -c Release `
  -p:Platform=x64
```

To configure the C library directly:

```powershell
cmake -S native/VoxelAudioNative -B out/VoxelAudioNative -A x64 `
  -DMINIAUDIO_ROOT="$PWD/thirdparty/miniaudio"
cmake --build out/VoxelAudioNative --config Release `
  --target VoxelAudioNative --parallel
```
