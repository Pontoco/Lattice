using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Lattice.Base;
using Lattice.Editor.Events;
using Lattice.IR;
using Lattice.StandardLibrary;
using Lattice.Utils;
using Unity.Entities;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using Enumerable = System.Linq.Enumerable;

namespace Lattice.Editor.Views
{
    public partial class LatticeGraphView : BaseGraphView
    {
        public const string UssClassName = "lattice-graph-view";

        public new LatticeGraph Graph => (LatticeGraph)base.Graph;
        public new IEnumerable<LatticeNodeView> NodeViews => Enumerable.Cast<LatticeNodeView>(base.NodeViews);

        /// <summary>
        ///     A lattice graph view may be viewing multiple entities at the same time, if a node in the graph references
        ///     another entity graph. It's a bit of an edge case, but important to support.
        /// </summary>
        public Dictionary<Qualifier, Entity> ViewingEntities = new();

        private enum PortTooltipState
        {
            None,
            All,
            Solo
        }

        private readonly GraphViewTooltipManipulator tooltipManipulator = new();

        public LatticeGraphView(EditorWindow window) : base(window)
        {
            AddToClassList(UssClassName);

            GridBackground gridBackground = new();
            gridBackground.StretchToParentSize();
            Insert(0, gridBackground);

            styleSheets.Add(
                AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.pontoco.lattice/Editor/UI/LatticeGraphView.uss"));

            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);

            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                LatticeDebugUpdateSystem.OnLatticeExecute += DisplayExecution;
                RegisterCallback<ToggleEdgeHiddenEvent>(OnToggleEdgeHidden);
                this.AddManipulator(tooltipManipulator);
            });

            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                LatticeDebugUpdateSystem.OnLatticeExecute -= DisplayExecution;
                UnregisterCallback<ToggleEdgeHiddenEvent>(OnToggleEdgeHidden);
                this.RemoveManipulator(tooltipManipulator);
            });
        }

        // Used to set the graph. 
        public override void LoadGraph(BaseGraph graph)
        {
            base.LoadGraph(graph);

            // Update display with compiled metadata
            GraphCompiler.AddToCompilation(Graph);
            GraphCompilation compilation = GraphCompiler.RecompileIfNeeded();
            DisplayCompilation(compilation);

            // Access the most recent execution so we can display debug values.
            IRExecution execution = ExecutionHistory.MostRecent;

            if (execution != null && !execution.Graph.SourceGraphs.Contains(Graph))
            {
                execution = null;
            }

            if (execution != null)
            {
                // Update debug values with most recent execution.
                DisplayExecution(execution);
            }

            GraphCompiler.OnGraphCompilation += OnCompilation;
        }

        public override void UnloadGraph()
        {
            GraphCompiler.OnGraphCompilation -= OnCompilation;
            base.UnloadGraph();
        }

        /// <inheritdoc/>
        protected override void GraphChangesCallback(GraphChanges obj)
        {
            base.GraphChangesCallback(obj);

            if (obj.nodeChanged is IBakedLatticeNode || obj.addedNode is IBakedLatticeNode ||
                obj.removedNode is IBakedLatticeNode ||
                (obj.entireGraphInitialized != null &&
                 obj.entireGraphInitialized.nodes.Any(n => n is IBakedLatticeNode)))
            {
                // For graphs with baked nodes, we have to save this asset to disk when they change. Otherwise the 
                // dependent subscenes will not be re-imported. This is costly, so we only set this when we modify nodes
                // that are baked. 
                //
                // Theoretically, we could skip this when in live conversion mode. More specifically, we don't 
                // need to save the graph, if the *only* usage of the graph in the project is in an open sub-scene.
                // That's pretty rare though, probably.
                Graph.NeedsSaveBeforeRecompile = true;
            }

            // Recompile the global graph when this graph is changed. 
            if (obj.removedEdge != null || obj.addedEdge != null || obj.removedNode != null || obj.addedNode != null ||
                obj.nodeChanged != null)
            {
                GraphCompiler.RecompileIfNeeded(true);
            }
        }

        private void OnCompilation(GraphCompilation compilation)
        {
            DisplayCompilation(compilation);
        }

        public void DisplayExecution(IRExecution context)
        {
            if (!context.Graph.SourceGraphs.Contains(Graph) || context.DebugData == null)
            {
                return;
            }

            // Set the viewer to show the first valid entity that was executed, otherwise just show globals.
            var entitiesForGraph = context.EntitiesInGraph(Graph);

            foreach (var (q, entities) in entitiesForGraph)
            {
                if (!ViewingEntities.ContainsKey(q) // haven't selected a viewing entity
                    || ViewingEntities[q] == Entity.Null
                    || !entities.Contains(ViewingEntities[q]) // current viewing entity is invalid
                   )
                {
                    foreach (Entity en in entities)
                    {
                        if (en != Entity.Null)
                        {
                            // todo: this only works because the toolbar uses imgui, but if we move to fully retained, 
                            // we'll have to issue some dirtying event here.
                            ViewingEntities[q] = en;
                            break;
                        }
                    }
                }
            }

            // Update debug info for each node.
            foreach (LatticeNodeView node in NodeViews)
            {
                if (context.DebugData == null)
                {
                    node.AddMessageView("Graph was not compiled in debug mode.", NodeMessageType.Warning);
                    continue;
                }

                node.ClearMessageViewsFromNodeAndPorts();

                node.UpdateCompilationInfo(context.Graph);
                node.UpdateDebugValues(context);

                var nodeTarget = node.Target;
                if (context.Graph.Mappings.TryGetValue(nodeTarget, out var mapping))
                {
                    foreach (IRNode irnode in mapping.Nodes)
                    {
                        if (TryGetViewingEntity(context.Graph, irnode, out Entity entity) &&
                            context.DebugData.Values.TryGetValue(entity, irnode, out object val))
                        {
                            if (val is Exception e)
                            {
                                node.AddMessageView(e.ToString(), NodeMessageType.Error);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        ///     Gets the entity the graph is currently set to view, for this specific node. Returns false if no entity has
        ///     been chosen.
        /// </summary>
        public bool TryGetViewingEntity(GraphCompilation context, IRNode node, out Entity viewingEntity)
        {
            var q = context.CompileNode(node).Qualifier;
            if (q == null)
            {
                viewingEntity = Entity.Null;
                return true;
            }
            if (ViewingEntities.TryGetValue(q.Value, out viewingEntity))
            {
                if (viewingEntity == Entity.Null)
                {
                    Debug.LogWarning($"Viewing entity shouldn't be null. [{q}]");
                }
                return true;
            }

            return false;
        }

        /// <summary>Updates the visual display with information from the compiled form of the graph.</summary>
        public void DisplayCompilation(GraphCompilation compilation)
        {
            if (!compilation.SourceGraphs.Contains(Graph))
            {
                return;
            }

            // Update debug compilation info.
            foreach (LatticeNodeView node in NodeViews)
            {
                node.ClearMessageViewsFromNodeAndPorts();

                if (!compilation.Mappings.ContainsKey(node.Target))
                {
                    Debug.LogError(!Graph.nodes.Contains(node.Target)
                        ? $"Dangling NodeView without node in graph. [{node}]"
                        : $"Node was not converted to any IR Nodes. [{node.Target}]", Graph);

                    continue;
                }

                node.UpdateCompilationInfo(compilation);
            }
        }

        /// <inheritdoc />
        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            base.BuildContextualMenu(evt);

            evt.menu.AppendAction("Dump Execution Values", _ =>
            {
                var ex = ExecutionHistory.MostRecent;
                if (ex == null)
                {
                    Debug.Log("No execution to dump.");
                    return;
                }
                if (ex.DebugData == null)
                {
                    Debug.Log("Graph not compiled in debug mode.");
                    return;
                }

                StringBuilder b = new();
                foreach ((Entity entity, IRNode irNode, object item3) in ex.DebugData.Values.OrderBy(x => x.Item2.ToString()))
                {
                    int idx = ex.Graph.NodeIndices[irNode];
                    b.AppendLine($"[{idx}]{irNode}:{entity} -> \t{LatticeNodeView.ObjectToDebugString(item3)}");
                }

                var path = FileUtil.GetUniqueTempPathInProject() + ".txt";
                File.WriteAllText(path, b.ToString());

                EditorUtility.OpenWithDefaultApp(path);
            });

            evt.menu.AppendAction("Force Save", _ =>
            {
                EditorUtility.SetDirty(Graph);
                AssetDatabase.SaveAssetIfDirty(Graph);
            });
        }

        /// <summary> Toggles the hidden state of edges when a <see cref="ToggleEdgeHiddenEvent" /> is sent. </summary>
        private void OnToggleEdgeHidden(ToggleEdgeHiddenEvent evt)
        {
            // Only if all edges are hidden we should show them (i.e. preference the hidden state).
            bool toggleTo = !selection.OfType<EdgeView>().All(e => e.IsHidden);

            foreach (EdgeView edge in selection.OfType<EdgeView>())
            {
                edge.IsHidden = toggleTo;
            }
        }
    }
}
