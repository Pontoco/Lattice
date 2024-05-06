using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Lattice.Base;
using Lattice.Editor.Utils;
using Lattice.IR;
using Lattice.Nodes;
using Lattice.Utils;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.UI;
using Unity.Mathematics;
using Unity.Properties;
using Unity.Serialization.Json;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UIElements;
using Assert = Unity.Assertions.Assert;

namespace Lattice.Editor.Views
{
    [NodeCustomEditor(typeof(LatticeNode))]
    public class LatticeNodeView : BaseNodeView
    {
        public LatticeNode Target => (LatticeNode)base.NodeTarget;

        private readonly Label compileInfo = new(); // Show the value of the node
        private readonly Label debugNodeValue = new(); // Show the node metadata like qualifiers

        /// <inheritdoc />
        public override void Initialize(BaseGraphView owner, BaseNode node)
        {
            base.Initialize(owner, node);

            if (Target.MalformedReason is not null and var reason)
            {
                AddMessageView(reason.Message, NodeMessageType.Error);
            }

            if (Target.ActionPorts.Count > 0)
            {
                this.Q("title").style.backgroundColor = new Color(0.0509f, 0.2784f, 0.2941f);
            }

            compileDataContainer.Add(compileInfo);
            debugContainer.Add(debugNodeValue);
            debugNodeValue.text = "Node not executed.";

            // If the graph is compiled and has this node in it.
            GraphCompilation c = GraphCompiler.RecompileIfNeeded();
            if (c.SourceGraphs.Contains((LatticeGraph)Owner.Graph))
            {
                UpdateCompilationInfo(c);
            }
            
            styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.pon.lattice/Editor/UI/LatticeNodeView.uss"));
        }

        public override bool RefreshAllPorts()
        {
            bool result = base.RefreshAllPorts();

            GraphCompilation compilation = GraphCompiler.AddAndRecompileIfNeeded((LatticeGraph)Owner.Graph);

            // Update output port view types from the compilation.
            for (int i = 0; i < outputPortViews.Count; i++)
            {
                PortData outputPort = Target.OutputPorts[i].portData;
                outputPortViews[i].UpdatePortView(outputPort);
                outputPortViews[i].SetTypeFromCompilation(compilation);
            }

            return result;
        }

        public override PortView AddPort(Direction direction, BaseEdgeConnectorListener listener, PortData portData)
        {
            PortView p = base.AddPort(direction, listener, portData);

            // Update port type from compilation..
            GraphCompilation compilation = GraphCompiler.AddAndRecompileIfNeeded((LatticeGraph)Owner.Graph);
            p.SetTypeFromCompilation(compilation);
            return p;
        }

        /// <summary>Displays the metadata generated during compilation of the graph for the node.</summary>
        /// <remarks>This should not call CompileNode(). It shouldn't cause compilation, just display.</remarks>
        public void UpdateCompilationInfo(GraphCompilation compilation)
        {
            if (!compilation.Mappings.ContainsKey(Target))
            {
                compileInfo.text = "Node not compiled in this execution.";
                return;
            }
            
            IRNode primaryNode = compilation.Mappings[Target].PrimaryNode;
            var compileData = compilation.CompileNode(primaryNode);

            string compileErrorText = null;
            if (compileData.CompilationError != null)
            {
                compileErrorText = compilation.IsNodeOwnedBy(compileData.CompilationError.Node, Target)
                    ? "Node had compilation error.\n"
                    : $"Input node had compile error. [{compileData.CompilationError.Node}]\n";
                AddMessageView(
                    compilation.IsNodeOwnedBy(compileData.CompilationError.Node, Target)
                        ? compileData.CompilationError.ToString()
                        : compileErrorText, NodeMessageType.Error);
            }

            if (!IsShowingCompileInfo)
            {
                return;
            }

            if (compileErrorText != null)
            {
                compileInfo.text = compileErrorText;
            }

            StringBuilder b = new();
            b.AppendLine(compileData.Qualifier == null
                ? "Qualifier: None"
                : $"Qualifier: {compileData.Qualifier}");

            b.Append("ExecuteAfter: ").Append(compileData.ExecutionPhase).AppendLine();
            b.Append("Type: ").AppendLine(GraphUtils.GetReadableTypeName(compileData.OutputType));
            b.Append("Nullable Ports: (").Append(string.Join(",", compileData.NullableLiftedPorts)).AppendLine(")");
            compileInfo.text = b.ToString();
        }

        /// <summary>Displays the value output by the node during execution of the graph.</summary>
        /// <remarks>
        ///     This should not call CalculateNode() or CompileNode(). It shouldn't cause execution or compilation, just
        ///     display.
        /// </remarks>
        public void UpdateDebugValues(IRExecution execution)
        {
            if (!IsShowingDebugValue) 
            {
                return;
            }
            
            if (!execution.Graph.Mappings.ContainsKey(Target))
            {
                debugNodeValue.text = "Node was not in executed graph.";
                return;
            }
            
            if (execution.DebugData == null)
            {
                debugNodeValue.text = "Graph was not compiled in debug mode.";
                return;
            }

            IRNode primaryNode = execution.Graph.Mappings[Target].PrimaryNode;
            if (!execution.Graph.MetadataDb.TryGetValue(primaryNode, out Metadata compileData))
            {
                debugNodeValue.text = "Node was not compiled.";
                return;
            }

            if (!((LatticeGraphView)Owner).TryGetViewingEntity(execution.Graph, primaryNode, out Entity entity)) {
                debugNodeValue.text = "No entity selected in toolbar.";
                return;
            }

            if (execution.DebugData.Values.TryGetValue(entity, primaryNode, out object value))
            {
                debugNodeValue.text =
                    value != null
                        ? value is Exception e
                            ? $"Node threw an {e.GetType().Name}."
                            : ObjectToDebugString(value)
                        : "null"; // todo: allow selecting entity in menu

                if (Target.StateType != null)
                {
                    if (execution.DebugData.Values.TryGetValue(entity, execution.Graph.GetState(Target), out object state))
                    {
                        debugNodeValue.text += "\nState: " + ObjectToDebugString(state);
                    }
                    else
                    {
                        Debug.LogWarning($"Node is missing state value. [{Target}]");
                    }
                }
            }
            else
            {
                debugNodeValue.text = "Node not executed.";
            }
        }

        private static JsonSerializationParameters debugJson = new JsonSerializationParameters()
        {
            Minified = true,
            Simplified = true,
            DisableRootAdapters = true,
            DisableSerializedReferences = true,
            DisableValidation = true,
            SerializedType = typeof(object)
        };

        public static string ObjectToDebugString(object value)
        {
            if (value == null)
            {
                return "null";
            }

            if (value is IList list)
            {
                StringBuilder builder = new();
                builder.Append("[");
                foreach (object v in list)
                {
                    builder.Append(ObjectToDebugString(v));
                    builder.Append(",");
                }
                builder.Append("]");
                return builder.ToString();
            }

            if (value is ITuple t)
            {
                StringBuilder builder = new();
                builder.Append("(");
                for (int i = 0; i < t.Length; i++)
                {
                    builder.Append(ObjectToDebugString(t[i]));
                    builder.Append(",");
                }
                builder.Append(")");
                return builder.ToString();
            }
            
            // Some types need to be formatted with 'roundtrip'
            if (value is float or float3 or float2)
            {
                // Format with 'roundtrip'
                return $"{value:R}";
            }

            // Some types implement ToString() sensibly.
            if (value.GetType().IsPrimitive || value.GetType().IsEnum || 
                value is int2 or int3 or Entity or Unit)
            {
                return $"{value}";
            }

            // All other types get serialized via JSON so we can see their fields (and nested fields).
            return JsonSerialization.ToJson(value, debugJson);
        }

        /// <inheritdoc />
        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            base.BuildContextualMenu(evt);

            evt.menu.InsertAction(evt.menu.MenuItems().Count,
                !Target.DoNotLogErrors ? "Disable Error Logging" : "Enable Error Logging",
                _ =>
                {
                    Target.DoNotLogErrors = !Target.DoNotLogErrors;
                });

            evt.menu.AppendAction("Show Selected In GraphViz", _ =>
            {
                var nodes = Owner.selection.Where(s => s is LatticeNodeView).Select(s => ((LatticeNodeView)s).Target);
                GraphCompiler.AddToCompilation(((LatticeGraphView)Owner).Graph);
                GraphCompilation graph = GraphCompiler.RecompileIfNeeded();
                var irNodes = nodes.SelectMany(n => graph.Mappings[n].Nodes).ToList();
                LatticeGraphToolbar.OpenGraphviz(GraphCompilation.ToDot(graph, irNodes));
            });
        }
    }
}
