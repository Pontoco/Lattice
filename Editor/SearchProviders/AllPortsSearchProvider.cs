using System;
using System.Collections.Generic;
using Lattice.Base;
using Lattice.Nodes;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace Lattice.Editor.SearchProviders
{
    public class AllPortsSearchProvider : ScriptableObject, ISearchWindowProvider
    {
        private static AllPortsSearchProvider instance;

        public static AllPortsSearchProvider Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = CreateInstance<AllPortsSearchProvider>();
                }

                return instance;
            }
        }

        public Action<NodePort> Callback;
        public Func<NodePort, bool> Filter = _ => true;

        /// <inheritdoc />
        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            List<SearchTreeEntry> entries = new()
            {
                new SearchTreeGroupEntry(new GUIContent("❤️ Create Node")),
            };
            foreach (var node in GetAllNodes())
            {
                foreach (var port in node.OutputPorts)
                {
                    if (Filter(port))
                    {
                        string label =
                            $"{node.Graph.name}/{node.Name}";
                        if (node.OutputPorts.Count > 1)
                        {
                            label += $"/{port.portData.identifier}";
                        }
                        SearchTreeEntry searchTreeEntry = new(new GUIContent(label))
                        {
                            userData = port,
                            level = 1
                        };
                        entries.Add(searchTreeEntry);
                    }
                }
            }
            return entries;
        }

        /// <inheritdoc />
        public bool OnSelectEntry(SearchTreeEntry SearchTreeEntry, SearchWindowContext context)
        {
            Callback?.Invoke((NodePort)SearchTreeEntry.userData);
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
