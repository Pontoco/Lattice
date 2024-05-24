using Lattice.Nodes;
using UnityEngine.UIElements;

namespace Lattice.Editor.Views
{
    /// <summary>
    /// An inspector-like panel for Lattice that shows debug values for a node, as well as more detailed compilation information. 
    /// </summary>
    public class NodePanelView : VisualElement
    {
        public LatticeNode Target;
    }
}
