using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Mathematics;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lattice.Editor
{
    // This manipulator is based on the content of https://github.com/vertxxyz/Vertx.EdgeKnife
    // See /ThirdParty/THIRD-PARTY-NOTICES.md license information.
    /// <summary>
    /// A <see cref="Manipulator"/> for <see cref="GraphView"/> that adds a path-drawing edge manipulation tool.
    /// Ctrl+Right-Click drag will delete edges, Shift+Right-Click drag creates an edge redirect node.
    /// </summary>
    internal sealed class EdgeKnifeManipulator : Manipulator
    {
        private enum Mode
        {
            Inactive,
            Additive,
            Subtractive
        }

        // The knife is active if the pointer has been captured and the mode is not inactive.
        private Mode mode = Mode.Inactive;
        private int targetPointerId;
        private readonly EdgeKnifeElement element = new();
        private readonly GraphView graphView;
        private readonly Action<Vector2, IEnumerable<Edge>> createRedirect;

        public EdgeKnifeManipulator(GraphView graphView, Action<Vector2, IEnumerable<Edge>> createRedirect)
        {
            this.graphView = graphView;
            this.createRedirect = createRedirect;
            element.RegisterCallback<PointerDownEvent>(OnPointerDown);
            element.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            element.RegisterCallback<PointerUpEvent>(OnPointerUp);
        }

        /// <inheritdoc />
        protected override void RegisterCallbacksOnTarget()
        {
            target.hierarchy.Add(element);
            element.StretchToParentSize();
            target.RegisterCallback<KeyDownEvent>(OnKeyDown);
            target.RegisterCallback<PointerDownEvent>(OnPointerDown);
            target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
        }

        /// <inheritdoc />
        protected override void UnregisterCallbacksFromTarget()
        {
            element.RemoveFromHierarchy();
            target.UnregisterCallback<KeyDownEvent>(OnKeyDown);
            target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape)
            {
                CancelInteraction();
            }
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            // Cancel any prior interactions because we clicked.
            CancelInteraction();

            if (evt.button != 1)
            {
                return;
            }

            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (evt.modifiers)
            {
                case EventModifiers.Shift:
                    mode = Mode.Additive;
                    element.Color = Color.white;
                    break;
                case EventModifiers.Control:
                    mode = Mode.Subtractive;
                    element.Color = new Color(1, 0.5f, 0);
                    break;
                default:
                    return;
            }

            targetPointerId = evt.pointerId;
            element.RecordPoint(evt.position);
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (mode == Mode.Inactive || targetPointerId != evt.pointerId)
            {
                return;
            }

            if (!element.HasPointerCapture(evt.pointerId))
            {
                element.CapturePointer(evt.pointerId);
                targetPointerId = evt.pointerId;
            }

            element.RecordPoint(evt.position);
            evt.StopImmediatePropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (mode == Mode.Inactive || targetPointerId != evt.pointerId)
            {
                CancelInteraction();
                return;
            }

            element.RecordPoint(evt.position);
            IEnumerable<(Edge edge, Vector2 intersection)> edgeIntersections = element.GetIntersectingEdges(graphView);
            switch (mode)
            {
                case Mode.Additive:
                    // Add redirect nodes.

                    // Collect edges by their destination port
                    Dictionary<Port, List<(Edge, Vector2)>> edgesByDestination = new();
                    foreach ((Edge edge, Vector2 intersection) edgeIntersection in edgeIntersections)
                    {
                        if (!edgesByDestination.TryGetValue(edgeIntersection.edge.output, out List<(Edge, Vector2)> list)) {
                            edgesByDestination.Add(edgeIntersection.edge.output, list = new List<(Edge, Vector2)>());
                        }
                        list.Add(edgeIntersection);
                    }

                    foreach ((_, List<(Edge edge, Vector2 position)> edges) in edgesByDestination) {
                        Vector2 position = edges.Aggregate(Vector2.zero, (a, b) => a + b.position) / edges.Count;
                        position = element.ChangeCoordinatesTo(graphView.contentViewContainer, position);
                        createRedirect.Invoke(position, edges.Select(e => e.edge));
                    }
                    break;
                case Mode.Subtractive:
                    HashSet<Edge> toRemove = edgeIntersections.Select(e => e.edge).ToHashSet();
                    graphView.DeleteElements(toRemove);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            CancelInteraction();
            evt.StopImmediatePropagation();
        }

        private void CancelInteraction()
        {
            if (mode == Mode.Inactive || !element.HasPointerCapture(targetPointerId))
            {
                return;
            }

            element.ReleasePointer(targetPointerId);
            mode = Mode.Inactive;
            element.Reset();
        }

        private sealed class EdgeKnifeElement : VisualElement
        {
            public Color Color { get; set; }
            private readonly List<Vector2> points = new();

            public void Reset()
            {
                points.Clear();
                MarkDirtyRepaint();
            }

            public EdgeKnifeElement()
            {
                pickingMode = PickingMode.Ignore;
                generateVisualContent += GenerateVisualContent;
            }

            private void GenerateVisualContent(MeshGenerationContext obj)
            {
                Painter2D painter = obj.painter2D;
                painter.lineWidth = 1;
                painter.strokeColor = Color;
                if (points.Count == 0)
                {
                    return;
                }

                painter.BeginPath();
                painter.MoveTo(points[0]);
                for (int i = 1; i < points.Count; i++)
                {
                    Vector2 point = points[i];
                    painter.LineTo(point);
                }
                painter.Stroke();
            }

            public void RecordPoint(Vector2 worldPoint)
            {
                const int minimumPointDistance = 8;

                Vector2 localPoint = this.WorldToLocal(worldPoint);

                if (points.Count != 0 && (points[^1] - localPoint).sqrMagnitude < minimumPointDistance)
                {
                    return;
                }

                points.Add(localPoint);
                MarkDirtyRepaint();
            }

            public IEnumerable<(Edge edge, Vector2 intersection)> GetIntersectingEdges(GraphView graphView)
            {
                if (points.Count < 2)
                {
                    yield break;
                }

                Vector2 min = points[0];
                Vector2 max = min;

                for (var i = 1; i < points.Count; i++)
                {
                    Vector2 point = points[i];
                    min.x = Math.Min(min.x, point.x);
                    min.y = Math.Min(min.y, point.y);
                    max.x = Math.Max(max.x, point.x);
                    max.y = Math.Max(max.y, point.y);
                }
                Rect pointsBounds = new(min, max - min);

                VisualElement relativeContainer = graphView.contentViewContainer;
                Rect pointsBoundsInEdgeSpace = this.ChangeCoordinatesTo(relativeContainer, pointsBounds);

                FieldInfo renderPointsField = typeof(EdgeControl)
                    .GetField("m_RenderPoints", BindingFlags.NonPublic | BindingFlags.Instance)!;

                // Check edge intersections.
                foreach (Edge edge in graphView.edges)
                {
                    // Conservative check for the bounds of the points array.
                    if (!edge.Overlaps(pointsBoundsInEdgeSpace))
                    {
                        continue;
                    }

                    // Check the individual render points segments.
                    var renderPoints = (List<Vector2>)renderPointsField.GetValue(edge.edgeControl);
                    if (renderPoints.Count < 2)
                    {
                        continue;
                    }

                    int earliestSegment = points.Count - 1;
                    (Edge edge, Vector2 intersection)? result = null;

                    Vector2 a = edge.edgeControl.ChangeCoordinatesTo(this, renderPoints[0]);
                    for (var i = 0; i < renderPoints.Count - 1; i++)
                    {
                        Vector2 b = edge.edgeControl.ChangeCoordinatesTo(this, renderPoints[i + 1]);
                        // Conservative check per-segment.
                        if (!RectUtils.IntersectsSegment(pointsBounds, a, b))
                        {
                            a = b;
                            continue;
                        }
                        
                        // Thorough per-segment check.
                        for (var j = 0; j < earliestSegment; j++)
                        {
                            if (!TryGetSegmentIntersection(
                                    a, b,
                                    points[j], points[j + 1],
                                    out float2 intersection))
                            {
                                continue;
                            }
                            
                            result = (edge, intersection);
                            earliestSegment = j;
                            break;
                        }
                        a = b;
                    }

                    if (result.HasValue)
                    {
                        yield return result.Value;
                    }
                }
            }

            private static bool TryGetSegmentIntersection(
                float2 p1, float2 p2, float2 p3, float2 p4,
                out float2 intersection
            )
            {
                // Get the segments' parameters.
                float dx12 = p2.x - p1.x;
                float dy12 = p2.y - p1.y;
                float dx34 = p4.x - p3.x;
                float dy34 = p4.y - p3.y;

                // Solve for t1 and t2
                float denominator = dy12 * dx34 - dx12 * dy34;

                float t1 =
                    ((p1.x - p3.x) * dy34 + (p3.y - p1.y) * dx34)
                    / denominator;

                if (math.isinf(t1))
                {
                    // The lines are parallel (or close enough to it).
                    intersection = default;
                    return false;
                }

                float t2 =
                    ((p3.x - p1.x) * dy12 + (p1.y - p3.y) * dx12)
                    / -denominator;

                // The segments intersect if t1 and t2 are between 0 and 1.
                if (!(t1 is > 0 and < 1 &&
                      t2 is > 0 and < 1))
                {
                    intersection = default;
                    return false;
                }

                // Find the point of intersection.
                intersection = new float2(
                    math.mad(t1, dx12, p1.x),
                    math.mad(t1, dy12, p1.y)
                );
                return true;
            }
        }
    }
}
