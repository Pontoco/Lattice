using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lattice.Editor.Views
{
    public class GroupView : Group
    {
        public BaseGraphView owner;
        public Base.Group group;

        private Label titleLabel;
        private ColorField colorField;

        public GroupView()
        {
            styleSheets.Add(
                AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.pon.lattice/Editor/UI/GroupView.uss"));
        }

        private static void BuildContextualMenu(ContextualMenuPopulateEvent evt) { }

        public void Initialize(BaseGraphView graphView, Base.Group block)
        {
            group = block;
            owner = graphView;

            title = block.title;
            SetPosition(block.position);

            this.AddManipulator(new ContextualMenuManipulator(BuildContextualMenu));

            headerContainer.Q<TextField>().RegisterCallback<ChangeEvent<string>>(TitleChangedCallback);
            titleLabel = headerContainer.Q<Label>();

            colorField = new ColorField { value = group.color, name = "headerColorPicker" };
            colorField.RegisterValueChangedCallback(e =>
            {
                UpdateGroupColor(e.newValue);
            });
            UpdateGroupColor(group.color);

            headerContainer.Add(colorField);

            InitializeInnerNodes();
        }

        private void InitializeInnerNodes()
        {
            foreach (var nodeGUID in group.innerNodeGUIDs.ToList())
            {
                if (!owner.Graph.NodesPerGuid.ContainsKey(nodeGUID))
                {
                    Debug.LogWarning("Node GUID not found: " + nodeGUID);
                    group.innerNodeGUIDs.Remove(nodeGUID);
                    continue;
                }
                var node = owner.Graph.NodesPerGuid[nodeGUID];
                var nodeView = owner.NodeViewsPerNode[node];

                AddElement(nodeView);
            }
        }

        protected override void OnElementsAdded(IEnumerable<GraphElement> elements)
        {
            foreach (var element in elements)
            {
                var node = element as BaseNodeView;

                // Adding an element that is not a node currently supported
                if (node == null)
                {
                    continue;
                }

                if (!group.innerNodeGUIDs.Contains(node.NodeTarget.GUID))
                {
                    group.innerNodeGUIDs.Add(node.NodeTarget.GUID);
                }
            }
            base.OnElementsAdded(elements);
        }

        protected override void OnElementsRemoved(IEnumerable<GraphElement> elements)
        {
            // Only remove the nodes when the group exists in the hierarchy
            if (parent != null)
            {
                foreach (var elem in elements)
                {
                    if (elem is BaseNodeView nodeView)
                    {
                        group.innerNodeGUIDs.Remove(nodeView.NodeTarget.GUID);
                    }
                }
            }

            base.OnElementsRemoved(elements);
        }

        public void UpdateGroupColor(Color newColor)
        {
            group.color = newColor;
            style.backgroundColor = newColor;
        }

        private void TitleChangedCallback(ChangeEvent<string> e)
        {
            group.title = e.newValue;
        }

        public override void SetPosition(Rect newPos)
        {
            base.SetPosition(newPos);

            group.position = newPos;
        }
    }
}
