using System;
using UnityEditor;
using UnityEngine;

namespace BetterProject.Editor
{
    /// <summary>
    /// Project-wide Better Project settings, stored in <c>ProjectSettings/BetterProjectSettings.asset</c> (committed,
    /// shared by the team). Edited on the "Project/Better Project" page of Project Settings.
    /// </summary>
    [FilePath("ProjectSettings/BetterProjectSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public class BetterProjectSettings : ScriptableSingleton<BetterProjectSettings>
    {
        public const int MaxSidebarSpacing = 16;
        public const int MaxListSize = 100; // upper bound for "Max recents" / "Max new assets"

        // Field initializers are the defaults; a settings file saved before a field existed keeps the default.
        [SerializeField] int sidebarSpacing;

        [SerializeField] bool showFavourites = true;
        [SerializeField] bool showRecents = true;
        [SerializeField] bool showPackages = true;
        [SerializeField] bool showNewAssets;
        [SerializeField] bool showCreateButton = true;

        [SerializeField] int maxRecents = 10;
        [SerializeField] int maxNewAssets = 20;

        [SerializeField] bool hoverLeftPanel = true;
        [SerializeField] bool hoverRightPanel = true;

        [SerializeField] bool removeRightPanelOffset;

        [SerializeField] bool dontSelectFoldersInRightPane;
        [SerializeField] bool deleteWithoutConfirmation;

        [SerializeField] bool recentsOnAssetChanged = true;
        [SerializeField] bool recentsOnPrefabOpened = true;
        [SerializeField] bool recentsOnSceneOpened = true;
        [SerializeField] bool recentsOnAssetImported;

        /// <summary>Raised after any setting changed (and was saved).</summary>
        public static event Action Changed;

        /// <summary>Vertical space, in pixels, between the left panel's sections (Favourites, Recents, folder tree).</summary>
        public int SidebarSpacing
        {
            get => Mathf.Clamp(sidebarSpacing, 0, MaxSidebarSpacing);
            set => Set(ref sidebarSpacing, Mathf.Clamp(value, 0, MaxSidebarSpacing));
        }

        public bool ShowFavourites { get => showFavourites; set => Set(ref showFavourites, value); }
        public bool ShowRecents { get => showRecents; set => Set(ref showRecents, value); }
        public bool ShowPackages { get => showPackages; set => Set(ref showPackages, value); }
        /// <summary>Show the "New assets" section in the left panel, below Recents (off by default).</summary>
        public bool ShowNewAssets { get => showNewAssets; set => Set(ref showNewAssets, value); }
        /// <summary>Show the "+" (Create) button left of the search field.</summary>
        public bool ShowCreateButton { get => showCreateButton; set => Set(ref showCreateButton, value); }

        /// <summary>How many entries Recents keeps.</summary>
        public int MaxRecents
        {
            get => Mathf.Clamp(maxRecents, 1, MaxListSize);
            set => Set(ref maxRecents, Mathf.Clamp(value, 1, MaxListSize));
        }

        /// <summary>How many entries "New assets" keeps.</summary>
        public int MaxNewAssets
        {
            get => Mathf.Clamp(maxNewAssets, 1, MaxListSize);
            set => Set(ref maxNewAssets, Mathf.Clamp(value, 1, MaxListSize));
        }

        /// <summary>Rows of the left panel (sections, folder tree) highlight under the mouse.</summary>
        public bool HoverLeftPanel { get => hoverLeftPanel; set => Set(ref hoverLeftPanel, value); }
        /// <summary>Items of the right panel highlight under the mouse.</summary>
        public bool HoverRightPanel { get => hoverRightPanel; set => Set(ref hoverRightPanel, value); }

        /// <summary>List view of the right panel: icons start at the left edge (no padding / arrow column).</summary>
        public bool RemoveRightPanelOffset { get => removeRightPanelOffset; set => Set(ref removeRightPanelOffset, value); }

        /// <summary>Clicking a folder in the right panel never selects it (double click still opens it).</summary>
        public bool DontSelectFoldersInRightPane { get => dontSelectFoldersInRightPane; set => Set(ref dontSelectFoldersInRightPane, value); }

        /// <summary>The Delete key in Better Project moves assets to the trash without asking first.</summary>
        public bool DeleteWithoutConfirmation { get => deleteWithoutConfirmation; set => Set(ref deleteWithoutConfirmation, value); }

        /// <summary>Add to Recents: an asset the user edits in the Inspector.</summary>
        public bool RecentsOnAssetChanged { get => recentsOnAssetChanged; set => Set(ref recentsOnAssetChanged, value); }
        /// <summary>Add to Recents: a prefab opened in Prefab Mode.</summary>
        public bool RecentsOnPrefabOpened { get => recentsOnPrefabOpened; set => Set(ref recentsOnPrefabOpened, value); }
        /// <summary>Add to Recents: a scene opened.</summary>
        public bool RecentsOnSceneOpened { get => recentsOnSceneOpened; set => Set(ref recentsOnSceneOpened, value); }
        /// <summary>Add to Recents: an asset imported or reimported (new files, changes made outside Unity, saves).</summary>
        public bool RecentsOnAssetImported { get => recentsOnAssetImported; set => Set(ref recentsOnAssetImported, value); }

        void Set<T>(ref T field, T value)
        {
            if (Equals(field, value)) return;
            field = value;
            Save(true);
            Changed?.Invoke();
        }
    }
}
