using System.Linq;
using Lattice.Base;
using Lattice.Editor.Manipulators;
using Lattice.Editor.Utils;
using Lattice.Nodes;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;

namespace Lattice.Editor.Views
{
    /// <summary>The VisualElement for <see cref="RedirectNode"/></summary>
    [NodeCustomEditor(typeof(RedirectNode))]
    public sealed class RedirectNodeView : LatticeNodeView
    {
        public new const string UssClassName = "redirect-node";
        public const string PortPickerUssClassName = UssClassName + "__port-picker";
        public const string PortPickerInnerUssClassName = UssClassName + "__port-picker__inner";
        public const string PortPickerTopUssClassName = PortPickerUssClassName + "--top";
        public const string PortPickerBottomUssClassName = PortPickerUssClassName + "--bottom";

        public new RedirectNode Target => (RedirectNode)NodeTarget;

        private string typeUssClassName;

        public RedirectNodeView()
        {
            AddToClassList(UssClassName);
            AddToClassList(PortView.UssClassName);
            
            // Renaming by double-click.
            RegisterCallback<MouseDownEvent, RedirectNodeView>(static (e, args) =>
            {
                if (e.button != (int)MouseButton.LeftMouse || e.clickCount != 2)
                {
                    return;
                }
                args.OpenTitleEditor();
                e.StopImmediatePropagation();
            }, this);

            AddPortPicker(PortPickerTopUssClassName, Direction.Input);
            AddPortPicker(PortPickerBottomUssClassName, Direction.Output);
            return;

            void AddPortPicker(string className, Direction direction)
            {
                VisualElement portPicker = new();
                portPicker.AddToClassList(PortPickerUssClassName);
                portPicker.AddToClassList(className);
                VisualElement inner = new() { pickingMode = PickingMode.Ignore };
                inner.AddToClassList(PortPickerInnerUssClassName);
                portPicker.Add(inner);
                Add(portPicker);
                portPicker.RegisterCallback<MouseDownEvent>(e =>
                {
                    if (e.clickCount != 1 || e.button != 0)
                    {
                        return;
                    }
                    PortView view = direction == Direction.Input ? 
                        this.Query<PortView>().Where(p => p.direction == Direction.Input).First() :
                        this.Query<PortView>().Where(p => p.direction == Direction.Output).First();
                    if (view == null)
                    {
                        return;
                    }
                    var connector = (EdgeConnectionMouseManipulator)view.edgeConnector;
                    connector.TryStartDragging(e);
                });
            }
        }

        /// <inheritdoc />
        public override PortView AddPort(Direction direction, EdgeConnectorListener listener, PortData portData)
        {
            var port = base.AddPort(direction, listener, portData);
            // We don't want to allow selection of the port.
            port.pickingMode = PickingMode.Ignore;

            UpdateTypeUssClass();
            return port;
        }

        /// <inheritdoc />
        public override bool RefreshAllPorts()
        {
            bool result = base.RefreshAllPorts();
            UpdateTypeUssClass();
            return result;
        }

        private void UpdateTypeUssClass()
        {
            // Apply the port's type from the USS class to the redirect node so we can color it correctly.
            string portVisualClass = AllPortViews.FirstOrDefault()?.visualClass;
            if (portVisualClass != null && portVisualClass != typeUssClassName)
            {
                RemoveFromClassList(typeUssClassName);
                AddToClassList(typeUssClassName = portVisualClass);
            }
        }

        /// <inheritdoc />
        public override void Initialize(BaseGraphView owner, BaseNode node)
        {
            base.Initialize(owner, node);

            styleSheets.Add(
                AssetDatabase.LoadAssetAtPath<StyleSheet>(
                    "Packages/com.pontoco.lattice/Editor/UI/RedirectNodeView.uss"));
            
            // Move the title container to the root
            hierarchy.Add(this.Q(TitleContainerName));
        }
    }
}
