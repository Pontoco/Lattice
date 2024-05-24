using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using Lattice.Base;
using Lattice.Editor.Manipulators;
using Lattice.Editor.Utils;
using Lattice.Utils;
using Unity.Entities.Editor;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UIElements;
using NodeView = UnityEditor.Experimental.GraphView.Node;
using Resources = UnityEngine.Resources;

namespace Lattice.Editor.Views
{
    /// <summary>A visual element that renders a node on the canvas.</summary>
    [NodeCustomEditor(typeof(BaseNode))]
    public class BaseNodeView : NodeView
    {
        // Uss classes:
        public const string UssClassName = "node";
        public const string ControlsUssClassName = UssClassName + "__controls";
        public const string ControlsEmptyUssClassName = ControlsUssClassName + "--empty";
        public const string TypeLabelUssClassName = UssClassName + "__type-label";
        public const string TitleNameContainerUssClassName = UssClassName + "__title-name-container";
        public const string TitleNameContainerHasCustomNameUssClassName = TitleNameContainerUssClassName + "--has-custom-name";
        public const string TitleIconUssClassName = UssClassName + "__title-icon";
        public const string HighlightedUssClassName = UssClassName + "--highlighted";

        // Element names:
        public const string TitleContainerName = "title";
        public const string TitleLabelName = "title-label";
        public const string SubtitleLabelName = "sub-title-label";
        public const string TitleIconName = "TitleIcon";
        public const string TopPortContainerName = "TopPortContainer";
        public const string BottomPortContainerName = "BottomPortContainer";
        public const string BottomLeftPortContainerName = "BottomLeftPortContainer";
        public const string StatePortContainerName = "StatePortContainer";
        public const string LeftPortContainerName = "input";
        public const string RightPortContainerName = "output";

        /// <summary>The node this renders.</summary>
        public BaseNode NodeTarget;

        private readonly List<PortView> inputPortViews = new();
        private readonly List<PortView> outputPortViews = new();

        public IEnumerable<PortView> AllPortViews => inputPortViews.Concat(outputPortViews);
        public IReadOnlyList<PortView> InputPortViews => inputPortViews;
        public IReadOnlyList<PortView> OutputPortViews => outputPortViews;

        /// <summary>The parent graph view.</summary>
        public BaseGraphView Owner { get; private set; }

        private VisualElement titleIcon;
        private string currentTitleIconClass;
        protected VisualElement titleNameContainer; // Within titleContainer. Holds node type and renaming label.
        protected VisualElement controlsContainer; // The foldout properties within the node.
        protected VisualElement debugContainer; // The debug string under a node when in debug mode.
        protected VisualElement compileDataContainer; // Debug string with compilation metadata for the node.
        protected VisualElement rightTitleContainer; // Holds the settings gear, if present.
        protected VisualElement topPortContainer; // Holds the portViews on the top of the node (vertical)
        protected VisualElement bottomPortContainer; // Holds the portViews on the bottom of the node (vertical)
        protected VisualElement leftPortContainer => inputContainer; // Holds the ports on the left of the node.
        protected VisualElement rightPortContainer => outputContainer; // Holds the ports on the right of the node.
        protected VisualElement bottomLeftPortContainer; // Holds the ports on the bottom-left of the node (vertical)
        protected VisualElement statePortContainer; // Holds action ports that modify the state of the node (vertical)
        private VisualElement inputContainerElement;

        // The pop-up settings menu exposed by the gear icon.  
        protected NodeSettingsView settingsContainer;
        private Button settingButton;

        private TextField titleTextField;
        private Label nodeTypeLabel;

        public event Action<PortView> onPortConnected;
        public event Action<PortView> onPortDisconnected;

        private bool hasSettings;
        private bool initializing; // Used to avoid adding to the undo stack during initialization (SetPosition).

        private bool settingsExpanded;

        private IconBadges badges;

        // All of these are temp variables used in Alignment functions. Not for perf. Could be removed.
        private float selectedNodesFarLeft;
        private float selectedNodesNearLeft;
        private float selectedNodesFarRight;
        private float selectedNodesNearRight;
        private float selectedNodesFarTop;
        private float selectedNodesNearTop;
        private float selectedNodesFarBottom;
        private float selectedNodesNearBottom;
        private float selectedNodesAvgHorizontal;
        private float selectedNodesAvgVertical;

        public virtual void Initialize(BaseGraphView owner, BaseNode node)
        {
            NodeTarget = node;
            Owner = owner;

            if (!node.Deletable)
            {
                capabilities &= ~Capabilities.Deletable;
            }

            if (node.IsRenamable)
            {
                // The Capabilities.Renamable capability isn't currently implemented in GraphView, but we implement it below.
                capabilities |= Capabilities.Renamable;
            }

            node.OnPortsUpdated += () => schedule.Execute(_ =>
            {
                RefreshAllPorts();
            }).ExecuteLater(0);

            styleSheets.Add(
                AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.pontoco.lattice/Editor/UI/BaseNodeView.uss"));

            if (!string.IsNullOrEmpty(node.LayoutStyle))
            {
                styleSheets.Add(Resources.Load<StyleSheet>(node.LayoutStyle));
            }

            InitializeView();
            InitializePorts();

            CreateNodeInspector();

            InitializeSettings();
            RefreshExpandedState();
            RefreshAllPorts();

            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            OnGeometryChanged(null);
        }

        private void InitializePorts()
        {
            var listener = Owner.ConnectorListener;

            foreach (var inputPort in NodeTarget.InputPorts)
            {
                AddPort(Direction.Input, listener, inputPort.portData);
            }

            foreach (var outputPort in NodeTarget.OutputPorts)
            {
                AddPort(Direction.Output, listener, outputPort.portData);
            }
        }

        protected virtual void InitializeView()
        {
            controlsContainer = new VisualElement { name = "controls" };
            controlsContainer.AddToClassList(ControlsUssClassName);
            controlsContainer.AddToClassList(ControlsEmptyUssClassName);
            mainContainer.Q("contents").Insert(0, controlsContainer);

            var topPortsAll = new VisualElement { name = "TopPortsAll", pickingMode = PickingMode.Ignore };
            Add(topPortsAll);

            topPortContainer = new VisualElement { name = TopPortContainerName, pickingMode = PickingMode.Ignore };
            topPortsAll.Insert(0, topPortContainer);

            statePortContainer = new VisualElement { name = StatePortContainerName, pickingMode = PickingMode.Ignore };
            statePortContainer.SetEnabled(false); // todo: StateRef as inputs aren't currently supported.
            titleContainer.Insert(0, statePortContainer);

            var bottomPortsAll = new VisualElement { name = "BottomPortsAll", pickingMode = PickingMode.Ignore };
            Add(bottomPortsAll);

            bottomLeftPortContainer = new VisualElement
                { name = BottomLeftPortContainerName, pickingMode = PickingMode.Ignore };
            bottomPortsAll.Add(bottomLeftPortContainer);

            bottomPortContainer = new VisualElement { name = BottomPortContainerName, pickingMode = PickingMode.Ignore };
            bottomPortsAll.Add(bottomPortContainer);

            initializing = true;

            // Customize the title to show node types and allow renaming.
            var defaultTitleLabel = this.Q<Label>(TitleLabelName);

            titleNameContainer = new VisualElement();
            titleNameContainer.AddToClassList(TitleNameContainerUssClassName);
            titleContainer.Insert(1, titleNameContainer);

            // Move the normal title into the container.
            titleNameContainer.Add(defaultTitleLabel);

            // Setup label for node type (shown if custom name is provided)
            nodeTypeLabel = new Label(NodeTarget.DefaultName) { name = SubtitleLabelName };
            nodeTypeLabel.AddToClassList(TypeLabelUssClassName);
            titleNameContainer.Add(nodeTypeLabel);

            rightTitleContainer = new VisualElement { name = "RightTitleContainer", pickingMode = PickingMode.Ignore };
            titleContainer.Insert(2, rightTitleContainer);

            // Add renaming label and elements if it's renamable.
            if ((capabilities & Capabilities.Renamable) != 0)
            {
                SetupRenamableTitle();
            }
            UpdateTitle();

            SetPosition(NodeTarget.Position);
            SetNodeColor(NodeTarget.Color);

            AddInputContainer();

            badges = new IconBadges(this, topContainer);
        }

        private void SetupRenamableTitle()
        {
            titleTextField = new TextField { isDelayed = true };
            titleTextField.style.display = DisplayStyle.None;
            titleNameContainer.Insert(0, titleTextField);

            var titleLabel = this.Q("title-label") as Label;
            titleLabel!.RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.clickCount == 2 && e.button == (int)MouseButton.LeftMouse)
                {
                    OpenTitleEditor();
                    e.StopImmediatePropagation();
                }
            });

            titleTextField.RegisterValueChangedCallback(e => CloseAndSaveTitleEditor(e.newValue));

            titleTextField.RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.clickCount == 2 && e.button == (int)MouseButton.LeftMouse)
                {
                    CloseAndSaveTitleEditor(titleTextField.value);
                }
            });

            titleTextField.RegisterCallback<FocusOutEvent>(e => CloseAndSaveTitleEditor(titleTextField.value));

            void OpenTitleEditor()
            {
                // show title textbox
                titleTextField.style.display = DisplayStyle.Flex;
                titleLabel.style.display = DisplayStyle.None;
                titleTextField.focusable = true;

                titleTextField.SetValueWithoutNotify(title);

                // Calling focus immediately seems to cause a FocusOutEvent and call the Close handler above.
                // Delaying this call works just fine!
                EditorApplication.delayCall += () =>
                {
                    titleTextField.Focus();
                    titleTextField.SelectAll();
                };
            }

            void CloseAndSaveTitleEditor(string newTitle)
            {
                string name1 = "Renamed node " + newTitle;
                Undo.RegisterCompleteObjectUndo(Owner.Graph, name1);
                NodeTarget.CustomName = newTitle != "" ? newTitle : null;

                // hide title TextBox
                titleTextField.style.display = DisplayStyle.None;
                titleLabel.style.display = DisplayStyle.Flex;
                titleTextField.focusable = false;

                UpdateTitle();
            }
        }

        private void UpdateTitle()
        {
            bool hasCustomName = !string.IsNullOrEmpty(NodeTarget.CustomName);
            title = hasCustomName ? NodeTarget.CustomName : NodeTarget.DefaultName;
            nodeTypeLabel.style.display = hasCustomName ? DisplayStyle.Flex : DisplayStyle.None;
            titleNameContainer.EnableInClassList(TitleNameContainerHasCustomNameUssClassName, hasCustomName);
        }

        /// <summary>
        /// Sets the USS class name of the title icon element.<br/>
        /// The title icon is added if <see cref="className"/> is set.
        /// </summary>
        /// <param name="className">The additional USS class name of the title icon element.
        /// Only one additional class name is used at a time.</param>
        protected void SetTitleIcon([CanBeNull] string className)
        {
            if (currentTitleIconClass != null)
                titleIcon?.RemoveFromClassList(currentTitleIconClass);

            if (className == null)
            {
                titleIcon?.RemoveFromHierarchy();
                return;
            }

            titleIcon ??= new VisualElement { name = TitleIconName, pickingMode = PickingMode.Ignore };
            titleIcon.AddToClassList(TitleIconUssClassName);
            titleContainer.Insert(1, titleIcon);
            titleIcon.AddToClassList(currentTitleIconClass = className);
        }

        private void InitializeSettings()
        {
            if (!hasSettings)
                return;

            // Initialize settings button
            CreateSettingButton();

            settingsContainer = new NodeSettingsView { visible = false };

            // Add Node type specific settings
            settingsContainer.Add(CreateSettingsView());

            // Add [Setting] fields from the node
            var fields = NodeTarget.GetType()
                                   .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var field in fields)
            {
                if (field.GetCustomAttribute(typeof(SettingAttribute)) != null)
                {
                    AddSettingField(field);
                }
            }

            Add(settingsContainer);
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            if (settingButton != null)
            {
                var settingsButtonLayout =
                    settingButton.ChangeCoordinatesTo(settingsContainer.parent, settingButton.layout);
                settingsContainer.style.top = settingsButtonLayout.center.y + 2f;
                settingsContainer.style.left = settingsButtonLayout.center.x - 34f;
            }

            // Hide / show the collapse button
            m_CollapseButton.SetVisibility(inputContainer.childCount != 0 || outputContainer.childCount != 0 || controlsContainer.childCount != 0);
        }

        // Workaround for bug in GraphView that makes the node selection border way too big
        private VisualElement selectionBorder, nodeBorder;

        internal void EnableSyncSelectionBorderHeight()
        {
            if (selectionBorder == null || nodeBorder == null)
            {
                selectionBorder = this.Q("selection-border");
                nodeBorder = this.Q("node-border");

                schedule.Execute(() =>
                {
                    selectionBorder.style.height = nodeBorder.localBound.height;
                }).Every(17);
            }
        }

        private void CreateSettingButton()
        {
            settingButton = new Button(ToggleSettings) { name = "settings-button" };
            settingButton.Add(new Image { name = "icon", scaleMode = ScaleMode.ScaleToFit });
            settingButton.AddManipulator(new GraphTooltipManipulator { Tooltip = "Node Settings..." });

            rightTitleContainer.Add(settingButton);
        }

        private void ToggleSettings()
        {
            settingsExpanded = !settingsExpanded;
            if (settingsExpanded)
            {
                OpenSettings();
            }
            else
            {
                CloseSettings();
            }
        }

        public void OpenSettings()
        {
            if (settingsContainer != null)
            {
                Owner.ClearSelection();
                Owner.AddToSelection(this);

                settingButton.AddToClassList("clicked");
                settingsContainer.visible = true;
                settingsExpanded = true;
            }
        }

        public void CloseSettings()
        {
            if (settingsContainer != null)
            {
                settingButton.RemoveFromClassList("clicked");
                settingsContainer.visible = false;
                settingsExpanded = false;
            }
        }

        /// <summary>Gets a port via its identifier.</summary>
        public PortView GetPort(string identifier)
        {
            return AllPortViews.FirstOrDefault(p => p.Identifier == identifier);
        }

        /// <summary>Gets a port via its identifier, or creates a virtual port (<see cref="PortView.IsVirtual"/> is true).</summary>
        public PortView GetPortOrAddVirtualPort(string identifier, Direction direction)
        {
            return GetPort(identifier) ?? AddPort(direction, null, PortData.GetVirtualPortData(identifier, true));
        }

        /// <summary>Creates and adds a port to this node.</summary>
        public virtual PortView AddPort(Direction direction, [CanBeNull] EdgeConnectorListener listener, PortData portData)
        {
            PortView p = PortView.CreatePortView(this, direction, portData, listener);

            // Determine the port's location.
            (List<PortView> list, VisualElement container) = p.Location switch
            {
                PortViewLocation.State => (inputPortViews, statePortContainer),
                PortViewLocation.Top => (inputPortViews, topPortContainer),
                PortViewLocation.Left => (inputPortViews, leftPortContainer),
                PortViewLocation.BottomLeft => (outputPortViews, bottomLeftPortContainer),
                PortViewLocation.Bottom => (outputPortViews, bottomPortContainer),
                PortViewLocation.Right => (outputPortViews, rightPortContainer),
                _ => throw new ArgumentOutOfRangeException()
            };

            // Add to the views collection and container.
            list.Add(p);
            container.Add(p);

            return p;
        }

        /// <summary>Sorts the port views to match the given order of ports.</summary>
        public void SortPortContainer(VisualElement portContainer, List<NodePort> portOrder)
        {
            portContainer.Sort((elem1, elem2) =>
            {
                PortView p1 = (PortView)elem1;
                PortView p2 = (PortView)elem2;

                int index1 = portOrder.FindIndex(p => p.portData == p1.PortData);
                int index2 = portOrder.FindIndex(p => p.portData == p2.PortData);

                if (index2 == index1)
                {
                    return 0;
                }
                return index1 > index2 ? 1 : -1;
            });
        }

        public void RemovePort(PortView p)
        {
            // Remove all connected edges:
            var edgesCopy = p.Edges.ToList();
            foreach (var e in edgesCopy)
            {
                Owner.Disconnect(e, false);
            }

            if (p.direction == Direction.Input)
            {
                if (inputPortViews.Remove(p))
                {
                    p.RemoveFromHierarchy();
                }
            }
            else
            {
                if (outputPortViews.Remove(p))
                {
                    p.RemoveFromHierarchy();
                }
            }
        }

        /// <summary>Adds a message view (an attached icon and message) to this node.</summary>
        public void AddMessageView(string message, NodeMessageType messageType)
        {
            badges.AddMessageView(message, messageType);
        }

        /// <summary>Removes all message views from this node and its ports.</summary>
        public void ClearMessageViewsFromNodeAndPorts()
        {
            badges.ClearMessageViews();
            foreach (PortView port in AllPortViews)
            {
                port.ClearMessageViews();
            }
        }

        public void Highlight() => AddToClassList(HighlightedUssClassName);

        public void UnHighlight() => RemoveFromClassList(HighlightedUssClassName);

        protected void AddInputContainer()
        {
            inputContainerElement = new VisualElement { name = "input-container" };
            mainContainer.parent.Add(inputContainerElement);
            inputContainerElement.SendToBack();
            inputContainerElement.pickingMode = PickingMode.Ignore;
        }

        protected virtual void CreateNodeInspector()
        {
            var fields = NodeTarget
                         .GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                         // Filter fields from the BaseNode type since we are only interested in user-defined fields
                         // (better than BindingFlags.DeclaredOnly because we keep any inherited user-defined fields) 
                         .Where(f => f.DeclaringType != typeof(BaseNode));

            fields = SortFieldsByInheritanceLevel(fields).Reverse();

            foreach (var field in fields)
            {
                //skip if the field is a node setting
                if (field.GetCustomAttribute(typeof(SettingAttribute)) != null)
                {
                    hasSettings = true;
                    continue;
                }

                // skip if the field is not serializable
                bool serializeField = field.GetCustomAttribute(typeof(SerializeField)) != null;
                if ((!field.IsPublic && !serializeField) || field.IsNotSerialized)
                {
                    continue;
                }

                // skip if marked with NonSerialized or HideInInspector
                if (field.GetCustomAttribute(typeof(NonSerializedAttribute)) != null ||
                    field.GetCustomAttribute(typeof(HideInInspector)) != null)
                {
                    continue;
                }

                string displayName = ObjectNames.NicifyVariableName(field.Name);

                var inspectorNameAttribute = field.GetCustomAttribute<InspectorNameAttribute>();
                if (inspectorNameAttribute != null)
                {
                    displayName = inspectorNameAttribute.displayName;
                }

                AddControlField(field, displayName);
            }
        }

        /// <summary>Override the field order inside the node. It allows to re-order all the ports and field in the UI.</summary>
        /// <param name="fields">List of fields to sort</param>
        /// <returns>Sorted list of fields</returns>
        public IEnumerable<FieldInfo> SortFieldsByInheritanceLevel(IEnumerable<FieldInfo> fields)
        {
            long GetFieldInheritanceLevel(FieldInfo f)
            {
                int level = 0;
                Type t = f.DeclaringType;
                while (t != null)
                {
                    t = t.BaseType;
                    level++;
                }

                return level;
            }

            // Order by MetadataToken and inheritance level to sync the order with the port order (make sure FieldDrawers are next to the correct port)
            return fields.OrderByDescending(f => (GetFieldInheritanceLevel(f) << 32) | (uint)f.MetadataToken);
        }

        protected virtual void SetNodeColor(Color color)
        {
            titleContainer.style.borderBottomColor = new StyleColor(color);
            titleContainer.style.borderBottomWidth = new StyleFloat(color.a > 0 ? 5f : 0f);
        }

        private readonly Regex replaceNodeIndexPropertyPath = new Regex(@"(^nodes.Array.data\[)(\d+)(\])");

        internal void SyncSerializedPropertyPaths()
        {
            int nodeIndex = Owner.Graph.nodes.FindIndex(n => n == NodeTarget);

            // If the node is not found, then it means that it has been deleted from serialized data.
            if (nodeIndex == -1)
            {
                return;
            }

            var nodeIndexString = nodeIndex.ToString();
            foreach (var propertyField in this.Query<PropertyField>().ToList())
            {
                propertyField.Unbind();
                // The property path look like this: nodes.Array.data[x].fieldName
                // And we want to update the value of x with the new node index:
                propertyField.bindingPath = replaceNodeIndexPropertyPath.Replace(propertyField.bindingPath,
                    m => m.Groups[1].Value + nodeIndexString + m.Groups[3].Value);
                propertyField.Bind(Owner.SerializedGraph);
            }
        }

        protected SerializedProperty FindSerializedProperty(string fieldName)
        {
            int i = Owner.Graph.nodes.FindIndex(n => n == NodeTarget);
            return Owner.SerializedGraph.FindProperty("nodes").GetArrayElementAtIndex(i)
                        .FindPropertyRelative(fieldName);
        }

        protected VisualElement AddControlField(FieldInfo field, string label = null, bool showInputDrawer = false)
        {
            if (field == null)
            {
                return null;
            }

            bool showLabel = field.GetCustomAttribute<HideLabel>() == null && !showInputDrawer;
            SerializedProperty property = FindSerializedProperty(field.Name);
            var element = new PropertyField(property, showLabel ? label : "");
            element.Bind(Owner.SerializedGraph);

            if (typeof(IList).IsAssignableFrom(field.FieldType))
            {
                EnableSyncSelectionBorderHeight();
            }

            // When the serialized property for the UI changes. More robust and precise than tracking changes
            // in the element with RegisterValueChangedCallback, which also fires on element setup.
            element.TrackPropertyValue(property, p =>
            {
                NodeTarget.PropertiesHaveChanged();
            });

            // Disallow picking scene objects
            var objectField = element.Q<ObjectField>();
            if (objectField != null)
            {
                objectField.allowSceneObjects = false;
            }

            if (showInputDrawer)
            {
                var box = new VisualElement { name = field.Name };
                box.AddToClassList("port-input-element");
                box.Add(element);
                inputContainerElement.Add(box);
            }
            else
            {
                controlsContainer.Add(element);
                controlsContainer.RemoveFromClassList(ControlsEmptyUssClassName);
            }
            element.name = field.Name;

            return element;
        }

        protected void AddSettingField(FieldInfo field)
        {
            if (field == null)
            {
                return;
            }

            var label = field.GetCustomAttribute<SettingAttribute>().name;

            var element = new PropertyField(FindSerializedProperty(field.Name));
            element.Bind(Owner.SerializedGraph);

            settingsContainer.Add(element);
            element.label = label ?? field.Name;
        }

        internal void OnPortConnected(PortView port)
        {
            if (port.direction == Direction.Input && inputContainerElement?.Q(port.Identifier) != null)
            {
                inputContainerElement.Q(port.Identifier).AddToClassList("empty");
            }

            onPortConnected?.Invoke(port);
        }

        internal void OnPortDisconnected(PortView port)
        {
            if (port.direction == Direction.Input && inputContainerElement?.Q(port.Identifier) != null)
            {
                inputContainerElement.Q(port.Identifier).RemoveFromClassList("empty");
            }

            onPortDisconnected?.Invoke(port);
        }

        // TODO: a function to force to reload the custom behavior portViews (if we want to do a button to add portViews for example)
        public override void SetPosition(Rect rect)
        {
            base.SetPosition(rect);

            if (!initializing)
            {
                Undo.RegisterCompleteObjectUndo(Owner.Graph, "Moved graph node");
            }

            NodeTarget.Position = rect.position;
            initializing = false;
        }

        public void SetPosition(float2 pos)
        {
            var rect = GetPosition();
            rect.position = pos;
            SetPosition(rect);
        }

        public void ChangeLockStatus()
        {
            NodeTarget.IsLocked ^= true;
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            base.BuildContextualMenu(evt);
            
            if (NodeTarget.Lockable)
            {
                evt.menu.AppendAction(NodeTarget.IsLocked ? "Unlock" : "Lock", e => ChangeLockStatus(),
                    action => DropdownMenuAction.Status.Normal);
            }

            evt.menu.AppendAction("Dump Debug Data", action =>
            {
                BaseNode node = NodeTarget;
                Debug.Log($"{node.DefaultName}");
                Debug.Log($"{node.GetType().FullName}");
                Debug.Log($"{node.GUID}");
                foreach (var port in node.InputPorts)
                {
                    Debug.Log($"\tInput Port: {port.portData.identifier} :: {JsonUtility.ToJson(port.portData)}");
                    foreach (var edge in port.GetEdges())
                    {
                        Debug.Log($"\t\t{edge}");
                    }
                }
                foreach (var port in node.OutputPorts)
                {
                    Debug.Log($"\tOutput Port: {port.portData.identifier} :: {JsonUtility.ToJson(port.portData)}");
                    foreach (var edge in port.GetEdges())
                    {
                        Debug.Log($"\t\t{edge}");
                    }
                }
            });
        }

        public virtual bool RefreshAllPorts()
        {
            // If a port behavior was attached to one port, then
            // the port count might have been updated by the node,
            // so we have to refresh the list of port views.
            SyncPortViewsWithPorts(NodeTarget.InputPorts, inputPortViews, true);
            SyncPortViewsWithPorts(NodeTarget.OutputPorts, outputPortViews, false);

            void SyncPortViewsWithPorts(List<NodePort> ports, List<PortView> views, bool inputOrOutput)
            {
                if (ports.Count == 0 && views.Count == 0) // Nothing to update
                {
                    return;
                }

                // When there are no current portviews, we can't zip the list, so we just add all.
                var listener = Owner.ConnectorListener;

                // Remove views that don't have connections.
                using (ListPool<PortView>.Get(out List<PortView> portsToRemove))
                {
                    foreach (PortView portView in views)
                    {
                        // Only remove ports without connections that have no matching identifiers in the serialized data.
                        // We can use the identifier here because this function will only be called when there is a custom port behavior.
                        if (!portView.connected && ports.All(p => p.portData.identifier != portView.PortData.identifier))
                        {
                            portsToRemove.Add(portView);
                        }
                    }

                    foreach (PortView portView in portsToRemove)
                    {
                        RemovePort(portView);
                        views.Remove(portView);
                    }
                }

                // Add views for new portViews.
                foreach (var p in (IEnumerable<NodePort>)ports)
                {
                    // Add missing port views
                    if (views.All(pv => p.portData.identifier != pv.PortData.identifier))
                    {
                        Direction portDirection = inputOrOutput ? Direction.Input : Direction.Output;
                        var pv = AddPort(portDirection, listener, p.portData);
                        views.Add(pv);
                    }
                }

                // Reorder views to match portViews
                if (ports == NodeTarget.InputPorts)
                {
                    SortPortContainer(topPortContainer, ports);
                    SortPortContainer(leftPortContainer, ports);
                }
                if (ports == NodeTarget.OutputPorts)
                {
                    SortPortContainer(bottomPortContainer, ports);
                    SortPortContainer(rightPortContainer, ports);
                }

                // Now that the order and count are synced,
                // Update the views with data and properties from the portViews.
                for (int i = 0, portIndex = 0; i < views.Count; i++)
                {
                    PortView portView = views[i];
                    if (portView.IsVirtual)
                    {
                        continue;
                    }
                    portView.UpdatePortView(ports[portIndex++].portData);
                }
            }

            return RefreshPorts();
        }

        protected virtual VisualElement CreateSettingsView() => new Label("Settings") { name = "header" };
    }
}
