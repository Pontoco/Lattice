using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Unity.Entities;
using UnityEditor;
using UnityEngine;

namespace Lattice.Editor.SearchProviders
{
    /// <summary>The "Systems" menu implementation.</summary>
    public class AllSystemsSearchProvider : SearchMenuProvider<Type>
    {
        private static AllSystemsSearchProvider instance;

        public static AllSystemsSearchProvider Instance => instance ??= new AllSystemsSearchProvider(null);

        /// <inheritdoc />
        protected override string Title => "Systems";

        public Action<Type> Callback;
        public Func<Type, bool> Filter = _ => true;

        /// <inheritdoc />
        private AllSystemsSearchProvider([CanBeNull] EditorWindow editorWindow) : base(editorWindow) { }

        /// <inheritdoc />
        protected override void AddSearchEntries(List<SearchEntry> searchEntries)
        {
            foreach (Type system in TypeManager.GetSystems().Where(Filter))
            {
                SearchEntry searchTreeEntry = new()
                {
                    Title = new[] { GetTitle(system) },
                    Item = system,
                };
                searchEntries.Add(searchTreeEntry);
            }
            return;

            string GetTitle(Type system) => system.Namespace == null ? system.Name : $"{system.Name} ({system.Namespace})";
        }

        /// <inheritdoc />
        protected override void OnSearcherSelectEntry(SearchEntry entry, Vector2 windowMousePosition)
        {
            Callback?.Invoke(entry.Item);
        }
    }
}
