using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using JetBrains.Annotations;
using Lattice.Base;
using Lattice.Editor.Manipulators;
using Lattice.Editor.Utils;
using Lattice.IR;
using Lattice.Nodes;
using Lattice.StandardLibrary;
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
        public const string LiftedToNullableUssClassName = UssClassName + "--nullable-lifted";

        public LatticeNode Target => (LatticeNode)base.NodeTarget;

        // Whether to show the output value and state under the node.
        private bool ShowDebugValue
        {
            get => EditorPrefs.GetBool($"DebugValue:{NodeTarget.Guid}");
            set => EditorPrefs.SetBool($"DebugValue:{NodeTarget.Guid}", value);
        }

        public bool IsShowingDebugValue => debugContainer.style.display == DisplayStyle.Flex;
        public bool IsShowingCompileInfo => compileDataContainer.style.display == DisplayStyle.Flex;

        private bool showCompileData; // Whether to show the compilation metadata under the node.
        private readonly Label compileInfo = new(); // Show the value of the node
        private readonly Label debugNodeValue = new(); // Show the node metadata like qualifiers

        // ReSharper disable once MemberCanBeProtected.Global
        public LatticeNodeView()
        {
            // Dragging support by clicking on IconBadge Errors:
            // This partially exists to improve RedirectNode edge creation,
            // because the port creation interface is hidden under the badges.
            // It's also nice UX in general!
            RegisterCallback<MouseDownEvent, LatticeNodeView>(static (e, args) =>
            {
                if (e.clickCount != 1 || e.target is not IconBadge badge)
                {
                    return;
                }
                PortView view = args.Query<PortView>().Where(p => p.HasMessageView(badge)).First();
                if (view == null)
                {
                    return;
                }
                var connector = (EdgeConnectionMouseManipulator)view.edgeConnector;
                connector.TryStartDragging(e);
            }, this);
        }

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
                this.Q(TitleContainerName).style.backgroundColor = new Color(0.0509f, 0.2784f, 0.2941f);
            }

            compileDataContainer.Add(compileInfo);
            debugContainer.Add(debugNodeValue);
            debugNodeValue.text = "Node not executed.";

            // If the graph is compiled and has this node in it.
            try
            {
                GraphCompilation c = GraphCompiler.RecompileIfNeeded();
                if (c.SourceGraphs.Contains((LatticeGraph)Owner.Graph))
                {
                    UpdateCompilationInfo(c);
                }
            } catch (Exception _) {
                //ignored
            }

            styleSheets.Add(
                AssetDatabase.LoadAssetAtPath<StyleSheet>(
                    "Packages/com.pontoco.lattice/Editor/UI/LatticeNodeView.uss"));
        }

        /// <inheritdoc />
        protected override void InitializeView()
        {
            base.InitializeView();

            debugContainer = new VisualElement { name = "debug" };
            debugContainer.style.display =
                ShowDebugValue ? DisplayStyle.Flex : DisplayStyle.None; // disabled by default
            mainContainer.Add(debugContainer);

            compileDataContainer = new VisualElement { name = "compileData" };
            compileDataContainer.style.display = DisplayStyle.None; // disabled by default
            mainContainer.Add(compileDataContainer);
        }

        /// <inheritdoc />
        public override void OnSelected()
        {
            base.OnSelected();
            Owner.NodeSelectionChanged();
        }

        /// <inheritdoc />
        public override void OnUnselected()
        {
            base.OnUnselected();
            Owner.NodeSelectionChanged();
        }

        public override bool RefreshAllPorts()
        {
            bool result = base.RefreshAllPorts();

            GraphCompilation compilation = null;
            try
            {
                compilation = GraphCompiler.AddAndRecompileIfNeeded((LatticeGraph)Owner.Graph);
            }
            catch (Exception _)
            {
                // ignored
            }

            bool liftedToNullable =
                compilation != null &&
                compilation.Mappings.TryGetValue((LatticeNode)NodeTarget, out var mapping) &&
                mapping.PrimaryNode.Node is FunctionIRNode fNode && fNode.NullableLiftedPorts.Any();
            EnableInClassList(LiftedToNullableUssClassName, liftedToNullable);

            // Update output port view types from the compilation.
            for (int i = 0, portIndex = 0; i < OutputPortViews.Count; i++)
            {
                PortView outputPortView = OutputPortViews[i];
                if (outputPortView.IsVirtual)
                {
                    // Don't factor virtual ports into compilation.
                    continue;
                }

                PortData outputPort = Target.OutputPorts[portIndex++].portData;
                outputPortView.UpdatePortView(outputPort);
                if (compilation != null)
                {
                    outputPortView.SetTypeFromCompilation(compilation);
                }
            }

            return result;
        }

        /// <inheritdoc />
        public override PortView AddPort(Direction direction, EdgeConnectorListener listener, PortData portData)
        {
            PortView p = base.AddPort(direction, listener, portData);

            if (p.IsVirtual)
            {
                // Don't factor virtual ports into compilation.
                return p;
            }

            // Update port type from compilation..
            try
            {
                GraphCompilation compilation = GraphCompiler.AddAndRecompileIfNeeded((LatticeGraph)Owner.Graph);
                p.SetTypeFromCompilation(compilation);
            }
            catch (Exception _)
            {
                Debug.LogError("Couldn't set type of port. Compilation failed.");
            }
            return p;
        }

        /// <summary>Displays the metadata generated during compilation of the graph for the node.</summary>
        /// <remarks>This should not call CompileNode(). It shouldn't cause compilation, just display.</remarks>
        public void UpdateCompilationInfo([CanBeNull] GraphCompilation compilation)
        {
            if (compilation == null || !compilation.Mappings.TryGetValue(Target, out GraphCompilation.Mapping mapping))
            {
                compileInfo.text = "Node not compiled in this execution.";
                return;
            }

            IRNode primaryNode = mapping.PrimaryNode.Node;
            Metadata compileData = compilation.CompileNode(primaryNode);

            string compileErrorText = null;
            if (compileData.CompilationError != null)
            {
                bool nodeOwnsError = compilation.IsNodeOwnedBy(compileData.CompilationError.Node, Target);
                compileErrorText = compilation.IsNodeOwnedBy(compileData.CompilationError.Node, Target)
                    ? "Node had compilation error.\n"
                    : $"Input node had compile error. [{compileData.CompilationError.Node}]\n";

                switch (compileData.InnerCompilationError)
                {
                    case LatticePortException portException when nodeOwnsError:
                    {
                        PortView port = GetPort(portException.PortIdentifier);
                        if (port == null)
                        {
                            // Port couldn't be found, fall back to default error formatting.
                            goto default;
                        }
                        port.AddMessageView(
                            portException is LatticePortRequirementException
                                ? "Port is required"
                                : portException.Message,
                            NodeMessageType.Error
                        );
                        break;
                    }
                    default:
                    {
                        AddMessageView(
                            nodeOwnsError
                                ? compileData.CompilationError.ToString()
                                : compileErrorText, NodeMessageType.Error);
                        break;
                    }
                }
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
            if (primaryNode is FunctionIRNode f)
            {
                b.Append("Nullable Ports: (").Append(string.Join(",", f.NullableLiftedPorts)).AppendLine(")");
            }

            compileInfo.text = b.ToString();
        }

        /// <summary>Displays the value output by the node during execution of the graph.</summary>
        /// <remarks>
        ///     This should not call CalculateNode() or CompileNode(). It shouldn't cause execution or compilation, just
        ///     display.
        /// </remarks>
        public void UpdateDebugValues([CanBeNull] IRExecution execution)
        {
            if (!IsShowingDebugValue)
            {
                return;
            }

            if (execution == null)
            {
                debugNodeValue.text = "Node not executed.";
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

            IRNode primaryNode = execution.Graph.Mappings[Target].PrimaryNode.Node;
            if (!execution.Graph.MetadataDb.TryGetValue(primaryNode, out Metadata compileData))
            {
                debugNodeValue.text = "Node was not compiled.";
                return;
            }

            if (!((LatticeGraphView)Owner).TryGetViewingEntity(execution.Graph, primaryNode, out Entity entity))
            {
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
                    IRNode stateDebugNode = execution.Graph.Mappings[Target].StateDebugNode!.Node;
                    if (execution.DebugData.Values.TryGetValue(entity, stateDebugNode, out object state))
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
            if (NodeTarget is ScriptNode scriptNode)
            {
                evt.menu.AppendAction("Edit Script", _ => SourceUtility.OpenAtMethod(scriptNode.Method.Resolve()));
            }

            evt.menu.AppendAction("Debug Value", e =>
                {
                    ShowDebugValue = !ShowDebugValue;
                    UpdateDebugValues(ExecutionHistory.MostRecent);
                    debugContainer.style.display = ShowDebugValue ? DisplayStyle.Flex : DisplayStyle.None;
                },
                _ => ShowDebugValue ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);

            evt.menu.AppendAction("Show Compile Info", e =>
                {
                    showCompileData = !showCompileData;
                    UpdateCompilationInfo(ExecutionHistory.MostRecent?.Graph);
                    compileDataContainer.style.display = showCompileData ? DisplayStyle.Flex : DisplayStyle.None;
                },
                _ => showCompileData ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);

            evt.menu.AppendSeparator();

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

            evt.menu.AppendAction("Find References", _ => FindReferences());

            evt.menu.AppendSeparator();
        }

        public void FindReferences()
        {
            StringBuilder b = new();
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(LatticeGraph)))
            {
                LatticeGraph graph = AssetDatabase.LoadAssetAtPath<LatticeGraph>(AssetDatabase.GUIDToAssetPath(guid));

                foreach (var node in graph.LatticeNodes<CrossRefNode>())
                {
                    if (node.ResolvedNode == Target)
                    {
                        b.AppendLine($"- [{node}]");
                    }
                }
            }

            bool found = b.Length > 0;
            if (!found)
            {
                Debug.Log($"No cross-graph references found to node: [{Target}]");
                return;
            }

            Debug.Log($"References to node [{Target}]: (Click for info)\n" + b);
        }
    }
}
