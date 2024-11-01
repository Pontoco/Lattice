﻿using System;
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
        public const string VerticalUssClassName = UssClassName + "--vertical";
        public const string HorizontalUssClassName = UssClassName + "--horizontal";
        public const string ConnectedUssClassName = UssClassName + "--connected";
        public const string AcceptsMultipleEdgesUssClassName = UssClassName + "--accepts-multiple-edges";
        public const string SecondaryUssClassName = UssClassName + "--secondary";
        public const string ConnectorName = "connector";
        
        private static readonly VisualElement FauxBoxCap = new(); // An element to override base()'s direct modifications of m_ConnectorBoxCap
        [PublicAPI] // Prevent convert to inline variable warning. This variable promotes future proper usage within this class.
        private readonly VisualElement connectorBoxCap; // m_ConnectorBoxCap is overridden by above.

        private readonly IconBadges badges;

        /// <summary>See <see cref="AddPortTag"/> and <see cref="RemovePortTag"/>.</summary>
        private PortTagView portTag;

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

        /// <summary>If the port was created from an invalid state. Used in cases where a dangling edge created a port.</summary>
        public bool IsVirtual => PortData.IsVirtual;

        /// <summary>If a <see cref="PortTagView"/> has been added to this port.</summary>
        public bool HasPortTag => portTag != null;

        /// <summary>If the port is connected to an edge, or to a port tag.</summary>
        public bool ConnectedVisually => connected || HasPortTag;

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
                AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.pontoco.lattice/Editor/UI/PortView.uss"));

            UpdatePortView(portData);

            this.AddManipulator(new SelectConnectedEdgesManipulator());

            connectorBoxCap = m_ConnectorBoxCap;
            badges = new IconBadges(owner, connectorBoxCap);
            m_ConnectorBoxCap = FauxBoxCap;
        }

        /// <inheritdoc />
        public override bool ContainsPoint(Vector2 localPoint)
        {
            // Fix-up picking that was overridden by Port by reverting to the default behaviour.
            Rect layout = this.layout;
            Rect rect = new(0, 0, layout.width, layout.height);
            return rect.Contains(localPoint);
        }

        public void SetTypeFromCompilation(GraphCompilation compilation)
        {
            if (IsVirtual)
            {
                // Don't factor virtual ports into compilation.
                return;
            }

            if (Owner!.NodeTarget is LatticeNode lnode)
            {
                if (!compilation.Mappings.ContainsKey(lnode))
                {
                    Debug.LogWarning($"Couldn't update port type. Node was not present in compilation? [{lnode}]");
                    return;
                }
                if (compilation.Mappings[lnode].OutputPortMap.TryGetValue(PortData.identifier, out IRNodeRef irNode))
                {
                    Type outputType = compilation.CompileNode(irNode.Node).OutputType;

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
        public static PortView CreatePortView(
            [CanBeNull] BaseNodeView owner,
            Direction direction,
            PortData portData,
            [CanBeNull] EdgeConnectorListener edgeConnectorListener
        )
        {
            PortView pv = new(owner, direction, portData)
            {
                m_EdgeConnector = edgeConnectorListener == null
                    ? null
                    : new EdgeConnectionMouseManipulator(edgeConnectorListener)
            };

            // Only allow connections to ports that have an owner.
            if (owner != null)
            {
                pv.AddManipulator(pv.m_EdgeConnector);
            }
            return pv;
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

            EnableInClassList(ConnectedUssClassName, ConnectedVisually);
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
            EnableInClassList(AcceptsMultipleEdgesUssClassName, PortData.acceptMultipleEdges);
            EnableInClassList(SecondaryUssClassName, PortData.secondaryPort);

            // Only apply the port type if we don't have one set from the compilation.
            if (PortType == null && data.defaultType != null)
            {
                PortType = data.defaultType;
            }

            portName = !string.IsNullOrEmpty(data.displayName) ? data.displayName : PortData.identifier;

            EnableInClassList(VerticalUssClassName, PortData.vertical);
            EnableInClassList(HorizontalUssClassName, !PortData.vertical);

            Tooltip = PortData.customTooltip ?? GraphUtils.GetFormattedTypeNameWithIdentifierWithColor(PortType, Identifier);
            
            // Update the edge in case the port color have changed
            schedule.Execute(() =>
            {
                foreach (EdgeView edge in Edges)
                {
                    edge.UpdateEdgeControl();
                    edge.MarkDirtyRepaint();
                }
            }).ExecuteLater(50); // Hummm
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

        /// <summary>Adds a message view (an attached icon and message) to this port.</summary>
        public void AddMessageView(string message, NodeMessageType messageType, bool allowsRemoval = true)
        {
            SpriteAlignment alignment = Location switch
            {
                PortViewLocation.Bottom or PortViewLocation.BottomLeft => SpriteAlignment.BottomCenter,
                PortViewLocation.Left or PortViewLocation.State => SpriteAlignment.LeftCenter,
                PortViewLocation.Right => SpriteAlignment.RightCenter,
                _ => SpriteAlignment.TopCenter,
            };
            badges.AddMessageView(message, messageType, alignment, allowsRemoval);
        }

        /// <summary>True if the badge is present under the port.</summary>
        public bool HasMessageView(IconBadge badge) => badges.Contains(badge);

        /// <summary>Removes all message views.</summary>
        public void ClearMessageViews()
        {
            badges.ClearMessageViews();
        }

        /// <summary>
        ///     Adds a <see cref="PortTagView"/> to this port if one doesn't exist.
        ///     Check <see cref="HasPortTag"/> or call <see cref="RemovePortTag"/> before calling.
        /// </summary>
        public void AddPortTag(string label)
        {
            if (HasPortTag)
            {
                Debug.LogWarning("Port already has a tag.");
                return;
            }
            portTag = new PortTagView(this, label); // Handles adding/attaching itself.
            AddToClassList(ConnectedUssClassName);
        }
        
        /// <summary>Removes a <see cref="PortTagView"/> if present.</summary>
        public void RemovePortTag()
        {
            if (!HasPortTag)
            {
                return;
            }
            portTag.RemoveFromHierarchy();
            portTag = null;
            EnableInClassList(ConnectedUssClassName, ConnectedVisually);
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
