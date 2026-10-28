# Better Project

An alternative Project window for the Unity Editor (Unity 6000.2+).

## Install

Unity Package Manager → **+** → **Add package from git URL...** and paste:

```
https://github.com/<user>/BetterProject.git?path=Packages/com.emeraldpowder.betterproject
```

A specific version: append `#v0.1.0` (a git tag) to the URL.

Or add to `Packages/manifest.json`:

```json
"com.emeraldpowder.betterproject": "https://github.com/<user>/BetterProject.git?path=Packages/com.emeraldpowder.betterproject"
```

## Repository layout

- `Packages/com.emeraldpowder.betterproject` — the package (embedded in this Unity project for development).
- `BetterProjectTests` — dotnet test project: runs Core tests without Unity (`dotnet test BetterProjectTests`).
- `docs` — design docs.
