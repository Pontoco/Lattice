using System;
using System.Collections.Generic;
using Lattice.Base;
using Lattice.Nodes;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace Lattice.Editor.SearchProviders
{
    public class AllNodesSearchProvider : ScriptableObject, ISearchWindowProvider
    {
        private static AllNodesSearchProvider instance;

        public static AllNodesSearchProvider Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = CreateInstance<AllNodesSearchProvider>();
                }

                return instance;
            }
        }

        public Action<BaseNode> Callback;
        public Func<BaseNode, bool> Filter = _ => true;

        /// <inheritdoc />
        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            List<SearchTreeEntry> entries = new()
            {
                new SearchTreeGroupEntry(new GUIContent("❤️ Create Node")),
            };
            foreach (var node in GetAllNodes())
            {
                if (Filter(node))
                {
                    SearchTreeEntry searchTreeEntry = new(
                        new GUIContent($"{node.Graph.name}.{node.DefaultName}"));
                    searchTreeEntry.userData = node;
                    searchTreeEntry.level = 1;
                    entries.Add(searchTreeEntry);
                }
            }
            return entries;
        }

        /// <inheritdoc />
        public bool OnSelectEntry(SearchTreeEntry SearchTreeEntry, SearchWindowContext context)
        {
            Callback?.Invoke((BaseNode)SearchTreeEntry.userData);
            return true;
        }

        public List<LatticeNode> GetAllNodes()
        {
            List<LatticeNode> result = new();
            var paths = AssetDatabase.FindAssets($"t:{nameof(LatticeGraph)}");

            foreach (var path in paths)
            {
                LatticeGraph graph =
                    AssetDatabase.LoadAssetAtPath<LatticeGraph>(AssetDatabase.GUIDToAssetPath(path));
                foreach (var node in graph.nodes)
                {
                    result.Add((LatticeNode)node);
                }
            }

            return result;
        }
    }
}
