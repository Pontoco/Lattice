using System.Collections.Generic;
using Lattice.Base;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lattice.Editor.Views
{
    /// <summary>Stack node view implementation, can be used to stack multiple node inside a SharedContext like VFX graph does.</summary>
    public class BaseStackNodeView : StackNode
    {
        public delegate void ReorderNodeAction(BaseNodeView nodeView, int oldIndex, int newIndex);

        /// <summary>StackNode data from the graph</summary>
        protected internal BaseStackNode stackNode;

        protected BaseGraphView owner;

        /// <summary>Triggered when a node is re-ordered in the stack.</summary>
        public event ReorderNodeAction onNodeReordered;

        public BaseStackNodeView(BaseStackNode stackNode)
        {
            this.stackNode = stackNode;
            styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.pon.lattice/Editor/UI/BaseStackNodeView.uss"));
        }

        /// <inheritdoc />
        protected override void OnSeparatorContextualMenuEvent(ContextualMenuPopulateEvent evt, int separatorIndex)
        {
            // TODO: write the SharedContext menu for stack node
        }

        /// <summary>Called after the StackNode have been added to the graph view</summary>
        public virtual void Initialize(BaseGraphView graphView)
        {
            owner = graphView;
            headerContainer.Add(new Label(stackNode.title));

            SetPosition(new Rect(stackNode.position, Vector2.one));

            InitializeInnerNodes();
        }

        private void InitializeInnerNodes()
        {
            int i = 0;
            // Sanitize the GUID list in case some nodes were removed
            stackNode.nodeGUIDs.RemoveAll(nodeGUID =>
            {
                if (owner.Graph.NodesPerGuid.ContainsKey(nodeGUID))
                {
                    var node = owner.Graph.NodesPerGuid[nodeGUID];
                    var view = owner.NodeViewsPerNode[node];
                    view.AddToClassList("stack-child__" + i);
                    i++;
                    AddElement(view);
                    return false;
                }
                return true; // remove the entry as the GUID doesn't exist anymore
            });
        }

        /// <inheritdoc />
        public override void SetPosition(Rect newPos)
        {
            base.SetPosition(newPos);

            stackNode.position = newPos.position;
        }

        /// <inheritdoc />
        protected override bool AcceptsElement(GraphElement element, ref int proposedIndex, int maxIndex)
        {
            bool accept = base.AcceptsElement(element, ref proposedIndex, maxIndex);

            if (accept && element is BaseNodeView nodeView)
            {
                var index = Mathf.Clamp(proposedIndex, 0, stackNode.nodeGUIDs.Count - 1);

                int oldIndex = stackNode.nodeGUIDs.FindIndex(g => g == nodeView.NodeTarget.GUID);
                if (oldIndex != -1)
                {
                    stackNode.nodeGUIDs.Remove(nodeView.NodeTarget.GUID);
                    if (oldIndex != index)
                    {
                        onNodeReordered?.Invoke(nodeView, oldIndex, index);
                    }
                }

                stackNode.nodeGUIDs.Insert(index, nodeView.NodeTarget.GUID);
            }

            return accept;
        }

        public override bool DragLeave(DragLeaveEvent evt, IEnumerable<ISelectable> selection, IDropTarget leftTarget,
                                       ISelection dragSource)
        {
            foreach (var elem in selection)
            {
                if (elem is BaseNodeView nodeView)
                {
                    stackNode.nodeGUIDs.Remove(nodeView.NodeTarget.GUID);
                }
            }
            return base.DragLeave(evt, selection, leftTarget, dragSource);
        }
    }
}
