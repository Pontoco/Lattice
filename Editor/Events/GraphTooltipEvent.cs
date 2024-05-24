using JetBrains.Annotations;
using Lattice.Editor.Manipulators;
using Lattice.Editor.Views;
using UnityEngine.UIElements;

namespace Lattice.Editor.Events
{
    /// <summary>
    ///     Sends an event that finds tooltips when not using <see cref="IHasGraphTooltip" /> types. This event bubbles up.<br />
    ///     See <see cref="GraphTooltipManipulator"/> if you want to add a tooltip to an element.
    /// </summary>
    internal sealed class GraphTooltipEvent : EventBase<GraphTooltipEvent>
    {
        /// <summary>The tooltip shown.</summary>
        public string Tooltip { get; private set; }

        /// <summary>The element that set the tooltip. Null if no tooltip was found in the propagation path.</summary>
        [CanBeNull]
        public VisualElement Element { get; private set; }
        
        /// <summary>Sets the tooltip and stops the event propagating.</summary>
        public void SetTooltip(VisualElement element, string value)
        {
            Element = element;
            Tooltip = value ?? "";
            StopPropagation();
        }
        
        /// <inheritdoc />
        protected override void Init()
        {
            base.Init();
            bubbles = true;
            Tooltip = "";
            Element = null;
        }
    }
}
