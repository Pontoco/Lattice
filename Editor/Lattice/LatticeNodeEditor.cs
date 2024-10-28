using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Lattice.Base;
using Lattice.Editor.Tools;
using Lattice.Editor.Utils;
using Lattice.IR;
using Lattice.Nodes;
using Lattice.StandardLibrary;
using Lattice.Utils;
using Unity.Entities;
using Unity.Entities.UI;
using UnityEditor;
using UnityEngine.UIElements;
using UssClasses = Unity.Entities.Editor.UssClasses;
using EntitiesResources = Unity.Entities.Editor.Resources;

namespace Lattice.Editor.Views
{
    /// <summary>Editor for Nodes selected in Lattice. Shown in <see cref="LatticeInspector"/>.</summary>
    internal sealed class LatticeNodeEditor : VisualElement
    {
        public const string UssClassName = "lattice-node-editor";
        public const string StateLabelUssClassName = UssClassName + "__state-label";
        public const string StateContainerUssClassName = UssClassName + "__state-container";
        public const string HeaderButtonUssClassName = "header-button";

        private static string GetPinnedStateKey(string fileId) => $"{nameof(LatticeNodeEditor)}_{fileId}__Pinned";
        private static string GetExpandedStateKey(string fileId) => $"{nameof(LatticeNodeEditor)}_{fileId}__Expanded";
        public static bool IsPinned(string fileId) => SessionState.GetBool(GetPinnedStateKey(fileId), false);
        public static bool IsExpanded(string fileId) => SessionState.GetBool(GetExpandedStateKey(fileId), true);

        public readonly LatticeNode Node;
        public readonly LatticeNodeView View;

        private readonly PropertyElement valuePropertyElement;
        private readonly VisualElement stateRoot;
        private readonly PropertyElement statePropertyElement;
        private readonly HelpBox propertyHelpBox;
        private readonly Foldout foldout;
        private readonly Button pinButton;

        private bool pinned;

        /// <summary>If an Editor is pinned it will stay in the Inspector even when the node is unselected. This is represented as a lock icon.</summary>
        public bool Pinned
        {
            get => pinned;
            private set
            {
                pinned = value;
                SessionState.SetBool(GetPinnedStateKey(Node.FileId), value);
            }
        }

        /// <inheritdoc />
        public override VisualElement contentContainer { get; }

        public LatticeNodeEditor(LatticeNodeView view)
        {
            View = view;
            Node = view.Target;

            AddToClassList(UssClassName);
            AddToClassList("inspector");
            AddToClassList("component-header");
            EntitiesResources.Templates.Inspector.ComponentHeader.Clone(this);
            foldout = this.Q<Foldout>(className: UssClasses.Inspector.Component.Header);
            foldout.contentContainer.AddToClassList(UssClasses.Inspector.Component.Container);
            foldout.text = Node.Name;
            
            // Header context menu
            foldout.Q<Toggle>().AddManipulator(new ContextualMenuManipulator(evt =>
            {
                if (Node is ScriptNode scriptNode)
                {
                    evt.menu.AppendAction("Edit Script", _ => SourceUtility.OpenAtMethod(scriptNode.Method.Resolve()));
                }
            }));

            pinned = IsPinned(Node.FileId);
            foldout.value = IsExpanded(Node.FileId);

            var label = foldout.Q<Label>(className: UssClasses.UIToolkit.Toggle.Text);
            label.name = "ComponentName";
            label.AddToClassList(UssClasses.Inspector.Component.Name);

            var input = foldout.Q<VisualElement>(className: UssClasses.UIToolkit.Toggle.Input);
            input.AddToClassList("shrink");

            // Icon
            var icon = new VisualElement
            {
                name = "ComponentIcon"
            };
            icon.AddToClassList(UssClasses.Inspector.Component.Icon);
            icon.AddToClassList(UssClasses.Inspector.Icons.Small);
            string iconClass = view.TitleIconClass;
            if (iconClass != null)
            {
                icon.AddToClassList(iconClass);
            }
            else
            {
                switch (Node)
                {
                    case ScriptNode:
                        icon.AddToClassList("script-node-icon");
                        break;
                    case PrefabReference:
                        icon.AddToClassList("prefab-reference-node-icon");
                        break;
                    default:
                        icon.AddToClassList("default-node-icon");
                        break;
                }
            }
            input.Insert(1, icon);

            // Pin button
            pinButton = new Button(() =>
            {
                Pinned = !Pinned;
                pinButton!.EnableInClassList("pin-button--active", Pinned);
                if (!Pinned)
                {
                    // If the node editor was unpinned, and it's not selected, remove it.
                    if (!View.Owner.selection.Contains(View))
                    {
                        RemoveFromHierarchy();
                    }
                }
            })
            {
                tooltip = "Pin node to inspector"
            };
            pinButton.AddToClassList("pin-button");
            pinButton.AddToClassList(HeaderButtonUssClassName);
            pinButton.EnableInClassList("pin-button--active", Pinned);
            input.Add(pinButton);

            // GoTo button
            Button goToButton = new(() =>
            {
                View.Owner.ClearSelection();
                View.Owner.AddToSelection(View);
                View.Owner.FrameSelection();
            })
            {
                tooltip = "Select node"
            };
            goToButton.AddToClassList("goto-button");
            goToButton.AddToClassList(HeaderButtonUssClassName);
            input.Add(goToButton);

            contentContainer = foldout.contentContainer;

            // Show a label when the node is marked as nullable-lifted.
            if (View.ClassListContains(LatticeNodeView.LiftedToNullableUssClassName))
            {
                VisualElement nullableNotice = new()
                {
                    tooltip = "This node doesn't run when null is passed to a non-nullable port. Null is passed onwards."
                };
                nullableNotice.AddToClassList("nullable-lifted-notice");
                nullableNotice.Add(new StripedElement());
                Label nullableHelpBox = new("This node executes conditionally.") { pickingMode = PickingMode.Ignore };
                nullableNotice.Add(nullableHelpBox);
                Add(nullableNotice);
            }

            // State variables.
            Add(stateRoot = new VisualElement());
            stateRoot.AddToClassList(StateContainerUssClassName);
            Label stateLabel = new("State");
            stateRoot.Add(stateLabel);
            stateLabel.AddToClassList(StateLabelUssClassName);
            stateRoot.Add(statePropertyElement = new PropertyElement());
            statePropertyElement.SetEnabled(false);
            statePropertyElement.AddToClassList(BaseField<int>.alignedFieldUssClassName);

            // Value variables.
            Add(valuePropertyElement = new PropertyElement());
            valuePropertyElement.SetEnabled(false);
            valuePropertyElement.AddToClassList(BaseField<int>.alignedFieldUssClassName);
            
            // Help box.
            Add(propertyHelpBox = new HelpBox());
            propertyHelpBox.AddToClassList("unity-help-box--small");

            // Register changes when attaching to the panel, modifying the foldout, or when Lattice executes.
            foldout.RegisterValueChangedCallback(evt =>
            {
                UpdateDebugValue(ExecutionHistory.MostRecent);
                SessionState.SetBool(GetExpandedStateKey(Node.FileId), evt.newValue);
            });
            RegisterCallback<AttachToPanelEvent, LatticeNodeEditor>(static (_, args) =>
            {
                args.UpdateDebugValue(ExecutionHistory.MostRecent);
                LatticeDebugUpdateSystem.OnLatticeExecute += args.UpdateDebugValue;
                args.Node.OnPropertiesChanged += args.OnNodePropertiesChanged;
            }, this);

            // Unsubscribe.
            RegisterCallback<DetachFromPanelEvent, LatticeNodeEditor>(static (_, args) =>
            {
                LatticeDebugUpdateSystem.OnLatticeExecute -= args.UpdateDebugValue;
                args.Node.OnPropertiesChanged -= args.OnNodePropertiesChanged;
            }, this);

            // When the inspector is resized we need to handle the label sizing.
            RegisterCallback<GeometryChangedEvent, VisualElement>((_, args) =>
            {
                // ReSharper disable once ConvertClosureToMethodGroup
                args.Query<PropertyElement>().ForEach(e => StylingUtility.AlignInspectorLabelWidth(e));
            }, contentContainer);
        }

        private void OnNodePropertiesChanged(BaseNode node)
        {
            foldout.text = node.Name;
        }

        private enum UpdateType
        {
            HelpBox,
            Value,
            StateAndValue
        }

        private void UpdateDebugValue(IRExecution execution)
        {
            if (panel == null || !foldout.value)
            {
                return;
            }

            UpdateType updateType = Update();
            if (updateType != UpdateType.HelpBox)
            {
                propertyHelpBox.style.display = DisplayStyle.None;
                valuePropertyElement.style.display = DisplayStyle.Flex;
                stateRoot.style.display = updateType == UpdateType.StateAndValue ? DisplayStyle.Flex : DisplayStyle.None;
            }
            else
            {
                propertyHelpBox.style.display = DisplayStyle.Flex;
                valuePropertyElement.style.display = DisplayStyle.None;
                stateRoot.style.display = DisplayStyle.None;
            }
            return;

            // Returns true if propertyElement is used.
            UpdateType Update()
            {
                if (execution == null || !execution.Graph.Mappings.ContainsKey(Node))
                {
                    propertyHelpBox.messageType = HelpBoxMessageType.Info;
                    propertyHelpBox.text = "Node was not in executed graph.";
                    propertyHelpBox.tooltip = $"Ensure the graph has been added to an Entity using a {nameof(LatticeExecutorAuthoring)} component.";
                    return UpdateType.HelpBox;
                }

                if (execution.DebugData == null)
                {
                    propertyHelpBox.messageType = HelpBoxMessageType.Info;
                    propertyHelpBox.text = "The graph was not compiled in debug mode.";
                    propertyHelpBox.tooltip = $"Toggle {MenuItems.DisableDebugMenu} off.";
                    return UpdateType.HelpBox;
                }

                IRNode primaryNode = execution.Graph.Mappings[Node].PrimaryNode.Node;
                if (!execution.Graph.MetadataDb.ContainsKey(primaryNode))
                {
                    propertyHelpBox.messageType = HelpBoxMessageType.Info;
                    propertyHelpBox.text = "This node was not compiled.";
                    propertyHelpBox.tooltip = "";
                    return UpdateType.HelpBox;
                }

                if (!((LatticeGraphView)View.Owner).TryGetViewingEntity(execution.Graph, primaryNode, out Entity entity))
                {
                    propertyHelpBox.messageType = HelpBoxMessageType.Info;
                    propertyHelpBox.text = "No entity selected in toolbar.";
                    propertyHelpBox.tooltip = "Make sure a loaded SubScene has executed the graph.";
                    return UpdateType.HelpBox;
                }

                if (execution.DebugData.Values.TryGetValue(entity, primaryNode, out object value))
                {
                    switch (value)
                    {
                        case null:
                            propertyHelpBox.messageType = HelpBoxMessageType.None;
                            propertyHelpBox.text = "null";
                            propertyHelpBox.tooltip = "";
                            return UpdateType.HelpBox;
                        case Exception e:
                            propertyHelpBox.messageType = HelpBoxMessageType.Error;
                            propertyHelpBox.text = $"Node threw an {e.GetType().Name}.";
                            propertyHelpBox.tooltip = "";
                            return UpdateType.HelpBox;
                    }

                    SetTarget(valuePropertyElement, value);

                    // Rename the items in the ValueTuple to match their identifier names.
                    for (int i = valuePropertyElement.childCount - 1, j = View.OutputPortViews.Count - 1; i >= 0 && j >= 0; i--, j--)
                    {
                        valuePropertyElement[i].Q<Label>().text = GraphUtils.NicifyIdentifierName(View.OutputPortViews[j].Identifier);
                    }

                    if (Node.StateType != null)
                    {
                        IRNode stateDebugNode = execution.Graph.Mappings[Node].StateDebugNode!.Node;
                        if (execution.DebugData.Values.TryGetValue(entity, stateDebugNode, out object state))
                        {
                            SetTarget(statePropertyElement, state);

                            // Rename the state item to match its identifier.
                            if (statePropertyElement.childCount == 1 && View.InputPortViews.Count > 0)
                            {
                                statePropertyElement[0].Q<Label>().text = GraphUtils.NicifyIdentifierName(View.InputPortViews.FirstOrDefault(v => v.Location == PortViewLocation.State)?.Identifier ?? "State");
                            }
                            return UpdateType.StateAndValue;
                        }

                        propertyHelpBox.messageType = HelpBoxMessageType.Warning;
                        propertyHelpBox.text = "Missing state value!";
                        propertyHelpBox.tooltip = "";
                        return UpdateType.HelpBox;
                    }
                    return UpdateType.Value;
                }

                propertyHelpBox.messageType = HelpBoxMessageType.None;
                propertyHelpBox.text = "Node not executed.";
                propertyHelpBox.tooltip = "";
                return UpdateType.HelpBox;
            }
        }

        private static readonly MethodInfo SetTargetMethod = typeof(PropertyElement)
            .GetMethod(nameof(PropertyElement.SetTarget), BindingFlags.Public | BindingFlags.Instance);

        private static readonly Dictionary<Type, MethodInfo> SetTargetMethods = new();
        private static readonly object[] SetTargetMethodArgs = new object[1];

        private static void SetTarget(PropertyElement element, object target)
        {
            // Irritatingly passing an object that has fields of types that match types that support conversion works.
            // But passing those types themselves as object will result in the type being detected, but its value not being set.
            // So here we just generate the exact call that provides the correct type, and now nothing untoward occurs.
            Type type = target.GetType();
            if (!SetTargetMethods.TryGetValue(type, out var method))
            {
                SetTargetMethods.Add(type, method = SetTargetMethod.MakeGenericMethod(type));
            }
            SetTargetMethodArgs[0] = target;
            method.Invoke(element, SetTargetMethodArgs);
            
            // Also, objects that match the above criteria and don't have many children don't update their values when we set the target.
            // So we force a rebuild of the element so the value is properly updated.
            if (element.childCount == 1)
                element.ForceReload();

            // Ensure the labels are aligned.
            StylingUtility.AlignInspectorLabelWidth(element);
        }
    }
}
