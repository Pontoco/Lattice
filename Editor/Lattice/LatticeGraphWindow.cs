using Lattice.Editor.Views;
using Lattice.Nodes;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lattice.Editor
{
    /// <summary>Renders and allows editing of a LatticeGraph.</summary>
    public class LatticeGraphWindow : BaseGraphWindow
    {
        public const string UssClassName = "lattice-graph-window";
        public const string GraphAreaUssClassName = UssClassName + "__graph-area";
        
        private LatticeGraphView graphView;

        // Kept so we can load the graph after a domain reload.
        [SerializeField]
        private LatticeGraph currentGraph;

        private VisualTreeAsset windowDocument =>
            AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Packages/com.pon.lattice/Editor/UI/LatticeGraphWindow.uxml");

        protected override void CreateGUI()
        {
            base.CreateGUI();
            
            graphView?.Dispose();
            
            // Create a new GraphView to render the graph.
            // This must be constructed before the toolbar below.
            graphView = new LatticeGraphView(this);
            
            VisualElement root = new() { pickingMode = PickingMode.Ignore };
            root.AddToClassList(UssClassName);
            root.StretchToParentSize();
            {
                root.Add(new LatticeGraphToolbar(graphView));
                
                VisualElement graphArea = new() { pickingMode = PickingMode.Ignore };
                graphArea.AddToClassList(GraphAreaUssClassName);
                {
                    graphArea.Add(graphView);
                    graphArea.Add(new NodePanelView());
                }
                root.Add(graphArea);
            }
            rootVisualElement.Add(root);

            // Reload the graph, after a domain reload.
            ReloadGraph();
        }

        private void OnAddedAsTab()
        {
            ReloadGraph();
        }

        internal void ReloadGraph()
        {
            if (currentGraph == null)
            {
                return;
            }

            graphView.LoadGraph(currentGraph);
            titleContent = new GUIContent(currentGraph.ToString());
        }

        [OnOpenAsset]
        private static bool OnOpenAsset(int instanceID, int line)
        {
            LatticeGraph graph = EditorUtility.InstanceIDToObject(instanceID) as LatticeGraph;
            if (graph == null)
            {
                return false;
            }

            OpenWindow(graph, !LatticePreferences.instance.OpenGraphAssetsInNewTab);
            return true;
        }

        /// <summary>Opens or focuses a window based on the input <paramref name="graph"/>.</summary>
        /// <param name="graph">The graph associated with the window.</param>
        /// <param name="useExistingWindowIfPresent">Opens the graph in an existing window if one's available.</param>
        /// <returns>The window related to the input graph.</returns>
        public static LatticeGraphWindow OpenWindow(LatticeGraph graph, bool useExistingWindowIfPresent)
        {
            LatticeGraphWindow graphWindow;
            if (useExistingWindowIfPresent)
            {
                graphWindow = GetWindow<LatticeGraphWindow>();
            }
            else
            {
                foreach (LatticeGraphWindow window in Resources.FindObjectsOfTypeAll<LatticeGraphWindow>())
                {
                    if (window.currentGraph != graph)
                        continue;
                    
                    // An existing window was found with the graph.
                    window.Show();
                    window.Focus();
                    return window;
                }
                
                graphWindow = CreateWindow<LatticeGraphWindow>(typeof(LatticeGraphWindow));
            }
            
            graphWindow.OpenGraph(graph);
            return graphWindow;
        }
        
        /// <summary>Opens or focuses a window based on the input <paramref name="node"/>.</summary>
        /// <param name="node">A node in a graph that will be associated with the window.</param>
        /// <returns>The window related to the input node.</returns>
        public static LatticeGraphWindow OpenWindow(LatticeNode node)
        {
            LatticeGraphWindow window = OpenWindow(node.Graph, false);
            BaseNodeView nodeView = window.graphView.NodeViewsPerNode[node];
            nodeView.Select((VisualElement)nodeView.GetFirstAncestorOfType<ISelection>(), false);
            window.rootVisualElement.schedule.Execute(() => window.graphView.FrameSelection()).StartingIn(10);
            return window;
        }

        private void OpenGraph(LatticeGraph graph)
        {
            currentGraph = graph;
            Show();
            ReloadGraph();
        }
        
        private void OnDestroy()
        {
            graphView?.Dispose();
            graphView = null;
        }
        
        private void OnDisable()
        {
            // Required, as OnDestroy is not called when reloading the domain.
            graphView?.Dispose();
            graphView = null;
        }
    }
}
