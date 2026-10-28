## Scope

New alternative Project window plugin for Unity, with features like favourite objects, recent files

### Namespace

All files should be placed inside a folder with .asmdef to be shipped later as a package

### Flow

You can inherit from default project window, or rewrite your own

### Functionality

#### V1

For v1 make a complete copy of project window, including:
- Left panel with Assets and Packages folders
- Right panel with file/folder list
- search bar
- slider to change list/icons
- asset icons, clicking, and renaming

omit:
- favourites
- folder breadcrumbs bar
- Filters, and favourite selector

#### V2

1. Implement breadcrumbs bar - it should display clickable folder names, between them clickable arrows. On arrow click show context menu with all possible folders to go from it
2. Implement favourites - folders, assets, and subassets can be dragged there. On right click in menu it can be removed. Folders inside favourites can be expanded
3. Implement recents - whenever prefab is opened, or any asset changed in inspector, add there. Limit to 10 items