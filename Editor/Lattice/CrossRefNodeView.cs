using Lattice.Editor.Manipulators;
using Lattice.Editor.SearchProviders;
using Lattice.Nodes;
using UnityEditor;
using UnityEngine.UIElements;

namespace Lattice.Editor.Views
{
    [NodeCustomEditor(typeof(CrossRefNode))]
    public class CrossRefNodeView : LatticeNodeView
    {
        public const string SelectNodeButtonUssClassName = UssClassName + "__select-node-button";
        public const string GoToButtonUssClassName = UssClassName + "__goto-button";
        
        private Button selectNode;
        private Button gotoNode;

        protected override void CreateNodeInspector()
        {
            styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.pontoco.lattice/Editor/UI/CrossRefNodeView.uss"));

            selectNode = new Button(() =>
            {
                AllPortsSearchProvider search = AllPortsSearchProvider.Instance;
                search.Callback = port =>
                {
                    CrossRefNode node = (CrossRefNode)NodeTarget;
                    node.SetTarget(port.owner, port.portData.identifier);
                    UpdateText();
                };
                search.Show(selectNode);
            });
            selectNode.AddToClassList(SelectNodeButtonUssClassName);

            gotoNode = new Button(() =>
            {
                var resolvedNode = ((CrossRefNode)NodeTarget).ResolvedNode;
                if (resolvedNode == null)
                    return;
                LatticeGraphWindow.OpenWindow(resolvedNode);
            });
            gotoNode.AddToClassList(GoToButtonUssClassName);
            gotoNode.AddManipulator(new GraphTooltipManipulator { Tooltip = "Go to..." });

            UpdateText();
            controlsContainer.Add(selectNode);
            controlsContainer.Add(gotoNode);
            controlsContainer.RemoveFromClassList(ControlsEmptyUssClassName);
            
            titleContainer.style.display = DisplayStyle.None;
        }

        public void UpdateText()
        {
            CrossRefNode node = (CrossRefNode)NodeTarget;

            if (node.ResolvedNode == null)
            {
                if (node.IsDisconnected())
                {
                    selectNode.text = "Node Missing🙁";
                }
                else
                {
                    selectNode.text = "Select Node..";
                }
                gotoNode.SetEnabled(false);
            }
            else
            {
                selectNode.text = node.GetResolvedPath();
                gotoNode.SetEnabled(true);
            }
        }
    }
}
