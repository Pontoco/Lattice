using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Lattice.Base;
using Lattice.Editor.Manipulators;
using Lattice.Editor.Utils;
using Lattice.IR;
using Lattice.Nodes;
using Lattice.Utils;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using Port = UnityEditor.Experimental.GraphView.Port;

namespace Lattice.Editor.Views
{
    /// <summary>Location of the container the port has been added to.</summary>
    public enum PortViewLocation
    {
        Top,
        Bottom,
        Left,
        Right,
        State,
        BottomLeft
    }

    /// <summary>A VisualElement to render a port on a node.</summary>
    public sealed partial class PortView : Port, IHasGraphTooltip
    {
        public const string UssClassName = "port";
        public const string NullableUssClassName = UssClassName + "--is-nullable";
        public const string ConnectedUssClassName = UssClassName + "--connected";

        /// <summary>A half-circle element that indicates this element is nullable.</summary>
        private readonly VisualElement nullableHalfCircle;

        private bool isNullableType;

        /// <summary>The type associated with the port.</summary>
        public Type PortType
        {
            get => portType;
            set
            {
                portType = value;
                if (portType == null)
                {
                    visualClass = null;
                    IsNullableType = false;
                    nullableHalfCircle.ClearClassList();
                }
                else
                {
                    if (portType.IsNullable())
                    {
                        Type innerPortType = portType.GetGenericArguments()[0];
                        if (visualClass != null)
                        {
                            nullableHalfCircle.RemoveFromClassList(visualClass);
                        }
                        visualClass = $"{UssClassName}--{innerPortType.Name}";
                        nullableHalfCircle.AddToClassList(visualClass);
                        IsNullableType = true;
                    }
                    else
                    {
                        visualClass = $"{UssClassName}--{portType.Name}";
                        IsNullableType = false;
                        nullableHalfCircle.ClearClassList();
                    }
                }
            }
        }

        /// <summary>Whether <see cref="PortType" /> is a nullable type.</summary>
        public bool IsNullableType
        {
            get => isNullableType;
            private set
            {
                if (isNullableType == value)
                {
                    return;
                }
                isNullableType = value;
                EnableInClassList(NullableUssClassName, value);
            }
        }

        /// <summary>The parent node. Can be null if the port was created in an entirely readonly mode.</summary>
        [CanBeNull]
        public BaseNodeView Owner { get; }

        public PortData PortData;

        public string Identifier => PortData.identifier;

        public event Action<PortView, Edge> OnConnected;
        public event Action<PortView, Edge> OnDisconnected;

        public IEnumerable<EdgeView> Edges => connections.Select(e => (EdgeView)e);

        public string Tooltip { get; set; }

        /// <summary>The location the port has been added to based on the members of <see cref="PortData" />.</summary>
        public PortViewLocation Location
        {
            get
            {
                if (direction == Direction.Input)
                {
                    if (PortData.vertical && PortData.secondaryPort)
                    {
                        return PortViewLocation.State;
                    }
                    return PortData.vertical ? PortViewLocation.Top : PortViewLocation.Left;
                }

                if (PortData.vertical && PortData.secondaryPort)
                {
                    return PortViewLocation.BottomLeft;
                }
                return PortData.vertical ? PortViewLocation.Bottom : PortViewLocation.Right;
            }
        }

        private PortView([CanBeNull] BaseNodeView owner, Direction direction, PortData portData)
            : base(portData.vertical ? Orientation.Vertical : Orientation.Horizontal, direction, Capacity.Multi,
                portData.defaultType)
        {
            // Add a half-circle element that indicates this element is nullable.
            // Must be added before setting other port parameters.
            m_ConnectorBox.Add(nullableHalfCircle = new VisualElement
                {
                    name = "nullableOverlay",
                    pickingMode = PickingMode.Ignore
                }
            );

            Owner = owner;
            PortType = portData.defaultType;
            PortData = portData;

            styleSheets.Add(
                AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.pon.lattice/Editor/UI/PortView.uss"));

            UpdatePortView(portData);

            this.AddManipulator(new SelectConnectedEdgesManipulator());
        }

        /// <inheritdoc />
        public override bool ContainsPoint(Vector2 localPoint)
        {
            // Fix-up picking that was overridden by Port by reverting to the default behaviour.
            Rect layout = this.layout;
            Rect rect = new(0, 0, layout.width, layout.height);
            return rect.Contains(localPoint);
        }

        /// <summary>Calculates and updates the correct color of the port.</summary>
        private void UpdateColor()
        {
            if (PortType == null)
            {
                portColor = Color.gray;
                return;
            }

            // Set the color randomly based on the type.
            // if (owner.Owner.ColorByDataType)
            // {
            //     portColor = UniqueColor(PortType.FullName);
            // }

            // Ref portViews are always blue.
            if (PortData.isRefType)
            {
                if (direction == Direction.Input)
                {
                    portColor = new Color(0.45f, 0.59f, 0.67f);
                }
                else
                {
                    portColor = new Color(0.47f, 0.82f, 1f);
                }
            }
        }

        public void SetTypeFromCompilation(GraphCompilation compilation)
        {
            if (Owner!.NodeTarget is LatticeNode lnode)
            {
                if (!compilation.Mappings.ContainsKey(lnode))
                {
                    Debug.LogWarning($"Couldn't update port type. Node was not present in compilation? [{lnode}]");
                    return;
                }
                if (compilation.Mappings[lnode].OutputPortMap.TryGetValue(PortData.identifier, out IRNode irNode))
                {
                    Type outputType = compilation.CompileNode(irNode).OutputType;

                    // Ignore exceptions. Even if the node is malformed, we don't want to set the ports to exception type.
                    // That'll break the edge connections.
                    if (!typeof(Exception).IsAssignableFrom(outputType))
                    {
                        PortType = outputType;
                    }
                }
            }
        }

        /// <summary>PortView factory. <paramref name="owner"/> can be null if the port is spawned as a readonly view.</summary>
        public static PortView CreatePortView([CanBeNull] BaseNodeView owner, Direction direction, PortData portData,
                                              BaseEdgeConnectorListener edgeConnectorListener)
        {
            PortView pv = new(owner, direction, portData)
            {
                m_EdgeConnector = new EdgeConnectionMouseManipulator(edgeConnectorListener)
            };

            // Only allow connections to ports that have an owner.
            if (owner != null)
            {
                pv.AddManipulator(pv.m_EdgeConnector);
            }
            return pv;
        }

        /// <summary>Update the size of the port view (using the portData.sizeInPixel property)</summary>
        public void UpdatePortSize()
        {
            int size = PortData.sizeInPixel != 0 ? PortData.sizeInPixel : LatticeNode.PortSizePrimaryValue;
            m_ConnectorBox.style.width = size;
            m_ConnectorBox.style.height = size;
            m_ConnectorBoxCap.style.width = size - 4;
            m_ConnectorBoxCap.style.height = size - 4;

            // Update connected edge sizes:
            foreach (EdgeView e in Edges)
            {
                e.UpdateEdgeSize();
            }
        }

        public override void Connect(Edge edge)
        {
            OnConnected?.Invoke(this, edge);

            base.Connect(edge);

            if (edge.input is PortView inputView)
            {
                inputView.Owner!.OnPortConnected(inputView);
            }

            if (edge.output is PortView outputView)
            {
                outputView.Owner!.OnPortConnected(outputView);
            }

            AddToClassList(ConnectedUssClassName);
        }

        public override void Disconnect(Edge edge)
        {
            OnDisconnected?.Invoke(this, edge);

            base.Disconnect(edge);

            BaseNodeView inputNode = (edge.input as PortView)?.Owner;
            BaseNodeView outputNode = (edge.output as PortView)?.Owner;

            inputNode?.OnPortDisconnected(edge.input as PortView);
            outputNode?.OnPortDisconnected(edge.output as PortView);

            EnableInClassList(ConnectedUssClassName, connected);
        }

        /// <inheritdoc />
        public override void DisconnectAll()
        {
            base.DisconnectAll();
            RemoveFromClassList(ConnectedUssClassName);
        }

        public void UpdatePortView(PortData data)
        {
            PortData = data;

            // Only apply the port type if we don't have one set from the compilation.
            if (PortType == null && data.defaultType != null)
            {
                PortType = data.defaultType;
            }

            portName = !string.IsNullOrEmpty(data.displayName) ? data.displayName : PortData.identifier;

            if (PortData.vertical)
            {
                AddToClassList("Vertical");
            }
            else
            {
                RemoveFromClassList("Vertical");
            }

            Tooltip = PortData.customTooltip ??
                      (PortType != null
                          ? $"{GraphUtils.GetReadableTypeNameWithColor(PortType)} {NicifyIdentifierName(Identifier)}"
                          : NicifyIdentifierName(Identifier));

            UpdatePortSize();

            // Update the edge in case the port color have changed
            schedule.Execute(() =>
            {
                foreach (EdgeView edge in Edges)
                {
                    edge.UpdateEdgeControl();
                    edge.MarkDirtyRepaint();
                }
            }).ExecuteLater(50); // Hummm

            return;

            string NicifyIdentifierName(string input)
            {
                if (input.EndsWith("_in", StringComparison.Ordinal))
                {
                    return input[..^"_in".Length];
                }
                if (input.EndsWith("_out", StringComparison.Ordinal))
                {
                    return input[..^"_out".Length];
                }

                // Force the identifier to Pascal case.
                if (input.Length >= 1 && char.IsLower(input[0]))
                {
                    input = $"{char.ToUpper(input[0])}{input[1..]}";
                }
                return input;
            }
        }

        /// <summary>Returns a unique color based on the hash of the value. Tuned to be visible against a gray background.</summary>
        private static Color UniqueColor(object value)
        {
            uint hash = (uint)value.GetHashCode();

            // range: full integer, generate 3 numbers based on the hash: 
            uint lightness = JenkinsOneAtATimeHash(hash);
            uint aChroma = JenkinsOneAtATimeHash(lightness);
            uint bChroma = JenkinsOneAtATimeHash(aChroma);

            float l = MapUnitToRange((float)(lightness / (double)uint.MaxValue), 45, 100);
            float a = MapUnitToRange((float)(aChroma / (double)uint.MaxValue), -100f, 100);
            float b = MapUnitToRange((float)(bChroma / (double)uint.MaxValue), -100f, 100);

            Color color = new LabColor(l, a, b).ToColor();
            Debug.Log($"{l}  {a}  {b}  --  {hash} -- {lightness} {aChroma} {bChroma} -- {value}");
            return color;
        }

        /// <summary>Linearly maps a given value [0,1] to the range start..end.</summary>
        /// <param name="clamp">
        ///     Whether to force the final value to be within start/end or to allow the linear interpolation to
        ///     extend outside (if value > 1, for instance).
        /// </param>
        /// <returns></returns>
        public static float MapUnitToRange(float unitValue, float start, float end, bool clamp = false)
        {
            if (clamp)
            {
                unitValue = Clamp(unitValue, 0, 1);
            }

            return start + (end - start) * unitValue;
        }

        /// <summary>Clamps a value to be between the given boundaries, inclusive.</summary>
        /// <param name="value">Some value (-inf, +inf).</param>
        /// <param name="min">(-inf, +inf)</param>
        /// <param name="max">(-inf, +inf)</param>
        /// <returns>A value [min, max].</returns>
        public static float Clamp(float value, float min, float max)
        {
            return Math.Min(max, Math.Max(min, value));
        }

        public static uint JenkinsOneAtATimeHash(uint input)
        {
            uint hash = 0;

            hash += input;
            hash += hash << 10;
            hash ^= hash >> 6;

            hash += hash << 3;
            hash ^= hash >> 11;
            hash += hash << 15;

            return hash;
        }

        /// <inheritdoc />
        GraphTooltipView IHasGraphTooltip.CreateTooltipView()
        {
            var tooltipView = new PortTooltipView(this);
            // Add a USS class based on the port location.
            tooltipView.AddToClassList($"{GraphTooltipView.UssClassName}--{Location.ToString().ToLowerInvariant()}");
            return tooltipView;
        }

        /// <inheritdoc />
        void IHasGraphTooltip.PositionTooltip(GraphTooltipView tooltipElement)
        {
            GraphTooltipView.Position position = Location switch
            {
                PortViewLocation.Top => GraphTooltipView.Position.Top,
                PortViewLocation.Bottom or PortViewLocation.BottomLeft => GraphTooltipView.Position.Bottom,
                PortViewLocation.Left or PortViewLocation.State => GraphTooltipView.Position.Left,
                PortViewLocation.Right => GraphTooltipView.Position.Right,
                _ => throw new ArgumentOutOfRangeException()
            };
            // Position the tooltip based on the port location.
            tooltipElement.SetPosition(this, position);
        }

        /// <summary>Manipulator that selects connected edges on click.</summary>
        private sealed class SelectConnectedEdgesManipulator : Manipulator
        {
            private GraphView parentGraph;

            /// <inheritdoc />
            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<ClickEvent>(OnClick);
            }

            /// <inheritdoc />
            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<ClickEvent>(OnClick);
            }

            private void OnClick(ClickEvent evt)
            {
                parentGraph ??= target.GetFirstAncestorOfType<GraphView>();
                // Select the connected edges.
                parentGraph.ClearSelection();
                PortView port = (PortView)target;
                foreach (EdgeView edge in port.Edges)
                {
                    parentGraph.AddToSelection(edge);
                }
            }
        }
    }
}
