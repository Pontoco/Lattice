using System.Reflection;
using Lattice.Editor.Views;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lattice.Editor.Manipulators
{
    /// <summary>
    /// An <see cref="UnityEditor.Experimental.GraphView.EdgeDragHelper&lt;T&gt;"/>
    /// that supports dragging onto nodes in addition to the default behaviour.
    /// </summary>
    public sealed class EdgeDragHelperWithNodeDrop : EdgeDragHelper<EdgeView>
    {
        /// <inheritdoc />
        public EdgeDragHelperWithNodeDrop(IEdgeConnectorListener listener) : base(listener) { }

        private static readonly FieldInfo m_GhostEdgeField = typeof(EdgeDragHelper<EdgeView>)
            .GetField("m_GhostEdge", BindingFlags.NonPublic | BindingFlags.Instance);

        private Edge m_GhostEdge
        {
            get => (Edge)m_GhostEdgeField.GetValue(this);
            set => m_GhostEdgeField.SetValue(this, value);
        }

        /// <inheritdoc />
        public override void HandleMouseMove(MouseMoveEvent evt)
        {
            // Try and prioritise the default edge connection behaviour.
            base.HandleMouseMove(evt);

            Edge ghostEdge = m_GhostEdge;

            // Exit because we're connecting an edge.
            if (ghostEdge is not null)
                return;

            // Try to find a matching end port.
            if (!TryGetClosestEndPortWithOverlappingNode(evt.mousePosition, out Port endPort))
                return;

            // Logic taken from EdgeDragHelper<T> with one modification:
            // If we create a ghost edge here, we must also set it in the base class.
            m_GhostEdge = ghostEdge = new EdgeView();
            ghostEdge.isGhostEdge = true;
            ghostEdge.pickingMode = PickingMode.Ignore;
            m_GraphView.AddElement(ghostEdge);

            if (edgeCandidate.output == null)
            {
                ghostEdge.input = edgeCandidate.input;
                if (ghostEdge.output != null)
                    ghostEdge.output.portCapLit = false;
                ghostEdge.output = endPort;
                ghostEdge.output.portCapLit = true;
            }
            else
            {
                if (ghostEdge.input != null)
                    ghostEdge.input.portCapLit = false;
                ghostEdge.input = endPort;
                ghostEdge.input.portCapLit = true;
                ghostEdge.output = edgeCandidate.output;
            }
            // ---
        }

        private bool TryGetClosestEndPortWithOverlappingNode(Vector2 position, out Port result)
        {
            float closestDistSqr = Mathf.Infinity;
            result = null;

            // Find the closest compatible port with node overlap.
            foreach (Port compatiblePort in m_CompatiblePorts)
            {
                if (!compatiblePort.node.worldBound.Contains(position))
                    continue;

                float queryDistanceSqr = (position - compatiblePort.worldBound.center).sqrMagnitude;
                if (queryDistanceSqr >= closestDistSqr)
                    continue;
                closestDistSqr = queryDistanceSqr;
                result = compatiblePort;
            }

            return result != null;
        }

        /// <inheritdoc />
        public override void HandleMouseUp(MouseUpEvent evt)
        {
            // See if there are any normal connections via the default method.
            Port port = (Port)typeof(EdgeDragHelper<EdgeView>)
                              .GetMethod("GetEndPort", BindingFlags.NonPublic | BindingFlags.Instance)!
                              .Invoke(this, new object[] { evt.mousePosition });

            // A connection using the default logic exists,
            // or no overridden connection exists.
            if (port != null || !TryGetClosestEndPortWithOverlappingNode(evt.mousePosition, out port))
            {
                // Perform the default logic and exit.
                base.HandleMouseUp(evt);
                return;
            }

            // To override the default logic, we override the mouse position to connect to the port we identified.
            using MouseUpEvent evtOverride = MouseUpEvent.GetPooled(port.worldBound.center, 0, 1, Vector2.zero);
            base.HandleMouseUp(evtOverride);
        }
    }
}
