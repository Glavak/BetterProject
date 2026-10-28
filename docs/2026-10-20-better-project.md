# Better Project Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A dockable editor window that replaces Unity's Project window for daily work (V1), plus breadcrumbs,
shared Favourites and personal Recents (V2).

**Architecture:** Two assemblies in one package folder. `BetterProject.Core` (`noEngineReferences: true`) holds all
logic that can be pure: asset references, favourites/recents lists, their JSON format, path rules, grid navigation,
selection. It is unit-tested with `dotnet test` (same pattern as `CommonTests`). `BetterProject.Editor` holds the
UI Toolkit window and all `UnityEditor` calls; it is verified by compile checks and manual checklists in the Editor.
All Unity-internal reflection lives in one class, `UnityInternals`.

**Tech Stack:**
- Unity 6000.2.15f1, UI Toolkit (`EditorWindow.CreateGUI`, `TreeView`, `ListView`, `TwoPaneSplitView`, `ToolbarSearchField`)
- C# 9 in Core, NUnit 3 via `dotnet test BetterProjectTests` (SDK 8)

**Spec:** `docs/superpowers/specs/2026-10-20-better-project-design.md`

## Global Constraints

- Everything lives under `Assets/BetterProject/`. Nothing outside it references it; it references no project code (`VectorTerrain` etc.).
- `Assets/BetterProject/package.json`: `"name": "com.emeraldpowder.betterproject"`, `"displayName": "Better Project"`, `"unity": "6000.2"`.
- Namespaces: `BetterProject.Core`, `BetterProject.Editor`. Editor asmdef is Editor platform only.
- Menu item: `Window/General/Better Project`. Multiple window instances allowed.
- Favourites file: `ProjectSettings/BetterProjectFavourites.json` (committed). Recents file: `UserSettings/BetterProjectRecents.json` (not committed).
- Recents limit: **10**. Most recent first; re-adding moves to top.
- Single click selects (`Selection.objects`); double click opens (`AssetDatabase.OpenAsset`); double click on a folder navigates into it. Same rules in main list, Favourites and Recents.
- Reflection into `UnityEditor` internals only in `Editor/Internal/UnityInternals.cs`; every member returns a failure value instead of throwing and logs at most one warning per session.
- Search scope: `Assets` only, via `AssetDatabase.FindAssets(query, new[] { "Assets" })`.
- Not in scope: filter dropdowns, saved searches, Finder file drop import, bottom path bar, hover preview, redirecting Ping/"Show in Project".

## Review Focus

1. **Sub-asset local file IDs are 64-bit and can be negative or > 2^53.** Stored as JSON strings, round-trip exact. Test: Task 2 `Favourites_RoundTrip_LargeAndNegativeLocalId`.
2. **Favourites file broken by a git merge (conflict markers) or hand edit.** Reading throws `FormatException`; the editor shows a warning in the section header, and never overwrites the file until it reads cleanly again. Test: Task 2 `ReadFavourites_ConflictMarkers_Throws`; Task 11 manual check.
3. **Favourite names with quotes, backslashes, unicode, control chars.** Escaped on write, identical after read. Test: Task 2 `Favourites_RoundTrip_EscapesName`.
4. **Moving a folder into itself, its descendant, its current parent, or a case-variant path of itself.** Refused with no `MoveAsset` call. Test: Task 3 `CanMoveInto_*`.
5. **Recents/favourites asset deleted or moved.** Moved: still resolves (GUID). Deleted: recents drop it, favourites show it greyed. Test: Task 2 `Recents_RemoveWhere_DropsUnresolved`; Task 11/12 manual checks.

---

## File Structure

```
Assets/BetterProject/
  package.json
  Core/
    BetterProject.Core.asmdef          noEngineReferences: true
    AssetRef.cs                        guid + localId value type
    FavouritesList.cs                  FavouriteEntry, FavouritesList
    RecentsList.cs
    RefListJson.cs                     read/write both files (stable format)
    MiniJson.cs                        tiny JSON reader used by RefListJson (internal)
    AssetPaths.cs                      parent, breadcrumbs, move rules, project-path test
    GridNav.cs                         NavDir, arrow-key index math
    SelectionModel.cs                  click / ctrl / shift selection over ids
    RecentsFilter.cs                   which Undo edits count for recents
  Editor/
    BetterProject.Editor.asmdef        Editor only, references BetterProject.Core
    BetterProjectWindow.cs             window, layout, persisted state, menu item
    FolderTree.cs                      left panel (sections + Assets/Packages trees)
    ContentView.cs                     right panel: items, list + grid, sub-asset expansion
    ContentItem.cs                     row model
    Thumbnails.cs                      AssetPreview requests + repaint polling
    InlineRename.cs
    ItemDrag.cs                        drag out, drop-to-move
    AssetsContextMenu.cs               "Assets/" popup + create-in-this-folder
    SearchController.cs
    Breadcrumbs.cs
    PackageFolders.cs                  registered packages → display names
    AssetRefUtil.cs                    Object/path ↔ AssetRef
    FavouritesStore.cs                 load/save/watch favourites file
    RecentsStore.cs                    load/save recents file
    RecentsTracker.cs                  [InitializeOnLoad] triggers
    Internal/UnityInternals.cs
  Tests/Editor/
    BetterProject.Core.Tests.asmdef    Unity Test Framework, editor only, refs Core
    *Tests.cs                          plain NUnit, no Unity APIs
BetterProjectTests/
  BetterProjectTests.csproj            compiles Core/*.cs + Tests/Editor/*.cs for dotnet test
```

The test `.cs` files live inside the package (so they ship with it and run in Unity's Test Runner) and are also
compiled by `BetterProjectTests.csproj` for fast CLI runs. They must use no Unity APIs.

---

### Task 1: Package scaffold, `AssetRef`, test project

**Files:**
- Create: `Assets/BetterProject/package.json`, `Assets/BetterProject/Core/BetterProject.Core.asmdef`, `Assets/BetterProject/Core/AssetRef.cs`
- Create: `Assets/BetterProject/Tests/Editor/BetterProject.Core.Tests.asmdef`, `Assets/BetterProject/Tests/Editor/AssetRefTests.cs`
- Create: `BetterProjectTests/BetterProjectTests.csproj`
- Modify: `.gitignore` (add `BetterProjectTests/bin/`, `BetterProjectTests/obj/`, `!BetterProjectTests/BetterProjectTests.csproj` next to the CommonTests lines)

**Interfaces:**
- Produces: `readonly struct AssetRef : IEquatable<AssetRef>` with `AssetRef(string guid, long localId = 0)`, `string Guid`, `long LocalId`, `bool IsSubAsset` (`LocalId != 0`), value equality, `ToString()` → `"<guid>:<localId>"`.
  `LocalId == 0` means the main asset or a folder.

- [ ] **Step 1: Scaffold**

`BetterProject.Core.asmdef`: name `BetterProject.Core`, rootNamespace `BetterProject.Core`, `noEngineReferences: true`, `autoReferenced: false`.
Test asmdef: name `BetterProject.Core.Tests`, references `BetterProject.Core`, `UnityEngine.TestRunner`, `UnityEditor.TestRunner`; `includePlatforms: ["Editor"]`; `precompiledReferences: ["nunit.framework.dll"]`; `overrideReferences: true`; `defineConstraints: ["UNITY_INCLUDE_TESTS"]`; `autoReferenced: false`.
`BetterProjectTests.csproj`: copy `CommonTests/CommonTests.csproj`, change `AssemblyName`/`RootNamespace` to `BetterProjectTests` / `BetterProject.Core.Tests`, and compile includes to `../Assets/BetterProject/Core/**/*.cs` and `../Assets/BetterProject/Tests/Editor/**/*.cs`.

- [ ] **Step 2: Write failing tests** in `AssetRefTests.cs`

```csharp
[Test] public void Ctor_AcceptsLowercaseHexGuid() => Assert.AreEqual("0123456789abcdef0123456789abcdef", new AssetRef("0123456789abcdef0123456789abcdef").Guid);
[Test] public void Ctor_RejectsBadGuid([Values(null, "", "xyz", "0123456789ABCDEF0123456789ABCDEF", "0123")] string g) => Assert.Throws<ArgumentException>(() => new AssetRef(g));
[Test] public void Equality_ByGuidAndLocalId() {
    var g = "0123456789abcdef0123456789abcdef";
    Assert.AreEqual(new AssetRef(g, 5), new AssetRef(g, 5));
    Assert.AreNotEqual(new AssetRef(g, 5), new AssetRef(g, 0));
}
[Test] public void IsSubAsset_WhenLocalIdNonZero() { Assert.IsFalse(new AssetRef(G).IsSubAsset); Assert.IsTrue(new AssetRef(G, -7).IsSubAsset); }
```

- [ ] **Step 3: Run** `dotnet test BetterProjectTests` — Expected: FAIL (`AssetRef` not defined).
- [ ] **Step 4: Implement `AssetRef`** in `Core/AssetRef.cs`. Guid must be exactly 32 chars `[0-9a-f]`.
- [ ] **Step 5: Run** `dotnet test BetterProjectTests` — Expected: all PASS.
- [ ] **Step 6: Commit** (no `.meta` files yet; Task 4 stages them)

```bash
git add .gitignore BetterProjectTests/BetterProjectTests.csproj Assets/BetterProject/package.json Assets/BetterProject/Core Assets/BetterProject/Tests
git commit -m "Better Project 1: package scaffold, AssetRef, dotnet test project"
```

---

### Task 2: Favourites & recents lists and their JSON format

**Files:**
- Create: `Core/FavouritesList.cs`, `Core/RecentsList.cs`, `Core/RefListJson.cs`, `Core/MiniJson.cs`
- Test: `Tests/Editor/FavouritesListTests.cs`, `Tests/Editor/RecentsListTests.cs`, `Tests/Editor/RefListJsonTests.cs`

**Interfaces:**
- Consumes: `AssetRef`.
- Produces:
  - `sealed class FavouriteEntry { AssetRef Ref { get; } string Name { get; } }` (ctor `(AssetRef, string name)`; null name → `""`).
  - `sealed class FavouritesList { IReadOnlyList<FavouriteEntry> Items; bool Add(AssetRef r, string name); bool Remove(AssetRef r); }` — `Add` appends, returns false for a duplicate `Ref`; `Remove` returns false if absent.
  - `sealed class RecentsList { const int Limit = 10; IReadOnlyList<AssetRef> Items; bool Push(AssetRef r); bool Remove(AssetRef r); bool Clear(); int RemoveWhere(Func<AssetRef, bool> predicate); }` — each returns whether the list changed (`RemoveWhere` returns count removed).
  - `static class RefListJson { string WriteFavourites(FavouritesList); FavouritesList ReadFavourites(string json); string WriteRecents(RecentsList); RecentsList ReadRecents(string json); }` — readers throw `FormatException` on any malformed input, wrong/missing `version`, bad guid, or non-integer `localId`.

Exact file format (LF line endings, 2-space indent, one entry per line, trailing newline; `localId` is a **string**):

```json
{
  "version": 1,
  "favourites": [
    { "guid": "0123456789abcdef0123456789abcdef", "localId": "0", "name": "Player.prefab" }
  ]
}
```

Recents is the same with key `"recents"` and entries `{ "guid": "...", "localId": "0" }`. Empty list → `"favourites": []`.
Names escape `"`, `\`, and chars < 0x20 (`\n`, `\t`, others as `\u00XX`); other unicode written as-is.

- [ ] **Step 1: Write failing tests**

```csharp
// FavouritesListTests
[Test] public void Add_AppendsInOrder()            // Add A, B → Items refs == [A, B]
[Test] public void Add_Duplicate_ReturnsFalse()    // Add A twice → second false, Count 1
[Test] public void Remove_Absent_ReturnsFalse()

// RecentsListTests
[Test] public void Push_NewItemGoesFirst()         // Push A, B → [B, A]
[Test] public void Push_Existing_MovesToTop()      // Push A, B, A → [A, B]
[Test] public void Push_AlreadyTop_ReturnsFalse()
[Test] public void Push_Eleventh_DropsOldest()     // Push r0..r10 → Count 10, Items[0]==r10, !Contains(r0)
[Test] public void Recents_RemoveWhere_DropsUnresolved() // Push A,B,C; RemoveWhere(r => r.Equals(B)) == 1 → [C, A]
[Test] public void Clear_Empty_ReturnsFalse()

// RefListJsonTests
[Test] public void WriteFavourites_ExactFormat()   // one entry {G, 0, "Player.prefab"} → string equals the sample above exactly
[Test] public void WriteFavourites_Empty()         // == "{\n  \"version\": 1,\n  \"favourites\": []\n}\n"
[Test] public void Favourites_RoundTrip_LargeAndNegativeLocalId()   // localIds long.MaxValue, long.MinValue, -2345678901234567890 survive
[Test] public void Favourites_RoundTrip_EscapesName()               // name "a\"b\\c\nd\té😀" survives
[Test] public void Favourites_WriteIsStable()      // Write(Read(Write(x))) == Write(x)
[Test] public void ReadFavourites_ToleratesWhitespaceAndCrLf()
[Test] public void ReadFavourites_ConflictMarkers_Throws() // "<<<<<<< HEAD\n...\n=======\n...\n>>>>>>> x" → FormatException
[Test] public void Read_Rejects([Values("", "{}", "{\"version\":2,\"favourites\":[]}", "{\"version\":1,\"favourites\":[{\"guid\":\"bad\",\"localId\":\"0\",\"name\":\"\"}]}", "{\"version\":1,\"favourites\":[{\"guid\":\"<G>\",\"localId\":\"1.5\",\"name\":\"\"}]}")] string json) // FormatException
[Test] public void ReadFavourites_DuplicateEntries_KeepsFirst()
[Test] public void Recents_RoundTrip_PreservesOrder()
[Test] public void ReadRecents_MoreThanLimit_Truncates() // 12 entries → first 10
```

- [ ] **Step 2: Run** `dotnet test BetterProjectTests` — Expected: FAIL (types not defined).
- [ ] **Step 3: Implement** the lists, then `RefListJson`. `MiniJson` is an `internal static` recursive-descent reader returning `Dictionary<string, object>` / `List<object>` / `string` / `double` / `bool` / `null`; the writer is hand-built string output (not MiniJson) so format stays exact. `localId` parsed with `long.Parse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)`.
- [ ] **Step 4: Run** `dotnet test BetterProjectTests` — Expected: all PASS.
- [ ] **Step 5: Commit** — `git commit -m "Better Project 2: favourites/recents lists and JSON format"`

---

### Task 3: Paths, grid navigation, selection, recents filter

**Files:**
- Create: `Core/AssetPaths.cs`, `Core/GridNav.cs`, `Core/SelectionModel.cs`, `Core/RecentsFilter.cs`
- Test: `Tests/Editor/AssetPathsTests.cs`, `Tests/Editor/GridNavTests.cs`, `Tests/Editor/SelectionModelTests.cs`, `Tests/Editor/RecentsFilterTests.cs`

**Interfaces:**
- Produces:
  - `static class AssetPaths`:
    - `string Parent(string path)` — `"Assets/A/B"` → `"Assets/A"`; `"Assets"` → `""`.
    - `IReadOnlyList<(string Name, string Path)> Breadcrumbs(string folderPath)` — `"Assets/Art/Props"` → `[("Assets","Assets"), ("Art","Assets/Art"), ("Props","Assets/Art/Props")]`.
    - `bool IsProjectAssetPath(string path)` — true for `"Assets"`, `"Assets/..."`, `"Packages/<pkg>/..."`; false for `"ProjectSettings/..."`, `"Library/..."`, `""`, null.
    - `bool CanMoveInto(string sourcePath, string targetFolder)` — false when target equals source, target is a descendant of source, or `Parent(source)` equals target; comparisons `OrdinalIgnoreCase`; false if either is not a project path.
  - `enum NavDir { Left, Right, Up, Down, Home, End }`; `static class GridNav { int Move(int index, int count, int columns, NavDir dir); }` — list mode is `columns == 1`. Returns -1 when `count == 0`; `index == -1` + any dir → 0. Left/Right step by 1, clamp to `[0, count-1]`. Up: `index - columns`, stay if < 0. Down: `index + columns`; if ≥ count and there is a later row, go to `count - 1`; otherwise stay. Home → 0, End → `count - 1`.
  - `sealed class SelectionModel { IReadOnlyList<int> Selected; int Anchor; void Click(int id, bool toggle, bool range, IReadOnlyList<int> order); void Set(IEnumerable<int> ids); void Retain(IReadOnlyList<int> order); }` — ids are `instanceID`s. Plain click → only `id`, anchor = id. Toggle → add/remove `id`, anchor = id. Range → everything between anchor and `id` in `order` (inclusive), replacing the selection; anchor unchanged; anchor missing from `order` → treat as plain click. `Retain` drops ids no longer in `order` (after refresh).
  - `static class RecentsFilter { bool ShouldRecordEdit(string focusedWindowTypeName, string assetPath); }` — true only when `focusedWindowTypeName` is `"UnityEditor.InspectorWindow"` or `"UnityEditor.PropertyEditor"` **and** `AssetPaths.IsProjectAssetPath(assetPath)`. (Scene GameObject edits never reach it: their targets aren't persistent, see Task 12.)

- [ ] **Step 1: Write failing tests**

```csharp
[Test] public void Parent_Cases()                       // as above
[Test] public void Breadcrumbs_ThreeLevels()            // as above; "Assets" → [("Assets","Assets")]
[Test] public void IsProjectAssetPath_Cases()
[Test] public void CanMoveInto_Allowed()                // ("Assets/A/x.png", "Assets/B") → true
[Test] public void CanMoveInto_Self()                   // ("Assets/A", "Assets/A") → false
[Test] public void CanMoveInto_Descendant()             // ("Assets/A", "Assets/A/B") → false
[Test] public void CanMoveInto_CaseVariantDescendant()  // ("Assets/A", "assets/a/B") → false
[Test] public void CanMoveInto_SameParent()             // ("Assets/A/x.png", "Assets/A") → false
[Test] public void CanMoveInto_SiblingWithSharedPrefix()// ("Assets/A", "Assets/AB") → true

[TestCase(0, 7, 3, NavDir.Down, 3)] [TestCase(5, 7, 3, NavDir.Down, 5)] // 5 is on the last row (row 1 of 0..2): stays
[TestCase(4, 7, 3, NavDir.Down, 6)] // index 7 doesn't exist but row 2 does → last item
[TestCase(1, 7, 3, NavDir.Up, 1)]   [TestCase(4, 7, 3, NavDir.Up, 1)]
[TestCase(6, 7, 3, NavDir.Right, 6)] [TestCase(0, 7, 3, NavDir.Left, 0)]
[TestCase(-1, 7, 3, NavDir.Down, 0)] [TestCase(3, 0, 3, NavDir.Down, -1)]
[TestCase(2, 7, 1, NavDir.Down, 3)] [TestCase(2, 7, 3, NavDir.End, 6)]
public void GridNav_Move(int i, int count, int cols, NavDir d, int expected)

[Test] public void Click_Plain_SelectsOnly()
[Test] public void Click_Toggle_AddsAndRemoves()
[Test] public void Click_Range_FromAnchor()             // order [10,20,30,40]; click 20; range-click 40 → [20,30,40]
[Test] public void Click_Range_Backwards()              // anchor 40, range-click 20 → [20,30,40]
[Test] public void Click_Range_AnchorGone_ActsPlain()
[Test] public void Retain_DropsMissing()

[Test] public void ShouldRecordEdit_InspectorAsset_True()      // ("UnityEditor.InspectorWindow", "Assets/M.mat")
[Test] public void ShouldRecordEdit_OtherWindow_False()        // ("BetterProject.Editor.BetterProjectWindow" or a VectorTerrain window type, "Assets/M.mat")
[Test] public void ShouldRecordEdit_NullWindow_False()         // scripts with no focused window
[Test] public void ShouldRecordEdit_ProjectSettings_False()    // ("UnityEditor.InspectorWindow", "ProjectSettings/TagManager.asset")
```

- [ ] **Step 2: Run** `dotnet test BetterProjectTests` — Expected: FAIL.
- [ ] **Step 3: Implement** the four classes.
- [ ] **Step 4: Run** `dotnet test BetterProjectTests` — Expected: all PASS.
- [ ] **Step 5: Commit** — `git commit -m "Better Project 3: paths, grid nav, selection, recents filter"`

---

### Task 4: Window shell — folder tree + list view

**Files:**
- Create: `Editor/BetterProject.Editor.asmdef`, `Editor/BetterProjectWindow.cs`, `Editor/FolderTree.cs`, `Editor/ContentView.cs`, `Editor/ContentItem.cs`, `Editor/PackageFolders.cs`

**Interfaces:**
- Consumes: `AssetPaths`, `SelectionModel`.
- Produces:
  - `class BetterProjectWindow : EditorWindow` — `[MenuItem("Window/General/Better Project")] static void Open()` (creates a new instance each call via `CreateWindow<BetterProjectWindow>`, title "Better Project"). Serialized state: `string currentFolder = "Assets"`, `float splitterWidth = 220`, `float iconSize` (0 = list), `List<string> expandedFolders`, `string searchText`. Public: `string CurrentFolder { get; }`, `void NavigateTo(string folderPath)`.
  - `class FolderTree` — owns a UITK `TreeView`; `event Action<string> FolderClicked`; `void Rebuild()`; `void Reveal(string folderPath)` (expands ancestors + highlights). Roots in this task: **Assets**, **Packages** (Task 11/12 add the Favourites/Recents sections above them).
  - `struct ContentItem { string Path; string Guid; int InstanceId; bool IsFolder; bool IsSubAsset; int Depth; bool HasSubAssets; string Name; }`
  - `class ContentView` — `void Show(IReadOnlyList<ContentItem> items)`; `event Action<ContentItem> ItemOpened`; `SelectionModel Selection`; `static List<ContentItem> ListFolder(string folderPath)`.
  - `static class PackageFolders { IReadOnlyList<(string DisplayName, string Path)> All(); string DisplayNameFor(string packageRootPath); }` via `UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()`, sorted by display name.

- [ ] **Step 1: Editor asmdef** — name `BetterProject.Editor`, rootNamespace `BetterProject.Editor`, references `BetterProject.Core`, `includePlatforms: ["Editor"]`, `autoReferenced: false`.
- [ ] **Step 2: Implement the window** — toolbar row (placeholder for search), `TwoPaneSplitView` (left fixed pane, `splitterWidth` persisted on change), bottom bar with a `Slider` (0..96, persisted; wired in Task 6). Refresh both panels on `EditorApplication.projectChanged` (unsubscribe in `OnDisable`), keeping selection via `SelectionModel.Retain`.
- [ ] **Step 3: Implement `FolderTree`** — folders only (`AssetDatabase.GetSubFolders`), children loaded on expand (rebuild that subtree from `itemExpandedChanged`) so huge projects stay fast. Icons: `EditorGUIUtility.IconContent("Folder Icon")` / `"FolderOpened Icon"`. Alt+click expand = recursive. Expanded state persisted in `expandedFolders`. Packages root lists `PackageFolders.All()` by display name.
- [ ] **Step 4: Implement `ContentView` list mode** — `ListView` with fixed row height 18; row = icon (`AssetDatabase.GetCachedIcon`) + name. `ListFolder`: subfolders then assets (skip `.meta`, use `AssetDatabase.FindAssets("", new[]{folder})` filtered to direct children or `Directory` + `AssetPathToGUID`), each group sorted with `EditorUtility.NaturalCompare`. Single click → `SelectionModel.Click` → `Selection.objects`. Double click → folder: `NavigateTo`; asset: `AssetDatabase.OpenAsset`. Ctrl/Cmd/Shift flags from the pointer event.
- [ ] **Step 5: Compile check** — load `unity:unity-cli`; with a connected Editor refresh assets and read the Console (Expected: no errors). Otherwise ask the user to focus Unity and report Console errors. Fix until clean.
- [ ] **Step 6: Manual check** (ask the user, or drive via unity-cli + screenshot):
  - Open `Window/General/Better Project` twice → two independent windows.
  - Clicking `Assets/VectorTerrain` lists its subfolders then assets in default-window order. Double click `Props` navigates into it.
  - Single click a material → Inspector shows it. Double click a prefab → Prefab Mode opens.
  - Packages root shows packages by display name and their folders.
  - Close and reopen Unity: splitter width, current folder, expanded folders restored.
- [ ] **Step 7: Commit** (include all `.meta` files Unity generated under `Assets/BetterProject/` so far)

```bash
git add Assets/BetterProject
git commit -m "Better Project 4: window shell, folder tree, list view"
```

---

### Task 5: Context menu, create-in-this-folder (spike), rename, delete, duplicate

Spike first: the spec's main risk. **Time-box ~2 hours.** If no approach meets the acceptance criteria below, stop and report back to the user before continuing.

**Files:**
- Create: `Editor/Internal/UnityInternals.cs`, `Editor/AssetsContextMenu.cs`, `Editor/InlineRename.cs`
- Modify: `Editor/ContentView.cs`, `Editor/BetterProjectWindow.cs`

**Interfaces:**
- Produces:
  - `static class UnityInternals` — every member: `bool TryX(..., out ...)` or returns null on failure; logs one warning per session per member.
  - `static class AssetsContextMenu { void Show(BetterProjectWindow window, Vector2 mousePosition); }`
  - `class InlineRename { void Begin(ContentItem item, VisualElement row); bool IsActive; }` — Enter commits (`AssetDatabase.RenameAsset(path, newName)`), Esc/focus-out cancel; non-empty error → `Debug.LogWarning` and revert.
  - `BetterProjectWindow.BeginRename(string assetPath)` — selects + scrolls to the item and starts inline rename (used by create flow).

**Acceptance criteria for create (the spike):** with the default Project window closed *and* with it open showing a different folder, `Right click → Create → Material` (and `Create → Folder`, `Create → C# Script`) in Better Project creates the asset in **Better Project's current folder**, and inline rename starts in Better Project. Cancelling the rename keeps the default name (same as Unity).

Candidate approaches, in order:
1. Before `EditorUtility.DisplayPopupMenu`, set the folder Unity uses as the active project folder via `UnityInternals` (e.g. `ProjectWindowUtil` / `ProjectBrowser` active-folder state), then intercept the started name-edit (`EndNameEditAction` + temp asset via `ProjectWindowUtil.StartNameEditingIfProjectWindowExists` path) and run it in `InlineRename`.
2. Public-API fallback: record `AssetDatabase` state on menu open; an `AssetPostprocessor` catches assets imported within ~1 s whose parent ≠ our folder → `AssetDatabase.MoveAsset` into our folder → `BeginRename`.
Record the chosen approach (and why others failed) as a comment at the top of `AssetsContextMenu.cs`.

- [ ] **Step 1: Spike** — implement until acceptance criteria pass, or stop and report.
- [ ] **Step 2: Context menu** — right-click on an item (selects it first if not selected) or empty space (clears selection) → `AssetsContextMenu.Show` → `EditorUtility.DisplayPopupMenu(new Rect(pos, Vector2.zero), "Assets/", null)`.
- [ ] **Step 3: Rename** — F2, `Assets → Rename` (via `UnityInternals` hook or a `[MenuItem("Assets/Rename", true)]`-independent command handler on `ExecuteCommandEvent "Rename"`), and slow click (second click on the single selected item, > 0.5 s after the first, not a double-click) → `InlineRename.Begin`.
- [ ] **Step 4: Delete & duplicate** — handle `ValidateCommandEvent`/`ExecuteCommandEvent` `"Delete"`, `"SoftDelete"`, `"Duplicate"` while the window has focus. Delete: confirm dialog listing paths (`EditorUtility.DisplayDialog`), then `AssetDatabase.MoveAssetsToTrash`. Duplicate: `AssetDatabase.CopyAsset` to `AssetDatabase.GenerateUniqueAssetPath`, select the copies.
- [ ] **Step 5: Compile check** (as Task 4 Step 5).
- [ ] **Step 6: Manual check** — acceptance criteria above; context menu on a texture shows Reimport / Show in Finder and they work; F2 and slow click rename; rename to an existing name shows warning and reverts; Delete asks and moves to trash; Cmd/Ctrl+D duplicates.
- [ ] **Step 7: Commit** — `git commit -m "Better Project 5: context menu, create in current folder, rename, delete, duplicate"`

---

### Task 6: Icon mode, thumbnails, sub-asset expansion

**Files:**
- Create: `Editor/Thumbnails.cs`
- Modify: `Editor/ContentView.cs`, `Editor/BetterProjectWindow.cs`

**Interfaces:**
- Consumes: `ContentItem`, `GridNav` (column count used by Task 7).
- Produces:
  - `ContentView.IconSize { get; set; }` — 0 = list mode; otherwise grid with tile size = `IconSize` (min 32, slider 0..96 snaps values in (0, 32) to 32).
  - `ContentView.Columns { get; }` — current grid columns (1 in list mode).
  - `static class Thumbnails { Texture Get(ContentItem item, out bool loading); }` — `AssetPreview.GetAssetPreview(obj)` if available else `AssetPreview.GetMiniThumbnail(obj)`; `loading = AssetPreview.IsLoadingAssetPreview(instanceId)`. Folders: folder icon.

- [ ] **Step 1: Grid** — virtualized: a `ListView` whose rows each hold `Columns` tiles (`Columns = max(1, floor(width / (IconSize + padding)))`), rebuilt on width/size change. Tile = thumbnail + name under it (ellipsis, 2 lines max).
- [ ] **Step 2: Thumbnails** — only bound (visible) tiles request previews. While any visible tile is loading, `schedule.Execute(...).Every(100)` re-binds loading tiles; stops when none load. `AssetPreview.SetPreviewTextureCacheSize` ≥ visible tile count + 64.
- [ ] **Step 3: Sub-assets** — `HasSubAssets` = `AssetDatabase.LoadAllAssetRepresentationsAtPath(path).Length > 0`. Expand arrow on such items; expanding inserts sub-asset items (`IsSubAsset = true`, `Depth = 1`) right after the parent, in both modes. Expanded set kept per window (by guid), not persisted.
- [ ] **Step 4: Compile check.**
- [ ] **Step 5: Manual check** — slider at minimum = list mode; dragging up switches to grid with rendered previews of prefabs/materials/textures; previews pop in without manual repaint; scrolling a folder of 1000+ assets stays smooth; an FBX expands to show its meshes/materials; a sprite sheet shows its sprites.
- [ ] **Step 6: Commit** — `git commit -m "Better Project 6: icon grid, thumbnails, sub-asset expansion"`

---

### Task 7: Multi-select, keyboard navigation, external selection sync

**Files:**
- Modify: `Editor/ContentView.cs`, `Editor/BetterProjectWindow.cs`

**Interfaces:**
- Consumes: `SelectionModel`, `GridNav`, `ContentView.Columns`.

- [ ] **Step 1: Mouse** — Ctrl (Win) / Cmd (mac) toggles, Shift ranges, using `SelectionModel.Click` with `order` = current visible item ids. Click on empty space clears.
- [ ] **Step 2: Keyboard** (when the content panel has focus): arrows → `GridNav.Move` from the last-clicked/anchor item (Shift extends the range), Home/End, Enter → open (same as double click), Backspace (mac: Cmd+Up) → navigate to parent folder, Right/Left on an item with sub-assets in list mode expands/collapses it. Scroll the focused item into view.
- [ ] **Step 3: External sync** — `Selection.selectionChanged`: highlight visible items whose instanceId is selected (`SelectionModel.Set`); never navigate.
- [ ] **Step 4: Compile check.**
- [ ] **Step 5: Manual check** — Shift/Cmd selection of 5 items → Inspector multi-edit works; arrows in grid move by row/column; selecting the same asset in the default Project window highlights it here if visible.
- [ ] **Step 6: Commit** — `git commit -m "Better Project 7: multi-select, keyboard navigation, selection sync"`

---

### Task 8: Drag and drop

**Files:**
- Create: `Editor/ItemDrag.cs`
- Modify: `Editor/ContentView.cs`, `Editor/FolderTree.cs`

**Interfaces:**
- Consumes: `AssetPaths.CanMoveInto`.
- Produces: `static class ItemDrag { void StartDrag(IReadOnlyList<ContentItem> items); bool HandleDropOnFolder(string folderPath, bool perform); }` — `HandleDropOnFolder` reads `DragAndDrop.paths`, sets `DragAndDrop.visualMode` (`Move` if any path passes `CanMoveInto`, else `Rejected`), and on `perform` calls `AssetDatabase.MoveAsset` for each passing path, logging `AssetDatabase.MoveAsset` errors as warnings.

- [ ] **Step 1: Drag out** — pointer-down + move > 5 px on a selected item → `DragAndDrop.PrepareStartDrag`; `objectReferences` = selected objects; `paths` = their paths; `StartDrag(title)`. Sub-assets have `paths` = parent path but only the sub-asset object in `objectReferences`.
- [ ] **Step 2: Drop to move** — folders in the right panel and in the folder tree are drop targets (highlight on `DragUpdatedEvent`). Dropping on empty space in the right panel targets the current folder.
- [ ] **Step 3: Compile check.**
- [ ] **Step 4: Manual check** — drag a prefab to Scene view and Hierarchy (instantiates); drag a material onto an Inspector material slot; drag 3 assets onto a folder in the tree (moved); drag a folder onto its own child (rejected cursor, nothing happens); drag a texture from the default Project window onto a folder here (moved).
- [ ] **Step 5: Commit** — `git commit -m "Better Project 8: drag out and drag-to-move"`

---

### Task 9: Search

**Files:**
- Create: `Editor/SearchController.cs`
- Modify: `Editor/BetterProjectWindow.cs`

**Interfaces:**
- Produces: `class SearchController { event Action<IReadOnlyList<ContentItem>> ResultsReady; void SetQuery(string text); bool IsActive; }` — debounce 200 ms; empty/whitespace → `IsActive = false` and the window shows `CurrentFolder` again.

- [ ] **Step 1: Implement** — `ToolbarSearchField` in the toolbar bound to `searchText` (persisted). Results: `AssetDatabase.FindAssets(query, new[] { "Assets" })` → `ContentItem`s, folders first, natural sort; flat (no sub-asset rows unless expanded). Esc in the field clears it. Navigating to a folder (tree click / double click on a folder result) clears the search.
- [ ] **Step 2: Compile check.**
- [ ] **Step 3: Manual check** — `Ground` finds `Ground.mat`; `t:Prefab` lists prefabs; `l:SomeLabel` works; typing fast does not stutter; clearing restores the folder view with prior selection.
- [ ] **Step 4: Commit** — `git commit -m "Better Project 9: search"`

---

### Task 10: Breadcrumbs (V2.1)

**Files:**
- Create: `Editor/Breadcrumbs.cs`
- Modify: `Editor/BetterProjectWindow.cs`

**Interfaces:**
- Consumes: `AssetPaths.Breadcrumbs`, `PackageFolders.DisplayNameFor`, `BetterProjectWindow.NavigateTo`.
- Produces: `class Breadcrumbs : VisualElement { void SetFolder(string folderPath); void SetSearch(); }`

- [ ] **Step 1: Implement** — bar above the right panel: `Assets ▸ Art ▸ Props`. Each name is a button → `NavigateTo(path)`. Each `▸` after a folder is a button → `GenericMenu` with that folder's direct subfolders (natural sort; disabled "No subfolders" if none); choosing one navigates. Package root segment shows the package display name. While searching: shows a single non-clickable label "Search results". Overflow: if too wide, collapse leading segments into `…` (clicking it shows a menu of the hidden ancestors).
- [ ] **Step 2: Compile check.**
- [ ] **Step 3: Manual check** — every segment navigates; arrow after `Assets` lists `Scenes`, `Settings`, `VectorTerrain`, …; deep path in a narrow window shows `…`.
- [ ] **Step 4: Commit** — `git commit -m "Better Project 10: breadcrumbs"`

---

### Task 11: Favourites (V2.2)

**Files:**
- Create: `Editor/AssetRefUtil.cs`, `Editor/FavouritesStore.cs`
- Modify: `Editor/FolderTree.cs`, `Editor/BetterProjectWindow.cs`

**Interfaces:**
- Consumes: `FavouritesList`, `RefListJson`, `AssetRef`, `ItemDrag`.
- Produces:
  - `static class AssetRefUtil { bool TryFromObject(Object o, out AssetRef r); Object Resolve(AssetRef r); string PathOf(AssetRef r); }` — `TryFromObject`: `AssetDatabase.TryGetGUIDAndLocalFileIdentifier`; `LocalId = 0` when `AssetDatabase.IsMainAsset(o)` or `o` is a folder. `Resolve`: guid → path (null if none) → `LocalId == 0` ? `LoadMainAssetAtPath` : match in `LoadAllAssetsAtPath`.
  - `static class FavouritesStore { FavouritesList List; bool LoadFailed; event Action Changed; bool Add(Object o); bool Remove(AssetRef r); void ReloadIfChanged(); }` — path `ProjectSettings/BetterProjectFavourites.json`. Missing file = empty list (file created on first add). Saves only when the list changes; **never saves while `LoadFailed`** (Add/Remove then return false and log a warning). `ReloadIfChanged` compares `File.GetLastWriteTimeUtc`; called from `EditorApplication.projectChanged`, `EditorApplication.focusChanged` (true), and the window's `OnFocus`.

- [ ] **Step 1: Store + util** — implement as above. Name stored on add = `o.name` (+ extension for main assets, e.g. `Player.prefab`).
- [ ] **Step 2: Section** — **Favourites** section at the top of the left panel (collapsible, collapsed state persisted). Entry: icon + live name (resolved) or stored name greyed with tooltip "Missing: <guid>" (unresolved). Click/double-click rules from Global Constraints; a folder favourite click shows that folder in the right panel. Folder favourites expand to show subfolders **and** assets (lazy, on expand), with the same click / drag-out behavior. Header shows a warning icon + tooltip "Favourites file unreadable: <message>" while `LoadFailed`.
- [ ] **Step 3: Add/remove** — the Favourites header and entries area accept drops from this window, the default Project window, and favourites' own children: each `DragAndDrop.objectReferences` that `TryFromObject` succeeds on is added (folders, assets, sub-assets; scene objects rejected). Right-click entry → `GenericMenu` "Remove from Favourites". Dragging a favourite out works like any item (Task 8).
- [ ] **Step 4: Compile check.**
- [ ] **Step 5: Manual check** —
  - Add a folder, a prefab, and a sprite from a sprite sheet → `ProjectSettings/BetterProjectFavourites.json` matches the Task 2 format; `git diff` shows only those lines.
  - Rename and move the prefab → entry still works; `git diff` of the favourites file shows **no change**.
  - Delete a favourited asset → entry greyed with stored name; right-click remove works.
  - Edit the JSON externally (add/remove an entry) → window updates on refocus.
  - Put conflict markers in the file → header warning; adding a favourite is refused; file untouched. Fix file → recovers on refocus.
  - Single click prefab favourite → Inspector; double click → Prefab Mode; expand folder favourite → contents listed.
- [ ] **Step 6: Commit** — `git commit -m "Better Project 11: shared favourites"`

---

### Task 12: Recents (V2.3)

**Files:**
- Create: `Editor/RecentsStore.cs`, `Editor/RecentsTracker.cs`
- Modify: `Editor/FolderTree.cs`

**Interfaces:**
- Consumes: `RecentsList`, `RefListJson`, `RecentsFilter`, `AssetRefUtil`.
- Produces:
  - `static class RecentsStore { RecentsList List; event Action Changed; void Push(string assetPath); void Remove(AssetRef r); void Clear(); }` — path `UserSettings/BetterProjectRecents.json`; unreadable file → reset to empty silently (personal data); saves on every change. `Push` always records the **main asset** (`LocalId = 0`). On load and on `projectChanged`, `RemoveWhere` entries whose guid no longer resolves to a path.
  - `[InitializeOnLoad] static class RecentsTracker` — subscribes:
    - `PrefabStage.prefabStageOpened` → `Push(stage.assetPath)`;
    - `EditorSceneManager.sceneOpened` → `Push(scene.path)` when the path is non-empty;
    - `Undo.postprocessModifications` → for each modification whose `currentValue.target` is `EditorUtility.IsPersistent`, `path = AssetDatabase.GetAssetPath(target)`; push if `RecentsFilter.ShouldRecordEdit(EditorWindow.focusedWindow?.GetType().FullName, path)`. Return modifications unchanged. Deduplicate pushes within one callback.

- [ ] **Step 1: Implement** store + tracker.
- [ ] **Step 2: Section** — **Recents** section below Favourites, above Assets; same item rendering/click/drag rules; right-click entry → "Remove from Recents"; right-click header → "Clear Recents".
- [ ] **Step 3: Compile check.**
- [ ] **Step 4: Manual check** —
  - Open prefab A, then B → Recents `[B, A]`. Reopen A → `[A, B]`.
  - Change a material color in the Inspector → material at top.
  - Change texture import settings in the Inspector + Apply → texture added.
  - Edit a GameObject in the open scene → scene file **not** added. Open another scene → that scene added.
  - Edit a terrain in the VectorTerrain window (script-driven asset writes) → **nothing** added.
  - Open 11 different prefabs → 10 entries, oldest gone.
  - Delete a recent asset → entry disappears. `git status` shows no change for `UserSettings/` (ignored).
- [ ] **Step 5: Commit** — `git commit -m "Better Project 12: recents"`

---

### Task 13: Final pass

- [ ] **Step 1:** `dotnet test BetterProjectTests` — Expected: all PASS. Also run the same tests in Unity's Test Runner (EditMode) — Expected: all PASS.
- [ ] **Step 2: Isolation check** — `grep -rn "VectorTerrain\|ToonyColors" Assets/BetterProject` → no matches. `grep -rln "BindingFlags.NonPublic\|GetType(\"UnityEditor" Assets/BetterProject` → only `Editor/Internal/UnityInternals.cs`.
- [ ] **Step 3: Performance check** (user or unity-cli): in this project, folder switch, search typing, and scrolling the largest folder in grid mode feel as fast as the default window; no Console errors/warnings during a 5-minute session using the window instead of the default one.
- [ ] **Step 4: Light/dark skin check** — switch Editor theme; all text/icons readable.
- [ ] **Step 5: Commit** any fixes — `git commit -m "Better Project 13: final fixes"`
