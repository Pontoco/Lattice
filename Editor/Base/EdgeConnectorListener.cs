using System;
using System.Collections.Generic;
using Lattice.Editor.Views;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace Lattice.Editor.Utils
{
    /// <summary>Handles logic for when edges are connected, or if they are dropped outside of a port. Generic to all graphs.</summary>
    public class BaseEdgeConnectorListener : IEdgeConnectorListener
    {
        public readonly BaseGraphView GraphView;

        private readonly Dictionary<Edge, PortView> edgeInputPorts = new();
        private readonly Dictionary<Edge, PortView> edgeOutputPorts = new();

        private static CreateNodeMenuWindow edgeNodeCreateMenuWindow;

        public BaseEdgeConnectorListener(BaseGraphView graphView)
        {
            GraphView = graphView;
        }

        public void OnDropOutsidePort(Edge edge, Vector2 position)
        {
            Undo.RegisterCompleteObjectUndo(GraphView.Graph, "Disconnect edge");

            // If the edge is already connected, and you drop it outside a port, remove it.
            if (!edge.isGhostEdge)
            {
                GraphView.Disconnect(edge as EdgeView);
            }

            // If the edge is not fully connected, and it's dropped, spawn the node create window.
            if (edge.input == null || edge.output == null)
            {
                EdgeView edgeView = edge as EdgeView;
                if (edgeNodeCreateMenuWindow == null)
                {
                    edgeNodeCreateMenuWindow = ScriptableObject.CreateInstance<CreateNodeMenuWindow>();
                }

                edgeNodeCreateMenuWindow.Initialize(GraphView, EditorWindow.focusedWindow, edgeView);
                SearchWindow.Open(new SearchWindowContext(position + EditorWindow.focusedWindow.position.position),
                    edgeNodeCreateMenuWindow);
            }
        }

        public void OnDrop(GraphView graphView, Edge edge)
        {
            var edgeView = (EdgeView)edge; // There are no other edge types in our graphs.

            if (edgeView.input == null || edgeView.output == null)
            {
                // Do nothing if you drop a disconnected edge.
                return;
            }

            // If the edge was dropped on the same port.
            bool wasOnTheSamePort = false;
            if (edgeInputPorts.ContainsKey(edge) && edgeOutputPorts.TryGetValue(edge, out PortView port))
            {
                if (edgeInputPorts[edge] == edge.input && port == edge.output)
                {
                    wasOnTheSamePort = true;
                }
            }

            if (!wasOnTheSamePort)
            {
                GraphView.Disconnect(edgeView);
            }

            edgeInputPorts[edge] = edge.input as PortView;
            edgeOutputPorts[edge] = edge.output as PortView;

            try
            {
                string name = "Connected " + edgeView.input.node.name + " and " +
                              edgeView.output.node.name;
                Undo.RegisterCompleteObjectUndo(GraphView.Graph, name);
                if (!GraphView.Connect((EdgeView)edge, !wasOnTheSamePort))
                {
                    GraphView.Disconnect((EdgeView)edge);
                }
            }
            catch (Exception)
            {
                GraphView.Disconnect(edge as EdgeView);
            }
        }
    }
}
