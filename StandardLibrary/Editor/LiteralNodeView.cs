using Lattice.Base;
using Lattice.Editor;
using Lattice.Editor.Views;
using Lattice.Nodes;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lattice.StandardLibrary.Editor
{
    [NodeCustomEditor(typeof(LiteralNode<>))]
    public class LiteralNodeView : LatticeNodeView
    {
        public override void Initialize(BaseGraphView owner, BaseNode node)
        {
            base.Initialize(owner, node);
            LatticeNode n = (LatticeNode)NodeTarget;

            styleSheets.Add(
                AssetDatabase.LoadAssetAtPath<StyleSheet>(
                    "Packages/com.pon.lattice/Editor/UI/LatticeGraphWindow.uxml"));

            //    Find and remove expand/collapse button
            titleContainer.Remove(titleContainer.Q("title-button-container"));

            PropertyField propertyField = controlsContainer.Q<PropertyField>("Value");
            propertyField.AddToClassList("hide-label");
            propertyField.style.minWidth = 25;
            controlsContainer.Remove(propertyField);

            if (n.OutputPorts.Count != 1)
            {
                // This node should always have one output. Internal System Error.
                title = "ICE: Node Malformed. Incorrect outputs";
            }

            // title = GraphUtils.GetReadableTypeName(n.OutputPorts[0].portData.defaultType);
            titleContainer.Add(propertyField);
        }
    }
}
