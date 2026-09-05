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
players/identities/<fingerprint>.bin
players/legacy-<timestamp>-<unique-id>/*.bin
client-identity.pk8          (client root)
trusted-servers.json         (client root)
server-identity.pk8          (server root)
server-access.json          (server root)
logs/console.log
```

`scripts/Test-All.ps1` creates a distinct temporary runtime root under its
ignored validation artifact directory for every automatic smoke mode. Tests do
not reuse normal developer saves.

## Identity, profile backups, and trust

The client signing key is a P-256 PKCS#8 private key. Keep a private backup of
`client-identity.pk8`; losing it creates a new identity and a fresh server profile.
Do not share it. There is no account recovery service. Server profiles use the
SHA-256 public-key fingerprint, so changing a display name preserves ownership.

On server startup, old name-based `.bin` player files directly under `players`
move to a unique `players/legacy-*` backup directory. Identity profiles start
fresh. The server never claims a legacy profile by name and provides no import
command. Invalid profile files are quarantined as `.invalid-*.bak` files.
Existing compatible world archives remain intact; the world archive format is 6.

Back up the server's `server-identity.pk8`, `server-access.json`, `worlds`, and
`players/identities` together. Administrator and ban entries contain public
fingerprints. The hosted player is authorized by client application composition.
A dedicated server starts without an administrator until its console assigns one.

`trusted-servers.json` pins a signing fingerprint for each normalized
`address:port` after the first authenticated connection. If that server key
changes, connection fails with a trust-reset message. After verifying that the
change is expected, explicitly reset the same address used to connect:

```powershell
Voxelgine.exe --data-root C:/AuroraClient --reset-server-trust localhost:7777
```

The option removes that pin before normal application startup. The next successful
connection records the new key. Aliases such as `localhost` and `127.0.0.1` are
separate pin entries. Private keys and session secrets must not be logged or
included in shared validation artifacts.
