using System;
using System.Collections.Generic;
using System.Linq;
using Lattice.Base;
using Lattice.Editor.Tools;
using Lattice.Editor.Utils;
using Lattice.Utils;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UIElements;
using Group = Lattice.Base.Group;
using StickyNote = Lattice.Base.StickyNote;

namespace Lattice.Editor.Views
{
    /// <summary>Base class to write a custom view for a node</summary>
    public class BaseGraphView : GraphView, IDisposable
    {
        public BaseGraph Graph { get; protected set; }

        /// <summary>Connector listener that will create the edges between portViews</summary>
        public EdgeConnectorListener ConnectorListener { get; private set; }

        /// <summary>List of all node views in the graph</summary>
        public List<BaseNodeView> NodeViews = new();

        /// <summary>Dictionary of the node views accessed view the node instance, faster than a Find in the node view list</summary>
        public Dictionary<BaseNode, BaseNodeView> NodeViewsPerNode = new();

        /// <summary>List of all edge views in the graph</summary>
        public List<EdgeView> EdgeViews = new();

        /// <summary>List of all group views in the graph</summary>
        public List<GroupView> GroupViews = new();

        /// <summary>List of all sticky note views in the graph</summary>
        public List<StickyNoteView> StickyNoteViews = new();

        /// <summary>List of all stack node views in the graph</summary>
        public List<BaseStackNodeView> StackNodeViews = new();

        private readonly CreateNodeMenuProvider createNodeMenu;

        /// <summary>Triggered when a node is duplicated (crt-d) or copy-pasted (crtl-c/crtl-v)</summary>
        public delegate void NodeDuplicatedDelegate(BaseNode duplicatedNode, BaseNode newNode);

        public event NodeDuplicatedDelegate NodeDuplicated;

        public SerializedObject SerializedGraph { private set; get; }

        private VisualElement malformedTextBox;
        private Vector2 cachedMousePosition;
        private const string ErrorMessageForMissingEdgeConnection =
            "Port was removed from the node.\nRemove the connections; or if you modified the node, consider re-adding the port.";

        public BaseGraphView(EditorWindow window)
        {
            serializeGraphElements = SerializeGraphElementsCallback;
            canPasteSerializedData = CanPasteSerializedDataCallback;
            unserializeAndPaste = UnserializeAndPasteCallback;
            graphViewChanged = GraphViewChangedCallback;
            viewTransformChanged = ViewTransformChangedCallback;
            elementResized = ElementResizedCallback;
            nodeCreationRequest = NodeCreationRequested;

            RegisterCallback<MouseDownEvent>(MouseDownCallback);

            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            SetupZoom(0.05f, 2f);

            createNodeMenu = new CreateNodeMenuProvider(window, (LatticeGraphView)this);

            this.StretchToParentSize();

            // todo(john): This will only reload the graph asset when undo is invoked *and* the window is open.
            // But it won't properly undo the asset if the editor window is closed when the undo invokes. 
            RegisterCallback<AttachToPanelEvent>(_ => Undo.undoRedoPerformed += OnUndo);

            // Deregister callbacks and unload graph when we remove the view.
            RegisterCallback<DetachFromPanelEvent>(_ => Undo.undoRedoPerformed -= OnUndo);
            
            RegisterCallback<MouseMoveEvent>(OnMouseMoveEvent);
        }

        private void OnMouseMoveEvent(MouseMoveEvent evt)
        {
            cachedMousePosition = evt.mousePosition;
        }

        #region Initialization

        /// <summary>Sets the graph this will display. Separate from the constructor so we can use this view in XML UI, or swap the graph out for another.</summary>
        public virtual void LoadGraph(BaseGraph graph)
        {
            Assert.IsNotNull(graph, "Cannot load a null graph.");

            if (Graph != null)
            {
                UnloadGraph();

                // todo: This needs major cleanup (global state) ugh
                NodeProvider.UnloadGraph(graph);
            }

            Graph = graph;
            SerializedGraph = new SerializedObject(Graph);

            // If the graph is malformed, show the messages and exit early. It can't be rendered.
            string malformedDetails = graph.GetMalformedDetails();
            if (malformedTextBox != null)
            {
                Remove(malformedTextBox);
                malformedTextBox = null;
            }
            if (malformedDetails != null)
            {
                malformedTextBox = AssetDatabase
                                   .LoadAssetAtPath<VisualTreeAsset>(
                                       "Packages/com.pontoco.lattice/Editor/UI/InvalidGraph.uxml").CloneTree();
                malformedTextBox.Q<Label>("MissingNodes").text = malformedDetails;
                malformedTextBox.Q<Button>("OpenRefactor").clicked += RepairMissingNodes.OpenRepairWindow;
                malformedTextBox.Q<Button>("ClearNullNodes").style.display =
                    graph.nodes.Any(n => n == null) ? DisplayStyle.Flex : DisplayStyle.None;
                malformedTextBox.Q<Button>("ClearNullNodes").clicked += () =>
                {
                    graph.DeleteNullNodes();
                    ((LatticeGraphWindow)EditorWindow.focusedWindow).ReloadGraph();
                };
                Add(malformedTextBox);
                malformedTextBox.StretchToParentSize();

                return;
            }
            
            ConnectorListener = new EdgeConnectorListener((LatticeGraphView)this);

            InitializeNodeViews();
            InitializeEdgeViews();
            InitializeGroups();
            InitializeStickyNotes();
            InitializeStackNodes();

            Graph.OnGraphChanges += GraphChangesCallback;

            viewTransform.position = Graph.ViewPosition;
            viewTransform.scale = new Vector3(Graph.ViewScale, Graph.ViewScale, Graph.ViewScale);

            NodeProvider.LoadGraph(graph);
        }

        public virtual void UnloadGraph()
        {
            Assert.IsNotNull(Graph);

            RemoveGroups();
            RemoveNodeViews();
            RemoveEdges();
            RemoveStackNodeViews();
            RemoveStickyNoteViews();

            NodeProvider.UnloadGraph(Graph);
            Graph.OnGraphChanges -= GraphChangesCallback;

            SerializedGraph = null;
            Graph = null;
        }

        private void OnUndo()
        {
            // Undo will reset the data within the ScriptableObject, but doesn't call OnEnable/OnDisable.
            // This means the stored referenced / caches are all out of date, so reinitialize the graph.
            //
            // This view will be reloaded fully, due to the OnGraphChanges callback.
            Graph.Initialize();
        }

        // This runs after an undo operation, or if the entire BaseGraph is reloaded from disk.
        private void ReloadEntireGraph()
        {
            Assert.IsNotNull(Graph, "Can't reload an empty graph.");

            // Store the current selected nodes during reload.
            var selectedNodeGUIDs = new List<string>();
            foreach (var e in selection)
            {
                if (e is BaseNodeView v && Contains(v))
                {
                    selectedNodeGUIDs.Add(v.NodeTarget.GUID);
                }
            }

            // Remove everything
            var graph = Graph;
            UnloadGraph();

            // And re-add with new up to date datas
            LoadGraph(graph);

            // Restore selection after re-creating all views
            foreach (var guid in selectedNodeGUIDs)
            {
                AddToSelection(NodeViews.FirstOrDefault(n => n.NodeTarget.GUID == guid));
            }
        }

        private void InitializeNodeViews()
        {
            foreach (var node in Graph.nodes)
            {
                AddNodeView(node);
            }
        }

        private void InitializeEdgeViews()
        {
            foreach (var serializedEdge in Graph.edges)
            {
                NodeViewsPerNode.TryGetValue(serializedEdge.toNode, out var inputNodeView);
                NodeViewsPerNode.TryGetValue(serializedEdge.fromNode, out var outputNodeView);

                PortView inputPort = inputNodeView?.GetPortOrAddVirtualPort(serializedEdge.toPortIdentifier, Direction.Input);
                PortView outputPort = outputNodeView?.GetPortOrAddVirtualPort(serializedEdge.fromPortIdentifier, Direction.Output);
                
                if (inputNodeView == null || outputNodeView == null || inputPort == null || outputPort == null)
                {
                    Debug.LogError(
                        $"Edge [{serializedEdge}] could not find inputs. Completely detached from graph.");
                    continue;
                }
                
                if (inputPort.IsVirtual)
                {
                    inputPort.AddMessageView(ErrorMessageForMissingEdgeConnection, NodeMessageType.Error, false);
                }
                
                if (outputPort.IsVirtual)
                {
                    inputPort.AddMessageView(ErrorMessageForMissingEdgeConnection, NodeMessageType.Error, false);
                }

                var edgeView = new EdgeView
                {
                    userData = serializedEdge,
                    input = inputPort,
                    output = outputPort,
                    IsHidden = serializedEdge.IsHidden,
                    IsVirtual = inputPort.IsVirtual || outputPort.IsVirtual
                };

                AddElement(edgeView);

                ConnectEdgeView(edgeView);

                EdgeViews.Add(edgeView);
            }
        }

        private void InitializeGroups()
        {
            foreach (var group in Graph.groups)
            {
                AddGroupView(group);
            }
        }

        private void InitializeStickyNotes()
        {
            foreach (var group in Graph.stickyNotes)
            {
                AddStickyNoteView(group);
            }
        }

        private void InitializeStackNodes()
        {
            foreach (var stackNode in Graph.stackNodes)
            {
                AddStackNodeView(stackNode);
            }
        }

        #endregion

        #region Callbacks

        protected override bool canCopySelection
        {
            get { return selection.Any(e => e is BaseNodeView || e is GroupView); }
        }

        protected override bool canCutSelection
        {
            get { return selection.Any(e => e is BaseNodeView || e is GroupView); }
        }

        private string SerializeGraphElementsCallback(IEnumerable<GraphElement> elements)
        {
            var data = new CopyPasteHelper();

            List<GraphElement> elementsList = elements.ToList();

            foreach (BaseNodeView nodeView in elementsList.Where(e => e is BaseNodeView))
            {
                data.copiedNodes.Add(JsonSerializer.SerializeNode(nodeView.NodeTarget));
                foreach (var port in nodeView.NodeTarget.GetAllPorts())
                {
                    if (port.portData.vertical)
                    {
                        foreach (var edge in port.GetEdges())
                        {
                            data.copiedEdges.Add(JsonSerializer.Serialize(edge));
                        }
                    }
                }
            }

            foreach (GroupView groupView in elementsList.Where(e => e is GroupView))
            {
                data.copiedGroups.Add(JsonSerializer.Serialize(groupView.group));
            }

            foreach (EdgeView edgeView in elementsList.Where(e => e is EdgeView))
            {
                data.copiedEdges.Add(JsonSerializer.Serialize(edgeView.SerializedEdge));
            }

            ClearSelection();

            return JsonUtility.ToJson(data, true);
        }

        private bool CanPasteSerializedDataCallback(string serializedData)
        {
            try
            {
                return JsonUtility.FromJson(serializedData, typeof(CopyPasteHelper)) != null;
            }
            catch
            {
                return false;
            }
        }

        private void UnserializeAndPasteCallback(string operationName, string serializedData)
        {
            var data = JsonUtility.FromJson<CopyPasteHelper>(serializedData);

            Undo.RegisterCompleteObjectUndo(Graph, operationName);

            Dictionary<string, BaseNode> copiedNodesMap = new Dictionary<string, BaseNode>();

            var unserializedGroups = data.copiedGroups.Select(JsonSerializer.Deserialize<Group>).ToList();

            foreach (var serializedNode in data.copiedNodes)
            {
                var node = JsonSerializer.DeserializeNode(serializedNode);

                if (node == null)
                {
                    continue;
                }

                string sourceGuid = node.GUID;
                Graph.NodesPerGuid.TryGetValue(sourceGuid, out var sourceNode);
                //Call OnNodeCreated on the new fresh copied node
                node.OnNodeCreated();

                //And move a bit the new node
                node.Position += new float2(20, 20);

                AddNode(node);

                // If the nodes were copied from another graph, then the source is null
                if (sourceNode != null)
                {
                    NodeDuplicated?.Invoke(sourceNode, node);
                }
                copiedNodesMap[sourceGuid] = node;

                //Select the new node
                AddToSelection(NodeViewsPerNode[node]);
            }

            foreach (var group in unserializedGroups)
            {
                //Same than for node
                group.OnCreated();

                // try to centre the created node in the screen
                group.position.position += new Vector2(20, 20);

                var oldGuidList = group.innerNodeGUIDs.ToList();
                group.innerNodeGUIDs.Clear();
                foreach (var guid in oldGuidList)
                {
                    Graph.NodesPerGuid.TryGetValue(guid, out var node);

                    // In case group was copied from another graph
                    if (node == null)
                    {
                        node = copiedNodesMap[guid];
                        group.innerNodeGUIDs.Add(node.GUID);
                    }
                    else
                    {
                        group.innerNodeGUIDs.Add(copiedNodesMap[guid].GUID);
                    }
                }

                AddGroup(group);
            }

            foreach (var serializedEdge in data.copiedEdges)
            {
                var edge = JsonSerializer.Deserialize<SerializableEdge>(serializedEdge);

                edge.Deserialize();

                // Find port of new nodes:
                copiedNodesMap.TryGetValue(edge.toNode.GUID, out var oldInputNode);
                copiedNodesMap.TryGetValue(edge.fromNode.GUID, out var oldOutputNode);

                // We avoid to break the graph by replacing unique connections:
                if ((oldInputNode == null && !edge.toPort.portData.acceptMultipleEdges) ||
                    !edge.fromPort.portData.acceptMultipleEdges)
                {
                    continue;
                }

                oldInputNode ??= edge.toNode;
                oldOutputNode ??= edge.fromNode;

                var toPort = oldInputNode.GetPort(edge.toPortIdentifier);
                var fromPort = oldOutputNode.GetPort(edge.fromPortIdentifier);

                var newEdge = SerializableEdge.CreateNewEdge(Graph, toPort, fromPort);

                if (NodeViewsPerNode.ContainsKey(oldInputNode) && NodeViewsPerNode.ContainsKey(oldOutputNode))
                {
                    var edgeView = new EdgeView();
                    edgeView.userData = newEdge;
                    edgeView.input = NodeViewsPerNode[oldInputNode].GetPort(newEdge.toPortIdentifier);
                    edgeView.output = NodeViewsPerNode[oldOutputNode].GetPort(newEdge.fromPortIdentifier);

                    Connect(edgeView);
                }
            }
        }

        private GraphViewChange GraphViewChangedCallback(GraphViewChange changes)
        {
            if (changes.elementsToRemove != null)
            {
                Undo.RegisterCompleteObjectUndo(Graph, "Remove Graph Elements");

                // Destroy priority of objects
                // We need nodes to be destroyed first because we can have a destroy operation that uses node connections
                changes.elementsToRemove.Sort((e1, e2) =>
                {
                    int GetPriority(GraphElement e)
                    {
                        if (e is BaseNodeView)
                        {
                            return 0;
                        }
                        return 1;
                    }

                    return GetPriority(e1).CompareTo(GetPriority(e2));
                });

                //Handle ourselves the edge and node remove
                changes.elementsToRemove.RemoveAll(e =>
                {
                    switch (e)
                    {
                        case EdgeView edge:
                            Disconnect(edge);
                            return true;
                        case BaseNodeView nodeView:
                            // For vertical nodes, we need to delete them ourselves as it's not handled by GraphView
                            foreach (PortView pv in nodeView.AllPortViews)
                            {
                                if (pv.orientation == Orientation.Vertical)
                                {
                                    foreach (var edge in pv.Edges.ToList()) // clone
                                    {
                                        Disconnect(edge);
                                    }
                                }
                            }

                            Graph.RemoveNode(nodeView.NodeTarget);
                            SerializedGraph = new SerializedObject(Graph);
                            RemoveElement(nodeView);
                            SyncSerializedPropertyPaths();
                            return true;
                        case GroupView group:
                            Graph.RemoveGroup(group.group);
                            SerializedGraph = new SerializedObject(Graph);
                            RemoveElement(group);
                            return true;
                        case BaseStackNodeView stackNodeView:
                            Graph.RemoveStackNode(stackNodeView.stackNode);
                            SerializedGraph = new SerializedObject(Graph);
                            RemoveElement(stackNodeView);
                            return true;
                        case StickyNoteView stickyNoteView:
                            Graph.RemoveStickyNote(stickyNoteView.Note);
                            SerializedGraph = new SerializedObject(Graph);
                            RemoveElement(stickyNoteView);
                            return true;
                    }

                    return false;
                });
            }

            return changes;
        }

        /// <summary>
        /// Triggered when the loaded <see cref="BaseGraph.OnGraphChanges"/> is called.
        /// </summary>
        /// <param name="changes">A collection of changes made to the graph</param>
        protected virtual void GraphChangesCallback(GraphChanges changes)
        {
            if (changes.removedEdge != null)
            {
                var edge = EdgeViews.FirstOrDefault(e => e.SerializedEdge == changes.removedEdge);

                DisconnectView(edge);
            }

            if (changes.removedNode != null)
            {
                RemoveNodeView(NodeViewsPerNode[changes.removedNode]);
            }

            if (changes.entireGraphInitialized != null)
            {
                ReloadEntireGraph();
            }
        }

        private void ViewTransformChangedCallback(GraphView view)
        {
            if (Graph != null)
            {
                Graph.ViewPosition = viewTransform.position;
                Graph.ViewScale = viewTransform.scale.x;
            }
        }

        private void ElementResizedCallback(VisualElement elem)
        {
            var groupView = elem as GroupView;

            if (groupView != null)
            {
                groupView.group.size = groupView.GetPosition().size;
            }
        }

        private void NodeCreationRequested(NodeCreationContext c)
        {
            createNodeMenu.Show(cachedMousePosition, null);
        }

        /// <summary>Build the contextual menu shown when right clicking inside the graph view</summary>
        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            base.BuildContextualMenu(evt);
            
            Vector2 position1 =
                (evt.currentTarget as VisualElement).ChangeCoordinatesTo(contentViewContainer, evt.localMousePosition);
            evt.menu.AppendAction("Create Group",
                e => AddSelectionsToGroup(AddGroup(new Group("Create Group", position1))),
                DropdownMenuAction.AlwaysEnabled);
            
            evt.menu.AppendAction("Create Sticky Note",
                e => AddStickyNote(new StickyNote("Create Note", position1)), DropdownMenuAction.AlwaysEnabled);

            evt.menu.AppendAction("Dump Graph State", action =>
            {
                foreach (var edge in Graph.edges)
                {
                    Debug.Log(edge);
                }
            });
        }

        private void MouseDownCallback(MouseDownEvent e)
        {
            // When left clicking on the graph (not a node or something else)
            if (e.button == 0)
            {
                // Close all settings windows:
                NodeViews.ForEach(v => v.CloseSettings());
            }
        }

        #endregion

        #region Graph content modification

        public BaseNodeView AddNode(BaseNode node)
        {
            // This will initialize the node using the graph instance
            Graph.AddNode(node);

            SerializedGraph = new SerializedObject(Graph);

            var view = AddNodeView(node);

            return view;
        }

        public BaseNodeView AddNodeView(BaseNode node)
        {
            var viewType = NodeProvider.GetNodeViewTypeFromType(node.GetType());

            if (viewType == null)
            {
                viewType = typeof(BaseNodeView);
            }

            var baseNodeView = Activator.CreateInstance(viewType) as BaseNodeView;
            baseNodeView.Initialize(this, node);
            AddElement(baseNodeView);

            NodeViews.Add(baseNodeView);
            NodeViewsPerNode[node] = baseNodeView;

            return baseNodeView;
        }

        public void RemoveNodeView(BaseNodeView nodeView)
        {
            RemoveElement(nodeView);
            NodeViews.Remove(nodeView);
            NodeViewsPerNode.Remove(nodeView.NodeTarget);
        }

        private void RemoveNodeViews()
        {
            foreach (var nodeView in NodeViews)
            {
                RemoveElement(nodeView);
            }
            NodeViews.Clear();
            NodeViewsPerNode.Clear();
        }

        private void RemoveStackNodeViews()
        {
            foreach (var stackView in StackNodeViews)
            {
                RemoveElement(stackView);
            }
            StackNodeViews.Clear();
        }

        public GroupView AddGroup(Group block)
        {
            Graph.AddGroup(block);
            block.OnCreated();
            return AddGroupView(block);
        }

        public GroupView AddGroupView(Group block)
        {
            var c = new GroupView();

            c.Initialize(this, block);

            AddElement(c);

            GroupViews.Add(c);
            return c;
        }

        public BaseStackNodeView AddStackNodeView(BaseStackNode stackNode)
        {
            var viewType = StackNodeViewProvider.GetStackNodeCustomViewType(stackNode.GetType()) ??
                           typeof(BaseStackNodeView);
            var stackView = Activator.CreateInstance(viewType, stackNode) as BaseStackNodeView;

            AddElement(stackView);
            StackNodeViews.Add(stackView);

            stackView.Initialize(this);

            return stackView;
        }

        public void RemoveStackNodeView(BaseStackNodeView stackNodeView)
        {
            StackNodeViews.Remove(stackNodeView);
            RemoveElement(stackNodeView);
        }

        public StickyNoteView AddStickyNote(StickyNote note)
        {
            Graph.AddStickyNote(note);
            return AddStickyNoteView(note);
        }

        public StickyNoteView AddStickyNoteView(StickyNote note)
        {
            var c = new StickyNoteView();

            c.Initialize(this, note);

            AddElement(c);

            StickyNoteViews.Add(c);
            return c;
        }

        public void RemoveStickyNoteView(StickyNoteView view)
        {
            StickyNoteViews.Remove(view);
            RemoveElement(view);
        }

        public void RemoveStickyNoteViews()
        {
            foreach (var stickyNodeView in StickyNoteViews.ToList())
            {
                RemoveStickyNoteView(stickyNodeView);
            }
        }

        public void AddSelectionsToGroup(GroupView view)
        {
            foreach (var selectedNode in selection)
            {
                if (selectedNode is BaseNodeView)
                {
                    if (GroupViews.Exists(x => x.ContainsElement(selectedNode as BaseNodeView)))
                    {
                        continue;
                    }

                    view.AddElement(selectedNode as BaseNodeView);
                }
            }
        }

        public void RemoveGroups()
        {
            foreach (var groupView in GroupViews)
            {
                RemoveElement(groupView);
            }
            GroupViews.Clear();
        }

        public bool ConnectEdgeView(EdgeView e, bool autoDisconnectInputs = true)
        {
            Assert.IsTrue(e.IsValid());

            var inputPortView = e.input as PortView;
            var outputPortView = e.output as PortView;
            var inputNodeView = inputPortView.node as BaseNodeView;
            var outputNodeView = outputPortView.node as BaseNodeView;

            //If the input port does not support multi-connection, we remove them
            if (autoDisconnectInputs && !(e.input as PortView).PortData.acceptMultipleEdges)
            {
                foreach (var edge in EdgeViews.Where(ev => ev.input == e.input).ToList())
                {
                    // TODO: do not disconnect them if the connected port is the same than the old connected
                    DisconnectView(edge);
                }
            }
            // same for the output port:
            if (autoDisconnectInputs && !(e.output as PortView).PortData.acceptMultipleEdges)
            {
                foreach (var edge in EdgeViews.Where(ev => ev.output == e.output).ToList())
                {
                    // TODO: do not disconnect them if the connected port is the same than the old connected
                    DisconnectView(edge);
                }
            }

            AddElement(e);

            e.input.Connect(e);
            e.output.Connect(e);

            // If the input port have been removed by the custom port behavior
            // we try to find if it's still here
            if (e.input == null)
            {
                e.input = inputNodeView.GetPort(inputPortView.PortData.identifier);
            }
            if (e.output == null)
            {
                e.output = inputNodeView.GetPort(outputPortView.PortData.identifier);
            }

            EdgeViews.Add(e);

            inputNodeView.RefreshAllPorts();
            outputNodeView.RefreshAllPorts();

            // In certain cases the edge color is wrong so we patch it
            schedule.Execute(() =>
            {
                e.UpdateEdgeControl();
            }).ExecuteLater(1);

            return true;
        }

        /// <summary>Creates a new serialized edge between two portViews.</summary>
        /// <param name="autoDisconnectInputs">
        ///     If this automatically disconnects any inputs connected to the 'input' node of this
        ///     edge.
        /// </param>
        /// <param name="inputPortView">A port in an input container to connect to.</param>
        /// <param name="outputPortView">A port in an output container to connect from.</param>
        public EdgeView CreateNewEdge(PortView inputPortView, PortView outputPortView, bool autoDisconnectInputs = true)
        {
            // Checks that the nodes we are connecting still exist
            if (inputPortView.Owner!.parent == null || outputPortView.Owner!.parent == null)
            {
                throw new Exception("Invalid portViews. Can't connect them!");
            }
            
            var edgeView = new EdgeView
            {
                input = inputPortView,
                output = outputPortView
            };

            Connect(edgeView);
            return edgeView;
        }

        public bool Connect(EdgeView e, bool autoDisconnectInputs = true)
        {
            Assert.IsTrue(e.IsValid());

            var inputPortView = (PortView)e.input;
            var outputPortView = (PortView)e.output;
            var inputPort = inputPortView.Owner!.NodeTarget.GetPort(inputPortView.Identifier);
            var outputPort = outputPortView.Owner!.NodeTarget.GetPort(outputPortView.Identifier);

            e.userData = Graph.CreateEdge(inputPort, outputPort, autoDisconnectInputs);

            return ConnectEdgeView(e, autoDisconnectInputs);
        }

        public void DisconnectView(EdgeView e, bool refreshPorts = true)
        {
            if (e == null)
            {
                return;
            }

            RemoveElement(e);

            if (e?.input?.node is BaseNodeView inputNodeView)
            {
                e.input.Disconnect(e);
                if (refreshPorts)
                {
                    inputNodeView.RefreshAllPorts();
                }
            }
            if (e?.output?.node is BaseNodeView outputNodeView)
            {
                e.output.Disconnect(e);
                if (refreshPorts)
                {
                    outputNodeView.RefreshAllPorts();
                }
            }

            EdgeViews.Remove(e);
        }

        public void Disconnect(EdgeView e, bool refreshPorts = true)
        {
            // Remove the serialized edge if there is one
            if (e.userData is SerializableEdge serializableEdge)
            {
                Graph.Disconnect(serializableEdge);
            }

            DisconnectView(e, refreshPorts);
        }

        public void RemoveEdges()
        {
            foreach (var edge in EdgeViews)
            {
                RemoveElement(edge);
            }
            EdgeViews.Clear();
        }

        public void ResetPositionAndZoom()
        {
            if (Graph == null)
            {
                return;
            }

            Graph.ViewPosition = Vector3.zero;
            Graph.ViewScale = 1f;

            UpdateViewTransform(Graph.ViewPosition, new Vector3(Graph.ViewScale, Graph.ViewScale, Graph.ViewScale));
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            var compatiblePorts = new List<Port>();

            if (startPort is not PortView startPortView)
            {
                return new List<Port>();
            }

            compatiblePorts.AddRange(ports.ToList().Where(p =>
            {
                if (p is not PortView portView)
                {
                    return false;
                }

                if (portView.Owner == startPortView.Owner)
                {
                    return false;
                }

                if (p.direction == startPort.direction)
                {
                    return false;
                }

                //Check for type assignability
                if (startPort.direction == Direction.Input)
                {
                    if (!BaseGraph.TypesAreConnectable(p.portType, startPort.portType))
                    {
                        return false;
                    }
                }
                else
                {
                    if (!BaseGraph.TypesAreConnectable(startPort.portType, p.portType))
                    {
                        return false;
                    }
                }

                //Check if the edge already exists
                if (portView.Edges.Any(e => e.input == startPort || e.output == startPort))
                {
                    return false;
                }

                return true;
            }));

            return compatiblePorts;
        }

        public virtual IEnumerable<(string path, INodeTemplate builder)> FilterCreateNodeMenuEntries()
        {
            // By default we don't filter anything
            foreach (var nodeMenuItem in NodeProvider.GetNodeMenuEntries(Graph))
            {
                yield return nodeMenuItem;
            }

            // TODO: add exposed properties to this list
        }

        /// <summary>
        ///     Update all the serialized property bindings (in case a node was deleted / added, the property pathes needs to
        ///     be updated, because node indices may have changed)
        /// </summary>
        public void SyncSerializedPropertyPaths()
        {
            foreach (var nodeView in NodeViews)
            {
                nodeView.SyncSerializedPropertyPaths();
            }
        }

        #endregion

        /// <summary>
        /// Removes any registered events.<br/>
        /// Should be called when unloading a window that references the view.
        /// </summary>
        public void Dispose()
        {
            if (Graph != null)
                UnloadGraph();
        }
    }
}
