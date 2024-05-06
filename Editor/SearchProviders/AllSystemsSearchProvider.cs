using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace Lattice.Editor.SearchProviders
{
    public class AllSystemsSearchProvider : ScriptableObject, ISearchWindowProvider
    {
        private static AllSystemsSearchProvider instance;

        public static AllSystemsSearchProvider Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = CreateInstance<AllSystemsSearchProvider>();
                }

                return instance;
            }
        }

        public Action<Type> Callback;
        public Func<Type, bool> Filter = _ => true;

        /// <inheritdoc />
        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            List<SearchTreeEntry> entries = new()
            {
                new SearchTreeGroupEntry(new GUIContent("Systems")),
            };
            foreach (var system in TypeManager.GetSystems())
            {
                if (!Filter(system))
                    continue;
                
                SearchTreeEntry searchTreeEntry = new(new GUIContent(GetTitle(system)))
                {
                    userData = system,
                    level = 1
                };
                entries.Add(searchTreeEntry);
            }
            return entries;

            string GetTitle(Type system) => system.Namespace == null ? system.Name : $"{system.Name} ({system.Namespace})";
        }

        /// <inheritdoc />
        public bool OnSelectEntry(SearchTreeEntry SearchTreeEntry, SearchWindowContext context)
        {
            Callback?.Invoke((Type)SearchTreeEntry.userData);
            return true;
        }
    }
}
