using System;
using System.Collections.Generic;
using System.Reflection;
using Lattice.Base;
using Lattice.StandardLibrary;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace Lattice.Editor.SearchProviders
{
    /// <summary>Search provider that returns C# static method types. Currently scoped to BaseNode types, but we could expose more.</summary>
    public class ScriptNodeMethodSearchProvider : ScriptableObject, ISearchWindowProvider
    {
        private static ScriptNodeMethodSearchProvider instance;

        public static ScriptNodeMethodSearchProvider Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = CreateInstance<ScriptNodeMethodSearchProvider>();
                }

                return instance;
            }
        }

        public Action<MethodInfo> Callback;
        public Func<MethodInfo, bool> Filter = _ => true;

        /// <inheritdoc />
        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            List<SearchTreeEntry> entries = new()
            {
                new SearchTreeGroupEntry(new GUIContent("Find C# Method")),
            };
            foreach (var method in TypeCache.GetMethodsWithAttribute<LatticeNodeAttribute>())
            {
                if (method != null && Filter(method))
                {
                    SearchTreeEntry searchTreeEntry = new(
                        new GUIContent($"{method.DeclaringType!.FullName}/{method.Name}"));
                    searchTreeEntry.userData = method;
                    searchTreeEntry.level = 1;
                    entries.Add(searchTreeEntry);
                }
            }
            foreach (var type in TypeCache.GetTypesWithAttribute<LatticeNodesAttribute>())
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (Filter(method))
                    {
                        SearchTreeEntry searchTreeEntry = new(
                            new GUIContent($"{method.DeclaringType!.FullName}/{method.Name}"));
                        searchTreeEntry.userData = method;
                        searchTreeEntry.level = 1;
                        entries.Add(searchTreeEntry);
                    }
                }
            }
            return entries;
        }

        public bool OnSelectEntry(SearchTreeEntry SearchTreeEntry, SearchWindowContext context)
        {
            Callback?.Invoke((MethodInfo)SearchTreeEntry.userData);
            return true;
        }
    }
}
