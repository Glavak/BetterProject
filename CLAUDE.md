# Better Project

Unity Editor plugin: an alternative Project window (favourites, recents, new assets, breadcrumbs). Distributed as a UPM
package from this git repo. Unity 6000.2+ (dev project on 6000.2.15f1).

## Layout

- `Packages/com.emeraldpowder.betterproject/` — **the package**. All plugin work happens here. Embedded in this Unity
  project for development; users install it via
  `https://github.com/<user>/BetterProject.git?path=Packages/com.emeraldpowder.betterproject`.
  - `Core/` — `BetterProject.Core` asmdef, `noEngineReferences: true`. Pure C# logic (lists, JSON, selection, grid
    navigation, path helpers). Must not reference `UnityEngine` / `UnityEditor`: it is also compiled by the dotnet
    test project.
  - `Editor/` — `BetterProject.Editor` asmdef (Editor only). UI Toolkit window and stores. Entry point
    `BetterProjectWindow` (menu `Window/General/Better Project`), settings page `Project/Better Project`.
  - `Editor/Internal/UnityInternals.cs` — the **only** place allowed to reflect into UnityEditor internals. Every member
    fails gracefully (returns false / null, at most one warning per session), never throws.
  - `Tests/Editor/` — NUnit tests for Core (`BetterProject.Core.Tests`, `UNITY_INCLUDE_TESTS`).
- `BetterProjectTests/` — dotnet project that compiles `Core/` + `Tests/Editor/` directly, so Core tests run without Unity.
- `Assets/`, `ProjectSettings/` — throwaway dev project, not part of the package.
- `docs/` — GDD, design spec, implementation plan. Written when the plugin lived in `Assets/BetterProject` of another
  project; paths there are historical. Design decisions and anti-goals in the spec still apply.

## Commands

```sh
dotnet test BetterProjectTests     # Core tests, no Unity needed (~111 tests)
```

Editor code can only be checked in Unity: focus the editor, check the console for compile errors. Core tests also run
in Unity's Test Runner (package is listed in `testables` in `Packages/manifest.json`).

## Rules

- Every file and folder in the package needs a committed `.meta` (git-installed packages are immutable; Unity can't
  generate them). When adding a file outside Unity, create its `.meta` with a fresh GUID. Never change existing GUIDs.
- New code that needs only plain logic goes in `Core/` with tests; keep `Editor/` thin.
- Don't hardcode the package path. Find package assets by name (see `Theme.StyleSheet`), the install location varies.
- Data files in the consuming project:
  - `ProjectSettings/BetterProjectSettings.asset`, `ProjectSettings/BetterProjectFavourites.json` — shared, committed.
    Writes must be byte-stable (no diff noise), never overwrite an unreadable file.
  - `UserSettings/BetterProjectRecents.json`, `UserSettings/BetterProjectNewAssets.json` — personal.
  - In this dev repo the `ProjectSettings` ones are gitignored.
- Favourites / recents reference assets by GUID + local id (`AssetRef`), so they survive rename / move.
- Performance matters: must not be noticeably slower than the default Project window on thousands of assets. Don't
  load assets to build lists.

## Releasing

1. Bump `version` in `Packages/com.emeraldpowder.betterproject/package.json`, add a `CHANGELOG.md` entry.
2. Commit, tag `vX.Y.Z`, push with tags. Users pin a version with `#vX.Y.Z` at the end of the git URL.

## Commits

Subject style: `Better Project: <what changed>`.
