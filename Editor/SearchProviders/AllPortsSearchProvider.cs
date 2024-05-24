using System;
using System.Collections.Generic;
using System.Linq;
using Lattice.Base;
using Lattice.Nodes;
using Lattice.Utils;
using UnityEditor;
using UnityEngine;

namespace Lattice.Editor.SearchProviders
{
    /// <summary>The "Find Port" menu implementation.</summary>
    public class AllPortsSearchProvider : SearchMenuProvider<NodePort>
    {
        
        private static AllPortsSearchProvider instance;
        public static AllPortsSearchProvider Instance => instance ??= new AllPortsSearchProvider();

        /// <inheritdoc />
        protected override string Title => "Find Port";

        public Action<NodePort> Callback;
        public Func<NodePort, bool> Filter = _ => true;
        
        /// <inheritdoc />
        private AllPortsSearchProvider() : base(null) { }

        /// <inheritdoc />
        protected override void AddSearchEntries(List<SearchEntry> searchEntries)
        {
            foreach (var node in GetAllNodes())
            {
                foreach (NodePort port in node.OutputPorts.Where(Filter))
                {
                    string[] title;
                    if (node.OutputPorts.Count > 1)
                    {
                        title = new[]
                        {
                            node.Graph.name,
                            node.Name,
                            GraphUtils.NicifyIdentifierName(port.portData.identifier)
                        };
                    }
                    else
                    {
                        title = new[]
                        {
                            node.Graph.name,
                            node.Name
                        };
                    }
                        
                    SearchEntry entry = new()
                    {
                        Title = title,
                        Item = port
                    };
                    searchEntries.Add(entry);
                }
            }
        }

        /// <inheritdoc />
        protected override void OnSearcherSelectEntry(SearchEntry entry, Vector2 windowMousePosition)
        {
            Callback?.Invoke(entry.Item);
        }

        private static List<LatticeNode> GetAllNodes()
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
