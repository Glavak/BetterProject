using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BetterProject.Editor
{
    /// <summary>The "Better Project" page in Project Settings.</summary>
    static class BetterProjectSettingsProvider
    {
        public const string Path = "Project/Better Project";

        // Distance from the page's left edge to every control, so all of them line up in one column like the
        // Inspector's. Fixed rather than Unity's aligned-field width, which lets long labels push their control out.
        const float LabelWidth = 250;
        const float BlockIndent = 15;

        public static void Open() => SettingsService.OpenProjectSettings(Path);

        [SettingsProvider]
        static SettingsProvider Create() => new SettingsProvider(Path, SettingsScope.Project)
        {
            label = "Better Project",
            keywords = new HashSet<string>
            {
                "Better Project", "Project window", "Sidebar", "Spacing", "Show", "Favourites", "Recents", "Packages",
                "Highlight", "Hover", "Mouse", "Panel", "Select", "Folders", "Right pane", "Delete", "Confirmation", "Offset", "Padding", "Max recents", "Max new assets", "New assets", "Create", "Plus", "Button", "Asset changed", "Prefab opened", "Scene opened", "Asset imported",
            },
            activateHandler = (search, root) => Build(root, BetterProjectSettings.instance),
        };

        static void Build(VisualElement root, BetterProjectSettings s)
        {
            root.style.paddingLeft = 10;
            root.style.paddingTop = 1;

            var title = new Label("Better Project");
            title.style.fontSize = 19;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 10;
            root.Add(title);

            var spacing = new SliderInt("Sidebar spacing", 0, BetterProjectSettings.MaxSidebarSpacing)
            {
                value = s.SidebarSpacing,
                showInputField = true,
                tooltip = "Space, in pixels, between the sections of the left panel (Favourites, Recents, folders).",
            };
            SetLabelWidth(spacing.labelElement, LabelWidth);
            spacing.RegisterValueChangedCallback(e => s.SidebarSpacing = e.newValue);
            root.Add(spacing);

            var show = Block(root, "Show");
            Option(show, "Favourites", "Show the Favourites section in the left panel.", s.ShowFavourites, v => s.ShowFavourites = v);
            Option(show, "Recents", "Show the Recents section in the left panel.", s.ShowRecents, v => s.ShowRecents = v);
            Option(show, "Packages", "Show the Packages folder in the left panel's folder tree.", s.ShowPackages, v => s.ShowPackages = v);
            Option(show, "New assets", "Show the newest added assets in a section of the left panel, below Recents.",
                s.ShowNewAssets, v => s.ShowNewAssets = v);
            Option(show, "Create button", "Show the \"+\" button left of the search field. It opens the Create menu for the folder shown.",
                s.ShowCreateButton, v => s.ShowCreateButton = v);

            Option(show, "Remove offset in right panel",
                "List view: icons start at the left edge, without the padding Unity's Project window has there. " +
                "Assets with sub-assets show their expand arrow at the right end of the row instead.",
                s.RemoveRightPanelOffset, v => s.RemoveRightPanelOffset = v);

            var lists = Block(root, "Lists");
            Count(lists, "Max recents", "How many assets Recents keeps. Lowering it drops the oldest entries.",
                s.MaxRecents, v => s.MaxRecents = v);
            Count(lists, "Max new assets", "How many assets \"New assets\" keeps. Lowering it drops the oldest entries.",
                s.MaxNewAssets, v => s.MaxNewAssets = v);

            var hover = Block(root, "Highlight under mouse");
            Option(hover, "Left Panel", "Highlight the row under the mouse in Favourites, Recents and the folder tree.",
                s.HoverLeftPanel, v => s.HoverLeftPanel = v);
            Option(hover, "Right Panel", "Highlight the item under the mouse in the folder contents.",
                s.HoverRightPanel, v => s.HoverRightPanel = v);

            var selection = Block(root, "Selection");
            Option(selection, "Don't select folders in right pane",
                "Clicking a folder in the right panel doesn't select it (the Inspector keeps its object), not even on double click; double click still opens it.",
                s.DontSelectFoldersInRightPane, v => s.DontSelectFoldersInRightPane = v);
            var delete = Option(selection, "Delete without confirmation",
                "The Delete key in Better Project moves the selected assets to the trash without asking first. " +
                "(Delete from the right-click menu is Unity's own command and still asks.)",
                s.DeleteWithoutConfirmation, v => s.DeleteWithoutConfirmation = v);
            AddWarningIcon(delete, "Assets are deleted without asking. They go to the system trash, not Unity's Undo.");

            var recents = Block(root, "Add to Recents when");
            Option(recents, "Asset changed", "An asset is edited in the Inspector.",
                s.RecentsOnAssetChanged, v => s.RecentsOnAssetChanged = v);
            Option(recents, "Prefab opened", "A prefab is opened in Prefab Mode.",
                s.RecentsOnPrefabOpened, v => s.RecentsOnPrefabOpened = v);
            Option(recents, "Scene opened", "A scene is opened.",
                s.RecentsOnSceneOpened, v => s.RecentsOnSceneOpened = v);
            Option(recents, "Asset imported", "An asset is imported or reimported: new files, files changed outside Unity, saved assets.",
                s.RecentsOnAssetImported, v => s.RecentsOnAssetImported = v);
        }

        // A bold block title with its options indented below it.
        static VisualElement Block(VisualElement root, string title)
        {
            var header = new Label(title);
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.marginTop = 12;
            header.style.marginBottom = 2;
            header.style.marginLeft = 3;
            root.Add(header);
            var body = new VisualElement();
            body.style.paddingLeft = BlockIndent;
            root.Add(body);
            return body;
        }

        static Toggle Option(VisualElement block, string label, string tooltip, bool value, Action<bool> set)
        {
            var toggle = new Toggle(label) { value = value, tooltip = tooltip };
            SetLabelWidth(toggle.labelElement, LabelWidth - BlockIndent);
            toggle.RegisterValueChangedCallback(e => set(e.newValue));
            block.Add(toggle);
            return toggle;
        }

        // Applied on Enter / focus loss (not per keystroke), clamped to 1..MaxListSize.
        static void Count(VisualElement block, string label, string tooltip, int value, Action<int> set)
        {
            var field = new IntegerField(label) { value = value, tooltip = tooltip, isDelayed = true };
            SetLabelWidth(field.labelElement, LabelWidth - BlockIndent);
            field.RegisterValueChangedCallback(e =>
            {
                int v = Mathf.Clamp(e.newValue, 1, BetterProjectSettings.MaxListSize);
                if (v != e.newValue) field.SetValueWithoutNotify(v);
                set(v);
            });
            block.Add(field);
        }

        static void SetLabelWidth(VisualElement label, float width)
        {
            label.style.width = width;
            label.style.minWidth = width;
            label.style.maxWidth = width;
        }

        // Yellow warning sign in the block's indent, just left of a toggle's label, so the label stays aligned.
        static void AddWarningIcon(Toggle toggle, string tooltip)
        {
            var icon = new Image
            {
                image = EditorGUIUtility.IconContent("console.warnicon.sml").image,
                scaleMode = ScaleMode.ScaleToFit,
                tooltip = tooltip,
            };
            icon.style.width = 16;
            icon.style.height = 16;
            icon.style.position = Position.Absolute;
            icon.style.left = -17;
            icon.style.top = Length.Percent(50);
            icon.style.translate = new Translate(0, Length.Percent(-50));
            toggle.Insert(0, icon);
        }
    }
}
