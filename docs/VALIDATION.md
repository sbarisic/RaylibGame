# Validation

Run the complete local gate from the repository root:

```powershell
.\scripts\Test-All.ps1 -Configuration Debug -CleanRuntimeData -IncludeFishGfx
```

The script uses an isolated runtime-data root and stores logs and publishes under
`artifacts/validation/<timestamp>/`. It records the tracked contents of the root
repository and every recursive submodule before and after validation. Existing
developer edits are allowed, but validation fails if their contents change.

The accepted test baseline when this gate was introduced is:

- Engine: 241 tests.
- Client: 155 tests.
- Audio: 29 tests.
- FishGfx modern solution: 430 tests.

The Phase 1-3B implementation adds architecture and lifecycle coverage. Its
current passing totals are 244 engine, 161 client, 29 audio, and 430 FishGfx
tests.

Performance captures use Release/x64 at 1920x1080 with VSync disabled, 4x MSAA,
a 160-block draw distance, Medium shadows, and Medium fog. Each stationary,
rotation, resident movement, streaming movement, and local-fog scenario uses a
30-second warm-up followed by five 60-second captures. Median, p95, p99, queue
sizes, allocations, persistence timings, and connect-to-playable time are kept
with the validation artifacts; no unmeasured numbers are synthesized here.
