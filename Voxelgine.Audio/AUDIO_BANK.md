# Audio cue bank

`AudioCueBank.LoadDefault()` reads `data/audio/audio-bank.json` relative to the
application output directory. Version 1 uses the schema in
`audio-bank.schema.json`. Variant paths are relative to the bank file, so the
game bank can refer to existing effects as `../sound/walk/walk1.wav`.

```json
{
  "version": 1,
  "cues": [
    {
      "id": "player.walk",
      "variants": [
        {
          "path": "../sound/walk/walk1.wav",
          "gain": 0.65,
          "pitchMin": 0.96,
          "pitchMax": 1.04
        }
      ],
      "bus": "Sfx",
      "gain": 1.0,
      "spatialMode": "ThreeDimensional",
      "minDistance": 1.0,
      "maxDistance": 24.0,
      "dopplerFactor": 1.0,
      "looping": false,
      "streamed": false,
      "maxInstancesPerVariant": 6
    },
    {
      "id": "ambience.wind",
      "variants": [
        { "path": "../sound/wind/wind_loop.flac" }
      ],
      "bus": "Ambience",
      "spatialMode": "TwoDimensional",
      "looping": true,
      "streamed": true,
      "maxInstancesPerVariant": 1
    }
  ]
}
```

Short effects should remain WAV and use `streamed: false`; long ambience loops
should be FLAC and use `streamed: true`. Three-dimensional files with more than
one source channel are downmixed to mono before miniaudio spatialization and
reported once through the configured diagnostic logger.
