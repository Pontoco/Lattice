using Lattice.Editor.Events;
using UnityEngine.UIElements;

namespace Lattice.Editor.Manipulators
{
    /// <summary>Add to a <see cref="VisualElement"/> to show simple tooltips in a <see cref="LatticeGraphWindow"/>.</summary>
    public sealed class GraphTooltipManipulator : Manipulator
    {
        /// <summary>The tooltip to decorate the target element with when hovered.</summary>
        public string Tooltip { get; set; }

        /// <inheritdoc />
        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<GraphTooltipEvent>(SetTooltip);
        }

        /// <inheritdoc />
        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<GraphTooltipEvent>(SetTooltip);
        }
        
        private void SetTooltip(GraphTooltipEvent evt)
        {
            evt.SetTooltip(target, Tooltip);
        }
    }
}
