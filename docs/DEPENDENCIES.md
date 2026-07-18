# Dependency pins and upstream workflow

FishGfx and miniaudio are Git submodules. FishUI is a nested submodule owned by
the FishGfx checkout. A clone is complete only when all three paths have a
checked-out commit and `git submodule status --recursive` has no line beginning
with `-` or `+`.

## Pinned revisions

| Dependency | Path | Revision |
|---|---|---|
| FishGfx | `thirdparty/FishGfx` | `d182514d36718f82c0f55db27516c655f5f4a78f` |
| FishUI | `thirdparty/FishGfx/thirdparty/FishUI` | `2cd957596ec786852a0c1b2a51f4b47f864d046d` |
| miniaudio | `thirdparty/miniaudio` | `9634bedb5b5a2ca38c1ee7108a9358a4e233f14d` |

The FishGfx migration was seeded from
`e4aab38a4d5ee4389bcebe94dcbd2c17cf4467d7`. Its parent gitlink advances to
`723688c` only after the reusable window, monitor, VSync, cursor, logical-size,
framebuffer-size, and high-DPI work was committed and pushed upstream, then to
`4b9ff35` after the FishUI high-DPI scissor fix and regression test were pushed,
then to `d182514` after runtime voxel-atlas replacement was added for queued
game-asset hot reload.

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
