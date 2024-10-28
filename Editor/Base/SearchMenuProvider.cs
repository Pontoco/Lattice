using System;
using System.Collections.Generic;
using System.Reflection;
using JetBrains.Annotations;
using UnityEditor;
using UnityEditor.Searcher;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lattice.Editor
{
    /// <summary>The base of a searchable foldout tree implementation similar to ShaderGraph's Create Node menu.</summary>
    public abstract class SearchMenuProvider<T>
    {
        protected struct SearchEntry
        {
            /// <summary>A hierarchy of strings making up the entry in the tree, with the leaf being the name of the entry.</summary>
            public string[] Title;
            /// <summary>The value associated with this entry.</summary>
            public T Item;
        }

        private sealed class SearchEntryItem : SearcherItem
        {
            public readonly SearchEntry SearchEntry;

            public SearchEntryItem(string name, SearchEntry searchEntry, string[] synonyms) : base(name)
            {
                SearchEntry = searchEntry;
                Synonyms = synonyms;
            }
        }

        /// <summary>The title displayed at the top of the menu.</summary>
        protected abstract string Title { get; }

        /// <summary>How the menu is aligned relative to the mouse position.</summary>
        public SearcherWindow.Alignment Alignment { get; set; } = new(SearcherWindow.Alignment.Vertical.Center, SearcherWindow.Alignment.Horizontal.Left);

        /// <summary>Whether <see cref="AddSearchEntries" /> must be called before showing elements.</summary>
        protected bool RegenerateEntries { get; set; } = true;

        private List<SearchEntry> searchEntries;

        [CanBeNull]
        public EditorWindow EditorWindow { get; set; }

        protected SearchMenuProvider([CanBeNull] EditorWindow editorWindow)
        {
            EditorWindow = editorWindow;
        }

        /// <summary>Shows the search window.</summary>
        /// <param name="mousePosition">The position to show the menu and create any items under.</param>
        // ReSharper disable once MemberCanBeProtected.Global
        public virtual void Show(Vector2 mousePosition)
        {
            if (RegenerateEntries)
            {
                GenerateSearchEntries();
                RegenerateEntries = false;
            }

            AdjustMinimumSearcherWindowHeight();
            SearcherWindow.Show(
                EditorWindow ?? EditorWindow.focusedWindow,
                LoadSearchWindow(),
                item => OnSearcherSelectEntry(item, mousePosition),
                mousePosition,
                null,
                Alignment
            );
        }

        /// <summary>Shows the search window under the current mouse position.</summary>
        public void Show()
        {
            Show(Event.current.mousePosition);
        }

        /// <summary>Shows the search window over a VisualElement.</summary>
        public void Show(VisualElement element)
        {
            Alignment = new SearcherWindow.Alignment(SearcherWindow.Alignment.Vertical.Top, SearcherWindow.Alignment.Horizontal.Center);
            Rect bounds = element.worldBound;
            Show(new Vector2(bounds.center.x, bounds.y));
        }

        private static void AdjustMinimumSearcherWindowHeight()
        {
            Vector2 minimumSize = LatticePreferences.instance.MinimumSearchWindowSize;
            FieldInfo sizeField = typeof(SearcherWindow).GetField("s_Size", BindingFlags.Static | BindingFlags.NonPublic);
            try
            {
                Vector2 size = (Vector2)sizeField!.GetValue(null);
                size.x = Mathf.Max(size.x, minimumSize.x);
                size.y = Mathf.Max(size.y, minimumSize.y);
                sizeField.SetValue(null, size);
            }
            catch
            {
                Debug.LogWarning("Internal code relating to minimum searcher window size has changed, please report an issue with Lattice.");
            }
        }

        private void GenerateSearchEntries()
        {
            // Clear
            searchEntries?.Clear();
            searchEntries ??= new List<SearchEntry>();

            // Append
            AddSearchEntries(searchEntries);

            // Sort
            SortEntries();
        }

        /// <summary>Implementations should append search entries to <paramref name="searchEntries"/>.</summary>
        protected abstract void AddSearchEntries(List<SearchEntry> searchEntries);

        private Searcher LoadSearchWindow()
        {
            // Create empty root for searcher tree.
            var root = new List<SearcherItem>();
            SearchEntry dummyEntry = new();

            foreach (SearchEntry searchEntry in searchEntries)
            {
                SearcherItem parent = null;
                for (int i = 0; i < searchEntry.Title.Length; i++)
                {
                    string pathEntry = searchEntry.Title[i];
                    List<SearcherItem> children = parent != null ? parent.Children : root;
                    SearcherItem item = children.Find(x => x.Name == pathEntry);

                    if (item == null)
                    {
                        // if we are at a leaf, add userdata to the entry
                        if (i == searchEntry.Title.Length - 1)
                        {
                            item = new SearchEntryItem(pathEntry, searchEntry, null);
                        }
                        // if we aren't a leaf, don't add user data
                        else
                        {
                            item = new SearchEntryItem(pathEntry, dummyEntry, null);
                        }

                        if (parent != null)
                        {
                            parent.AddChild(item);
                        }
                        else
                        {
                            children.Add(item);
                        }
                    }

                    parent = item;

                    if (parent.Depth == 0 && !root.Contains(parent))
                    {
                        root.Add(parent);
                    }
                }
            }

            SearcherDatabase searchDatabase = SearcherDatabase.Create(root, string.Empty, false);

            return new Searcher(searchDatabase, new SearchWindowAdapter(Title));
        }

        private void SortEntries()
        {
            // Sort the entries lexicographically by group then title with the requirement that items always comes before sub-groups in the same group.
            // Example result:
            // - Art/BlendMode
            // - Art/Adjustments/ColorBalance
            // - Art/Adjustments/Contrast
            searchEntries.Sort((entry1, entry2) =>
            {
                for (int i = 0; i < entry1.Title.Length; i++)
                {
                    if (i >= entry2.Title.Length)
                    {
                        return 1;
                    }

                    int value = string.Compare(entry1.Title[i], entry2.Title[i], StringComparison.Ordinal);
                    if (value == 0)
                    {
                        continue;
                    }

                    // Make sure that leaves go before nodes
                    if (entry1.Title.Length == entry2.Title.Length || (i != entry1.Title.Length - 1 && i != entry2.Title.Length - 1))
                    {
                        return value;
                    }

                    // Once nodes are sorted, sort slot entries by slot order instead of alphabetically.
                    int alphaOrder = entry1.Title.Length < entry2.Title.Length ? -1 : 1;
                    // Add more sorting here, e.g. returning alphaOrder.CompareTo(slotOrder).
                    return alphaOrder;
                }
                return 0;
            });
        }

        private bool OnSearcherSelectEntry(SearcherItem entry, Vector2 screenMousePosition)
        {
            // Cast and check for null.
            if (entry is not SearchEntryItem searchEntryItem)
            {
                return true;
            }

            VisualElement windowRoot = (EditorWindow ?? EditorWindow.focusedWindow).rootVisualElement;
            Vector2 windowMousePosition = windowRoot.parent == null ? screenMousePosition : windowRoot.ChangeCoordinatesTo(windowRoot.parent, screenMousePosition);
            OnSearcherSelectEntry(searchEntryItem.SearchEntry, windowMousePosition);
            return true;
        }

        /// <summary>Called once an entry is selected.</summary>
        protected abstract void OnSearcherSelectEntry(SearchEntry entry, Vector2 windowMousePosition);
    }
}
