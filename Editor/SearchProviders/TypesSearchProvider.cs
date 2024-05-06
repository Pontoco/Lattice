using System;
using System.Collections.Generic;
using Lattice.Base;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace Lattice.Editor.SearchProviders
{
    /// <summary>Search provider that returns C# Types. Currently scoped to BaseNode types, but we could expose more.</summary>
    public class TypesSearchProvider : ScriptableObject, ISearchWindowProvider
    {
        private static TypesSearchProvider instance;

        public static TypesSearchProvider Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = CreateInstance<TypesSearchProvider>();
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
                new SearchTreeGroupEntry(new GUIContent("Find C# Type")),
            };
            foreach (var type in TypeCache.GetTypesDerivedFrom(typeof(BaseNode)))
            {
                if (type != null && Filter(type))
                {
                    SearchTreeEntry searchTreeEntry = new(
                        new GUIContent($"{type.Assembly.FullName}, {type.Namespace}.{type.Name}"));
                    searchTreeEntry.userData = type;
                    searchTreeEntry.level = 1;
                    entries.Add(searchTreeEntry);
                }
            }
            return entries;
        }

        /// <inheritdoc />
        public bool OnSelectEntry(SearchTreeEntry SearchTreeEntry, SearchWindowContext context)
        {
            Callback?.Invoke((Type)SearchTreeEntry.userData);
            return true;
        }
    }
}
