# Runtime data

Aurora Falls separates immutable shipped assets from mutable runtime state.
The executable directory contains the `data` asset tree and
`config.defaults.json`. The game never writes configuration, worlds, player
records, or logs beside the executable.

The client default root is the platform local-application-data directory plus
`AuroraFalls`. A dedicated server uses `AuroraFallsServer`. A hosted server
uses the client's `hosted-server` subdirectory. If the platform does not expose
a local-application-data directory, the resolver tries
`$HOME/.local/share`; its final fallback is `.runtime` below the working
directory and produces a warning.

Both executables accept `--data-root <path>`. This override is authoritative
and is useful for development, portable server instances, and isolated smoke
tests. Runtime roots contain:

```text
config.json
worlds/map.bin
players/*.bin
logs/console.log
```

`scripts/Test-All.ps1` creates a distinct temporary runtime root under its
ignored validation artifact directory for every automatic smoke mode. Tests do
not reuse normal developer saves.
