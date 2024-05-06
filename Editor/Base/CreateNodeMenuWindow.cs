using System.Collections.Generic;
using System.Linq;
using Lattice.Base;
using Lattice.Editor.Utils;
using Lattice.Editor.Views;
using Lattice.Nodes;
using Lattice.Utils;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lattice.Editor
{
    // TODO: replace this by the new UnityEditor.Searcher package
    internal class CreateNodeMenuWindow : ScriptableObject, ISearchWindowProvider
    {
        private BaseGraphView graphView;
        private EditorWindow window;
        private Texture2D icon;
        private EdgeView edgeFilter;
        private PortView inputPortView;
        private PortView outputPortView;

        public void Initialize(BaseGraphView graphView, EditorWindow window, EdgeView edgeFilter = null)
        {
            this.graphView = graphView;
            this.window = window;
            this.edgeFilter = edgeFilter;
            inputPortView = edgeFilter?.input as PortView;
            outputPortView = edgeFilter?.output as PortView;

            // Transparent icon to trick search window into indenting items
            if (icon == null)
            {
                icon = new Texture2D(1, 1);
            }
            icon.SetPixel(0, 0, new Color(0, 0, 0, 0));
            icon.Apply();
        }

        private void OnDestroy()
        {
            if (icon != null)
            {
                DestroyImmediate(icon);
                icon = null;
            }
        }

        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            var tree = new List<SearchTreeEntry>
            {
                new SearchTreeGroupEntry(new GUIContent("❤️ Create Node")),
            };

            if (edgeFilter == null)
            {
                CreateStandardNodeMenu(tree);
            }
            else
            {
                CreateEdgeNodeMenu(tree);
            }

            return tree;
        }

        private void CreateStandardNodeMenu(List<SearchTreeEntry> tree)
        {
            // Sort menu by alphabetical order and submenus
            var nodeEntries = graphView.FilterCreateNodeMenuEntries().OrderBy(k => k.path);
            var titlePaths = new HashSet<string>();

            foreach (var nodeMenuItem in nodeEntries)
            {
                var nodePath = nodeMenuItem.path;
                var nodeName = nodePath;
                var level = 0;
                var parts = nodePath.Split('/');

                if (parts.Length > 1)
                {
                    level++;
                    nodeName = parts[parts.Length - 1];
                    var fullTitleAsPath = "";

                    for (var i = 0; i < parts.Length - 1; i++)
                    {
                        var title = parts[i];
                        fullTitleAsPath += title;
                        level = i + 1;

                        // Add section title if the node is in subcategory
                        if (!titlePaths.Contains(fullTitleAsPath))
                        {
                            tree.Add(new SearchTreeGroupEntry(new GUIContent(title))
                            {
                                level = level
                            });
                            titlePaths.Add(fullTitleAsPath);
                        }
                    }
                }

                tree.Add(new SearchTreeEntry(new GUIContent(nodeName, icon))
                {
                    level = level + 1,
                    userData = nodeMenuItem.builder
                });
            }
        }

        private void CreateEdgeNodeMenu(List<SearchTreeEntry> tree)
        {
            var entries = NodeProvider.GetEdgeCreationNodeMenuEntry((edgeFilter.input ?? edgeFilter.output) as PortView,
                graphView.Graph);

            var titlePaths = new HashSet<string>();

            var nodePaths = NodeProvider.GetNodeMenuEntries(graphView.Graph);

            var sortedMenuItems = entries
                                  .Select(port => (port,
                                      nodePaths.FirstOrDefault(kp => kp.builder == port.nodeBuilder).path))
                                  .OrderBy(e => e.path);

            // Sort menu by alphabetical order and submenus
            foreach (var nodeMenuItem in sortedMenuItems)
            {
                var nodePath = nodePaths.FirstOrDefault(kp => kp.builder == nodeMenuItem.port.nodeBuilder).path;

                // Ignore the node if it's not in the create menu
                if (string.IsNullOrEmpty(nodePath))
                {
                    continue;
                }

                var nodeName = nodePath;
                var level = 0;
                var parts = nodePath.Split('/');

                if (parts.Length > 1)
                {
                    level++;
                    nodeName = parts[parts.Length - 1];
                    var fullTitleAsPath = "";

                    for (var i = 0; i < parts.Length - 1; i++)
                    {
                        var title = parts[i];
                        fullTitleAsPath += title;
                        level = i + 1;

                        // Add section title if the node is in subcategory
                        if (!titlePaths.Contains(fullTitleAsPath))
                        {
                            tree.Add(new SearchTreeGroupEntry(new GUIContent(title))
                            {
                                level = level
                            });
                            titlePaths.Add(fullTitleAsPath);
                        }
                    }
                }

                tree.Add(new SearchTreeEntry(new GUIContent($"{nodeName}:  {nodeMenuItem.port.portDisplayName}", icon))
                {
                    level = level + 1,
                    userData = nodeMenuItem.port
                });
            }
        }

        // On selecting a choice from the node creation menu.
        public bool OnSelectEntry(SearchTreeEntry searchTreeEntry, SearchWindowContext context)
        {
            // window to graph position
            var windowRoot = window.rootVisualElement;
            var windowMousePosition = windowRoot.ChangeCoordinatesTo(windowRoot.parent,
                context.screenMousePosition - window.position.position);
            var graphMousePosition = graphView.contentViewContainer.WorldToLocal(windowMousePosition);

            var nodeBuilder = searchTreeEntry.userData is INodeTemplate
                ? (INodeTemplate)searchTreeEntry.userData
                : ((NodeProvider.NodeCreationByPort)searchTreeEntry.userData).nodeBuilder;

            Undo.RegisterCompleteObjectUndo(graphView.Graph, "Added " + nodeBuilder.NodeType);

            var node = nodeBuilder.Build();
            ExceptionToLog.LogExceptions(node.OnNodeCreated);

            var view = graphView.AddNode(node);
            view.SetPosition(graphMousePosition);

            // If the node menu was opened by dragging from a port, connect it to the new node.
            if (searchTreeEntry.userData is NodeProvider.NodeCreationByPort desc)
            {
                var targetPort = view.GetPort(desc.portIdentifier);
                if (inputPortView == null)
                {
                    graphView.CreateNewEdge(targetPort, outputPortView);
                }
                else
                {
                    graphView.CreateNewEdge(inputPortView, targetPort);
                }
            }

            return true;
        }
    }
}
