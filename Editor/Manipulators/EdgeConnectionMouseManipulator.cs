using Lattice.Editor.Views;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lattice.Editor.Manipulators
{
    /// <summary>
    ///     Listens to mouse, keyboard, and other UI events to manage creating a new edge. This is a MouseManipulator like
    ///     zoom/pan/etc.
    /// </summary>
    public class EdgeConnectionMouseManipulator : EdgeConnector
    {
        private readonly EdgeDragHelperWithNodeDrop dragHelper;
        private Edge edgeCandidate;
        private bool active;
        private Vector2 mouseDownPosition;

        private const float ConnectionDistanceThreshold = 10f;

        public EdgeConnectionMouseManipulator(IEdgeConnectorListener listener)
        {
            active = false;
            dragHelper = new EdgeDragHelperWithNodeDrop(listener);
            activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
        }

        public override EdgeDragHelper edgeDragHelper => dragHelper;

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<MouseDownEvent>(OnMouseDown);
            target.RegisterCallback<MouseMoveEvent>(OnMouseMove);
            target.RegisterCallback<MouseUpEvent>(OnMouseUp);
            target.RegisterCallback<KeyDownEvent>(OnKeyDown);
            target.RegisterCallback<MouseCaptureOutEvent>(OnMouseCaptureOut);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<MouseDownEvent>(OnMouseDown);
            target.UnregisterCallback<MouseMoveEvent>(OnMouseMove);
            target.UnregisterCallback<MouseUpEvent>(OnMouseUp);
            target.UnregisterCallback<KeyDownEvent>(OnKeyDown);
        }

        private void OnMouseDown(MouseDownEvent e)
        {
            if (active)
            {
                e.StopImmediatePropagation();
                return;
            }

            if (!CanStartManipulation(e))
            {
                return;
            }

            Port graphElement = target as Port;
            if (graphElement == null)
            {
                return;
            }

            mouseDownPosition = e.localMousePosition;

            edgeCandidate = new EdgeView();
            edgeDragHelper.draggedPort = graphElement;
            edgeDragHelper.edgeCandidate = edgeCandidate;

            if (edgeDragHelper.HandleMouseDown(e))
            {
                active = true;
                target.CaptureMouse();

                e.StopPropagation();
            }
            else
            {
                edgeDragHelper.Reset();
                edgeCandidate = null;
            }
        }


        private void OnMouseMove(MouseMoveEvent e)
        {
            if (!active)
            {
                return;
            }

            edgeDragHelper.HandleMouseMove(e);
            edgeCandidate.candidatePosition = e.mousePosition;
            edgeCandidate.UpdateEdgeControl();
            e.StopPropagation();
        }

        private void OnMouseUp(MouseUpEvent e)
        {
            if (!active || !CanStopManipulation(e))
            {
                return;
            }

            if (Vector2.Distance(mouseDownPosition, e.localMousePosition) > ConnectionDistanceThreshold)
            {
                edgeDragHelper.HandleMouseUp(e);
            }
            else
            {
                Abort();
            }

            active = false;
            edgeCandidate = null;
            target.ReleaseMouse();
            e.StopPropagation();
        }
        
        private void OnMouseCaptureOut(MouseCaptureOutEvent e)
        {
            active = false;
            if (edgeCandidate != null)
            {
                Abort();
            }
        }

        private void OnKeyDown(KeyDownEvent e)
        {
            if (e.keyCode != KeyCode.Escape || !active)
            {
                return;
            }

            Abort();

            active = false;
            target.ReleaseMouse();
            e.StopPropagation();
        }

        private void Abort()
        {
            GraphView graphView = target?.GetFirstAncestorOfType<GraphView>();
            graphView?.RemoveElement(edgeCandidate);

            edgeCandidate.input = null;
            edgeCandidate.output = null;
            edgeCandidate = null;

            edgeDragHelper.Reset();
        }
    }
}
