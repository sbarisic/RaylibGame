# Dependency pins and upstream workflow

FishGfx and miniaudio are Git submodules. FishUI is a nested submodule owned by
the FishGfx checkout. A clone is complete only when all three paths have a
checked-out commit and `git submodule status --recursive` has no line beginning
with `-` or `+`.

## Pinned revisions

| Dependency | Path | Revision |
|---|---|---|
| FishGfx | `thirdparty/FishGfx` | `f8f7f9afe994d41e27d6f551b79116bf134bb9e9` |
| FishUI | `thirdparty/FishGfx/thirdparty/FishUI` | `18285683769e38ac0842af1f63efc9d033b5bacd` |
| miniaudio | `thirdparty/miniaudio` | `9634bedb5b5a2ca38c1ee7108a9358a4e233f14d` |

The table matches the checked-in gitlinks and `scripts/Test-SubmodulePins.ps1`.
Earlier migration revisions are historical and are not current dependency pins.

The miniaudio revision is the locked `0.11.25` migration revision. The parent
gitlink, rather than a branch name or a moving tag, is authoritative.

## Clone and restore

```powershell
git clone --recurse-submodules https://github.com/sbarisic/RaylibGame.git
Set-Location RaylibGame
git submodule status --recursive
```

To repair an existing non-recursive clone:

```powershell
git submodule sync --recursive
git submodule update --init --recursive
```

To verify the migration pins explicitly:

```powershell
pwsh -NoProfile -File scripts/Test-SubmodulePins.ps1
```

Never configure branch tracking and never run `git submodule update --remote`.
Those operations replace a reviewed dependency revision with mutable upstream
state.

## Updating dependencies

Reusable renderer or windowing changes belong in `sbarisic/FishGfx`.
Game-specific adapters, the render graph, assets, and gameplay integration stay
in RaylibGame. Generated files under `FishGfx/Glfw3` must not be edited; add a
handwritten binding extension when an extra GLFW call is needed.

When a dependency change is required:

1. Commit and push FishUI first if the nested dependency changed.
2. Commit and push FishGfx, including its updated FishUI gitlink.
3. In RaylibGame, stage the updated `thirdparty/FishGfx` gitlink.
4. Build `thirdparty/FishGfx/FishGfx.Modern.sln` and RaylibGame from a recursive
   checkout before pushing the parent change.

Inspect the exact revisions before staging the parent:

```powershell
git -C thirdparty/FishGfx rev-parse HEAD
git -C thirdparty/FishGfx/thirdparty/FishUI rev-parse HEAD
git -C thirdparty/miniaudio rev-parse HEAD
git diff --submodule=log -- thirdparty/FishGfx thirdparty/miniaudio
```

Do not add duplicate parent-side copy rules for `glfw3.dll`, FishGfx shaders, or
FishUI data. Those assets must flow through project references so downstream
build and publish behavior is exercised exactly as consumers use it.

The identity transport uses platform .NET cryptography and adds no NuGet or native
cryptography dependency. The subsequent graphics/UI hardening updates the two reviewed submodule revisions above and preserves upstream FishUI CI removal.

The graphics/UI API migration uses Unicode scalar input and glyph metrics, atomic font atlases, bounded process output and per-context state caches. Active consumers compile through the updated project references; see [the dependency report](../thirdparty/FishGfx/HARDENING.md) for measurements and [the UI contract](../thirdparty/FishGfx/thirdparty/FishUI/docs/HARDENING.md) for custom-control overrides.
