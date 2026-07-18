# VoxelAudioNative

This DLL is the deliberately narrow native boundary between the game and
miniaudio. Configure it from the repository root with:

```powershell
cmake -S native/VoxelAudioNative -B out/VoxelAudioNative -A x64 `
  -DMINIAUDIO_ROOT="$PWD/thirdparty/miniaudio"
cmake --build out/VoxelAudioNative --config Release --target VoxelAudioNative
```

`Voxelgine.Audio.csproj` runs those steps incrementally and copies
`VoxelAudioNative.dll` to build and publish outputs. The C ABI exports only
versioned VoxelAudioNative POD values and opaque pointers. No `ma_*` type crosses
the DLL boundary, because miniaudio does not promise ABI stability for its C
structures.

Decoded clips own immutable PCM and each playing voice owns an independent
cursor into that PCM. File streams use miniaudio resource-manager data sources;
a second mono-decoding resource manager handles streamed 3D sources without
affecting stereo 2D ambience. Voice handles combine a slot index and generation,
so a stopped voice can never control a later occupant of the same slot.

The `va_source_processor` field is the source-processing insertion seam. The
current `VA_SOURCE_PROCESSOR_BASIC` route uses miniaudio's inverse-distance 3D
spatializer. `VA_SOURCE_PROCESSOR_STEAM_AUDIO` is reserved and rejected until an
optional dry-mono Steam Audio node is implemented. That future node belongs
between source decode and the destination bus and must disable miniaudio's basic
spatializer for that source; the DLL intentionally has no `phonon.dll` link or
runtime dependency today.

The real-time mixer never calls managed code. Completion, underrun, diagnostic,
and statistics state is polled by `AudioSystem.Update` on the game thread.
