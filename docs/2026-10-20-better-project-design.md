# Better Project — Design

Source: `docs/GDD BetterProject.md`.

## Intent

The user works on 2–3 prefabs/assets at a time and keeps clicking back and forth between them in the
default Project window. Better Project is an alternative Project window whose real payoff is
**Favourites** and **Recents**: one click to get back to the asset you were just working on.
V1 rebuilds the Project window so it can fully replace the default one day-to-day; V2 adds the
features that solve the pain (breadcrumbs, favourites, recents).

## Goals

- A dockable editor window that can be used *instead of* the default Project window for daily work.
- Shared favourites (team-wide, committed to git) and personal recents.
- Shipped later as a standalone package: self-contained folder, own asmdef, no dependency on project code.

## Non-goals

- Filter dropdowns (by type / by label) and the saved-search ("favourite selector") UI. Typed search syntax
  (`t:Prefab`, `l:Label`) still works because it comes from `AssetDatabase.FindAssets`.
- Dropping files from Finder/Explorer into the window to import them (V1 and V2). Builder may add it if trivial.
- Bottom status bar showing the selected asset's path.
- Large preview on hover; the Inspector preview pane is Unity's, untouched.
- Replacing or modifying Unity's built-in Project window.

## Anti-goals (failure even if it "works")

- Noticeably slower than the default window in a project with thousands of assets (folder switch, scroll, search).
- Any move / rename / delete that does something different from what the user saw (wrong target folder, wrong asset).
- Favourites file that produces noisy git diffs (reordering, rewrites with no change).
- Favourites/recents that break when an asset is renamed or moved.

## Decisions

Theirs (from the user):
- Favourites are **shared**: stored in a file committed to the repo.
- Single click on an item (anywhere: main list, favourites, recents) selects it → Inspector shows it.
  Double click opens it (`AssetDatabase.OpenAsset`) → prefabs open in Prefab Mode.
- V1 must support: drag out to Scene / Hierarchy / Inspector object fields, drag-to-move between folders,
  right-click context menu with Create/…, multi-select, keyboard navigation, search, rename, thumbnails.
- "Previews" = thumbnails like the default window (rendered previews in icon mode), nothing more.

Mine (builder may revisit, flag if changed):
- Built from scratch. Unity's `ProjectBrowser` is `internal`, so it cannot be inherited.
- UI Toolkit (`EditorWindow.CreateGUI`, `TreeView`, `ListView`; custom virtualized grid for icon mode).
  IMGUI is acceptable for individual pieces only if UI Toolkit can't do them.
- Minimum Unity: 6000.2 (project is on 6000.2.15f1).
- Recents are **personal**: stored under `UserSettings/` (not committed).
- Reflection into `UnityEditor` internals is allowed only where no public API exists, isolated in one
  class (`Internal/UnityInternals.cs` or similar), and must fail gracefully (feature degrades, no exceptions
  spamming the console) if the internal member is missing.
- Several instances of the window may be open at once, like the default window.

## Package layout

```
Assets/BetterProject/
  package.json                     name "com.hypercell.betterproject" (my call), displayName "Better Project"
  Editor/
    BetterProject.Editor.asmdef    Editor platform only, root namespace BetterProject
    ...
  Tests/Editor/
    BetterProject.Editor.Tests.asmdef
```

Nothing outside `Assets/BetterProject/` may reference it, and it may not reference project code
(`VectorTerrain` etc.). Menu item: `Window/General/Better Project`.

## V1 — Project window clone

### Layout

Toolbar on top (search field), then two panels with a draggable splitter, then a bottom bar with the
icon-size slider. Splitter position, slider value, current folder, and tree expand state persist per window
(serialized fields on the window, survive domain reload and layout save).

### Left panel — folder tree

- Two roots: **Assets** and **Packages**. Assets shows the folder hierarchy. Packages lists registered packages
  (display name, like the default window) with their folders below.
- Folders only (no files), matching the default two-column mode.
- Click a folder → right panel shows its contents. Expand/collapse arrows; Alt-click expands recursively.
- Folders are drop targets for drag-to-move.

### Right panel — folder contents

- Folders first, then assets, alphabetical (case-insensitive, same as default).
- Assets with sub-assets (models, sprite sheets, prefabs with nested objects shown by default) get an expand
  arrow that reveals sub-assets inline, as in the default window. Required because V2 favourites accept sub-assets.
- **List mode** (slider at minimum): one row per item, small icon + name.
- **Icon mode** (slider above minimum): grid, tile size from slider, name under thumbnail.
- Thumbnails: `AssetPreview.GetAssetPreview` (async; repaint as they arrive), fallback `AssetDatabase.GetCachedIcon`
  / `AssetPreview.GetMiniThumbnail`. Must stay virtualized — only visible items request previews.
- Double click a folder → navigate into it. Double click an asset → `AssetDatabase.OpenAsset`.

### Search

- Non-empty search field → right panel shows a flat result list from `AssetDatabase.FindAssets(query)`, scoped to
  `Assets` (default window's default scope). Clearing the field returns to the current folder.
- Search updates as you type, debounced; Esc clears.

### Selection & keyboard

- Selection drives `Selection.objects`. Ctrl/Cmd-click toggles, Shift-click selects a range.
- If `Selection` changes from outside (Hierarchy, Inspector) the window updates its highlight for visible items
  but does not navigate.
- Keys: arrows move selection (grid-aware in icon mode), Enter opens, Backspace/Left goes to parent folder in list
  context where sensible, F2 renames, Delete / Cmd+Backspace deletes (with confirmation, `AssetDatabase.MoveAssetsToTrash`),
  Cmd/Ctrl+D duplicates. Exact key behavior should mirror the default window on each OS.

### Rename

F2, context-menu Rename, or slow second click on an already-selected item → inline text field.
Enter commits via `AssetDatabase.RenameAsset`, Esc cancels. Error from `RenameAsset` is shown (console warning
is fine) and the name reverts.

### Context menu

Right-click on an item or empty space shows Unity's own **Assets** menu (`EditorUtility.DisplayPopupMenu` with
`"Assets/"`), so Create/…, Show in Finder, Reimport, third-party Assets/ menu items, etc. all appear for free.

**Known risk:** Create/… and some commands resolve the target folder via `ProjectWindowUtil`, which reads the
*default* Project window's active folder, and start inline rename there. Requirement: assets created from this
window land in **this window's current folder** and enter inline rename **in this window**. How (reflection to set
the active folder, intercepting `ProjectWindowUtil` / `EndNameEditAction`, or re-implementing Create) is the
builder's call; spike it early.

### Drag and drop

- Drag out: `DragAndDrop.PrepareStartDrag` with `objectReferences` and `paths` → works into Scene, Hierarchy,
  Inspector fields, and other editor windows.
- Drag onto a folder (either panel) → move with `AssetDatabase.MoveAsset`; show drop-target highlight; refuse
  moving a folder into itself/descendant.
- Drag in from Hierarchy (GameObject → folder) creates a prefab, as in the default window — builder's call
  whether V1 or later; not requested.

### Refresh

Rebuild affected views on `EditorApplication.projectChanged`. Keep selection and scroll position when possible.

### Known limitation (accepted)

`EditorGUIUtility.PingObject` and "Show in Project" from other windows target the default Project window only.
Not worked around in V1/V2.

## V2

### Left panel sections

Top to bottom: **Favourites**, **Recents**, **Assets**, **Packages**. Each section collapsible.

Clicking an item in Favourites/Recents follows the same rules as everywhere: single click selects
(folder favourite → right panel shows that folder), double click opens.

### 1. Breadcrumbs bar

Above the right panel: `Assets ▸ Art ▸ Props`. Click a folder name → navigate there. Click an arrow `▸` after a
folder → dropdown menu listing that folder's direct subfolders; choosing one navigates there. Hidden (or shows
"Search results") while searching — builder's call.

### 2. Favourites

- Drop folders, assets, or sub-assets (from this window or the default Project window) onto the Favourites
  section to add. Duplicates are ignored.
- Right-click a favourite → **Remove from Favourites**.
- Folder favourites expand to show their contents (sub-folders **and** assets, my call) with the usual
  click/double-click/drag behavior.
- Order: order added; drag-reorder within the section is nice-to-have.
- Storage: `ProjectSettings/BetterProjectFavourites.json` (my call), committed. Each entry is GUID + local file id
  (for sub-assets), so moves and renames don't break it. Written only when the list changes; stable formatting.
- Each entry also stores a `name` captured when it was added (my call). It is **never rewritten** afterwards, so
  renames/moves don't produce diffs; when the asset exists, the live name is displayed instead.
- Missing entries (asset deleted, or not present on this machine) are shown greyed out with the stored name, not
  silently dropped — another teammate may still need them. They can be removed via the context menu.
- File changes from git (pull) are picked up without restarting the editor.

### 3. Recents

- Added when (user decision):
  - a prefab is opened in Prefab Mode (`PrefabStage.prefabStageOpened`);
  - a scene is opened (`EditorSceneManager.sceneOpened`);
  - the user edits an asset in the Inspector. Only user-driven edits count — detect via Undo
    (`Undo.postprocessModifications` / undo records whose target is a persistent asset), so changes made by scripts
    and editor tools (e.g. VectorTerrain, chunk spawners), imports, and Prefab Mode auto-save do **not** add.
    Import-settings edits made by the user in the Inspector count (they go through Undo).
- Does **not** add: selection alone; editing GameObjects in an open scene (the `.unity` file is not added).
- Most recent first. Re-adding moves the item to the top. Limit **10**.
- Sub-assets: entry points to the main asset (my call; avoids flooding recents with sub-objects).
- Storage: `UserSettings/BetterProjectRecents.json`, GUID-based. Deleted assets are dropped.
- Right-click → Remove from Recents, and **Clear Recents** on the section header (my call).

## Testing

EditMode tests for the parts with logic: favourites/recents storage (serialize, GUID resolution, dedupe, limit,
ordering, favourites file unchanged on rename/move), recents trigger filtering (user Inspector edit counts;
script-made asset change does not), search result building, move-target validation. UI is verified manually in
the editor; each phase ends with a manual checklist in the plan.

## Left to the builder

- Internal class structure, view/model split, how the grid is virtualized.
- Exact visual styling (aim: indistinguishable at a glance from the default window, light and dark skin).
- Mechanism for the Create/… active-folder problem (see Known risk).
- Whether to persist per-window state via serialized fields or `SessionState`/`EditorPrefs`.
