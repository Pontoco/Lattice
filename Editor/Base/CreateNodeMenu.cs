using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Lattice.Base;
using Lattice.Editor.Utils;
using Lattice.Editor.Views;
using Lattice.Nodes;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lattice.Editor
{
    /// <summary>The "Create Node" menu implementation.</summary>
    public sealed class CreateNodeMenuProvider : SearchMenuProvider<INodeTemplate>
    {
        internal const string TitleText = "Create Node";
        
        protected override string Title => TitleText;

        private LatticeGraph Graph
        {
            set
            {
                if (graph == value)
                {
                    return;
                }
                graph = value;
                RegenerateEntries = true;
            }
        }

        private LatticeGraph graph;
        private readonly LatticeGraphView graphView;

        [CanBeNull]
        private PortView sourcePort;

        public CreateNodeMenuProvider(EditorWindow editorWindow, LatticeGraphView graphView) : base(editorWindow)
        {
            Graph = graphView.Graph;
            this.graphView = graphView;
        }

        /// <summary>Shows the node creation window.</summary>
        /// <param name="mousePosition">The position to show the menu and create any nodes under.</param>
        /// <param name="port">An optional port to filter results, and connect to once a node is created.</param>
        public void Show(Vector2 mousePosition, [CanBeNull] PortView port)
        {
            sourcePort = port;
            // Ensure the data is up-to-date with the graph within the view before we show the menu.
            Graph = graphView.Graph;
            base.Show(mousePosition);
        }

        /// <inheritdoc />
        public override void Show(Vector2 mousePosition) => Show(mousePosition, null);

        /// <inheritdoc />
        protected override void AddSearchEntries(List<SearchEntry> searchEntries)
        {
            if (sourcePort != null)
            {
                searchEntries.Add(new SearchEntry
                {
                    Item = new BasicNodeBuilder(typeof(RedirectNode)),
                    Title = new[] { "Redirect" }
                });
            }
            
            // Build up data structure containing group & title as an array of strings (the last one is the actual title) and associated node type.
            foreach ((string path, INodeTemplate builder) in graphView.FilterCreateNodeMenuEntries().OrderBy(k => k.path))
            {
                string[] title = path.Split('/');

                // Ensure that there's no blank elements in the title path.
                for (int i = 0; i < title.Length; i++)
                {
                    if (title[i] == "")
                    {
                        title[i] = "Uncategorized";
                    }
                }

                AddEntriesFromTemplate(builder, title, searchEntries);
            }
        }

        /// <summary>Adds search entries from the node template. Multiple entries can be added if a source port is specified.</summary>
        private void AddEntriesFromTemplate(INodeTemplate node, string[] title, List<SearchEntry> nodeEntries)
        {
            // Add the node entries without considering the sourcePort.
            // In later versions of Lattice, we may consider adding the capacity to filter entries based on the port we dragged to create this menu.
            nodeEntries.Add(new SearchEntry
            {
                Item = node,
                Title = title
            });
        }

        /// <summary>Once an entry is selected, this creates the node and connects any required edges.</summary>
        protected override void OnSearcherSelectEntry(SearchEntry searchEntry, Vector2 windowMousePosition)
        {
            if (searchEntry.Item == null)
            {
                return;
            }

            Vector2 graphMousePosition = graphView.contentViewContainer.WorldToLocal(windowMousePosition);

            Undo.RegisterCompleteObjectUndo(graphView.Graph, "Added " + searchEntry.Item.NodeType);

            BaseNode node = searchEntry.Item.Build();
            node.OnNodeCreated();

            BaseNodeView view = graphView.AddNode(node);
            view.SetPosition(graphMousePosition);

            if (sourcePort != null)
            {
                // Connect the node to the port if it's found.
                if (sourcePort.direction == Direction.Input)
                {
                    // Get the first valid port.
                    if (TryGetFirstValidPort(view.OutputPortViews, sourcePort, out PortView connectTo))
                    {
                        graphView.CreateNewEdge(connectTo, sourcePort);
                    }
                }
                else
                {
                    if (TryGetFirstValidPort(view.InputPortViews, sourcePort, out PortView connectTo))
                    {
                        graphView.CreateNewEdge(sourcePort, connectTo);
                    }
                }

                bool TryGetFirstValidPort(IEnumerable<PortView> views, PortView from, out PortView connectTo)
                {
                    if (from.PortType == null)
                    {
                        connectTo = null;
                        return false;
                    }

                    if (node is RedirectNode)
                    {
                        // Port types aren't initialized for RedirectNodes until edges are connected.
                        connectTo = views.FirstOrDefault();
                        return connectTo != null;
                    }

                    // Get the first port that we can assign this port's type to.
                    connectTo = views.FirstOrDefault(p => p.PortType != null && p.PortType.IsAssignableFrom(from.PortType));
                    return connectTo != null;
                }
            }
        }
    }
}
