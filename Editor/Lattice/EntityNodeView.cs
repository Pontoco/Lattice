using Lattice.Nodes;

namespace Lattice.Editor.Views
{
    [NodeCustomEditor(typeof(EntityNode))]
    public class EntityNodeView : LatticeNodeView
    {
        /// <inheritdoc />
        protected override void CreateNodeInspector()
        {
            base.CreateNodeInspector();
            SetTitleIcon("unity-entity-icon");
        }
    }
}
