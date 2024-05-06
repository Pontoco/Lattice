using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lattice.Editor.Views
{
    public class NodeSettingsView : VisualElement
    {
        public NodeSettingsView()
        {
            pickingMode = PickingMode.Ignore;
            styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.pon.lattice/Editor/UI/NodeSettings.uss"));
            AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Packages/com.pon.lattice/Editor/UI/NodeSettings.uxml").CloneTree(this);

            // Get the element we want to use as content container
            contentContainer = this.Q("contentContainer");
            RegisterCallback<MouseDownEvent>(OnMouseDown);
            RegisterCallback<MouseUpEvent>(OnMouseUp);
        }

        private void OnMouseUp(MouseUpEvent evt)
        {
            evt.StopPropagation();
        }

        private void OnMouseDown(MouseDownEvent evt)
        {
            evt.StopPropagation();
        }

        public override VisualElement contentContainer { get; }
    }
}
