using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using Lattice.Editor.Events;
using Lattice.Editor.Manipulators;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lattice.Editor.Views
{
    public partial class LatticeGraphView
    {
        /// <summary>If <paramref name="element"/> produces custom tooltips, show them.</summary>
        public void ShowGraphTooltip(VisualElement element)
        {
            tooltipManipulator.ShowGraphTooltip(element);
        }

        /// <summary>Hide any previously shown tooltips for this element.</summary>
        public void HideGraphTooltip(VisualElement element)
        {
            tooltipManipulator.HideGraphTooltip(element);
        }

        /// <summary>
        ///     Manipulator that handles all custom tooltips in the graph. Implement <see cref="IHasGraphTooltip" />,
        ///     <see cref="IHasGraphTooltip" />, or <see cref="GraphTooltipManipulator"/> to add tooltip support to an element
        /// </summary>
        private sealed class GraphViewTooltipManipulator : Manipulator
        {
            private static readonly MethodInfo SendEventImmediatelyMethod =
                typeof(VisualElement).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic).First(t => t.Name == "SendEvent" && t.GetParameters().Length == 2);

            /// <summary>Helper class for finding tooltips using either <see cref="IHasGraphTooltip"/> or <see cref="GraphTooltipEvent"/>.</summary>
            private sealed class TooltipProvider : IEquatable<TooltipProvider>
            {
                public string Tooltip { get; private set; } = "";
                
                public VisualElement Element { get; private set; }
                
                public void Update(TooltipProvider other)
                {
                    Tooltip = other.Tooltip;
                    Element = other.Element;
                }

                public void Update<T>(T target) where T : VisualElement, IHasGraphTooltip
                {
                    Element = target;
                    Tooltip = target?.Tooltip ?? "";
                }

                public void Update(VisualElement target)
                {
                    if (Element == target)
                    {
                        // Target didn't change.
                        return;
                    }

                    // Ignore if null, or when hovering icon badges (errors, warnings, messages, etc. attached to elements).
                    if (target is null or IconBadge)
                    {
                        Element = null;
                        return;
                    }

                    IHasGraphTooltip newGraphTooltipElement = target as IHasGraphTooltip ?? target.GetFirstAncestorOfType<IHasGraphTooltip>();
                    if (newGraphTooltipElement == null)
                    {
                        using GraphTooltipEvent evt = GraphTooltipEvent.GetPooled();
                        evt.target = target;
                        SendEventImmediatelyMethod.Invoke(target, new object[] { evt, 2 });
                        Element = evt.Element;
                        Tooltip = evt.Tooltip;
                        return;
                    }

                    if (Element == newGraphTooltipElement)
                    {
                        return;
                    }

                    Element = (VisualElement)newGraphTooltipElement;
                    Tooltip = newGraphTooltipElement.Tooltip;
                }

                /// <inheritdoc />
                public bool Equals(TooltipProvider other)
                {
                    if (ReferenceEquals(null, other))
                    {
                        return false;
                    }
                    if (ReferenceEquals(this, other))
                    {
                        return true;
                    }
                    return Equals(Element, other.Element) && Tooltip == other.Tooltip;
                }
            }

            private LatticeGraphView graphView;
            private PortTooltipState portTooltipsState;
            private GraphElement currentlyHoveredElement;
            private BaseNodeView currentlyHoveredNode;
            private BaseNodeView currentSoloPortTooltipNode;
            private readonly TooltipProvider lastTooltipProvider = new();
            private readonly TooltipProvider currentTooltipProvider = new();
            private readonly TooltipProvider tempTooltipProvider = new();
            private readonly Dictionary<VisualElement, GraphTooltipView> tooltips = new();

            /// <inheritdoc />
            protected override void RegisterCallbacksOnTarget()
            {
                graphView = (LatticeGraphView)target;
                target.RegisterCallback<PointerOverEvent>(OnPointerOverEvent);
                target.RegisterCallback<PointerOutEvent>(OnPointerOutEvent);
                target.RegisterCallback<KeyDownEvent>(OnKeyDownShortcut);
                target.RegisterCallback<KeyUpEvent>(OnKeyUpShortcut);
            }

            /// <inheritdoc />
            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<PointerOverEvent>(OnPointerOverEvent);
                target.UnregisterCallback<PointerOutEvent>(OnPointerOutEvent);
                target.UnregisterCallback<KeyDownEvent>(OnKeyDownShortcut);
                target.UnregisterCallback<KeyUpEvent>(OnKeyUpShortcut);
            }

            /// <summary>Pointer has entered any element.</summary>
            private void OnPointerOverEvent(PointerOverEvent evt)
            {
                // Update the currently hovered element.
                GraphElement prevHoveredElement = currentlyHoveredElement;
                if (evt.target is GraphElement element)
                {
                    currentlyHoveredElement = element;
                }
                else
                {
                    currentlyHoveredElement = (evt.target as VisualElement)?.GetFirstAncestorOfType<GraphElement>();
                }
                currentlyHoveredNode = currentlyHoveredElement as BaseNodeView ?? (currentlyHoveredElement as PortView)?.Owner;

                UpdateAllPortTooltipsFromModifiers(evt.modifiers);

                // If all tooltips are enabled, and we newly hovered a node, bring its tooltips to the front.
                if (portTooltipsState == PortTooltipState.All && currentlyHoveredNode != prevHoveredElement)
                {
                    currentlyHoveredNode?.Query<PortView>().ForEach(port =>
                    {
                        if (tooltips.TryGetValue(port, out GraphTooltipView view))
                        {
                            view.BringToFront();
                        }
                    });
                }
                else if (portTooltipsState == PortTooltipState.None)
                {
                    UpdateHoveredTooltip(evt.target as VisualElement);
                }
            }

            /// <summary>Pointer has exited any element.</summary>
            private void OnPointerOutEvent(PointerOutEvent evt)
            {
                currentlyHoveredElement = null;
                currentlyHoveredNode = null;
                UpdateAllPortTooltipsFromModifiers(evt.modifiers);
            }

            private void OnKeyDownShortcut(KeyDownEvent evt)
            {
                UpdateAllPortTooltipsFromModifiers(evt.modifiers);
            }

            private void OnKeyUpShortcut(KeyUpEvent evt)
            {
                UpdateAllPortTooltipsFromModifiers(evt.modifiers);
            }

            public void ShowGraphTooltip(VisualElement element)
            {
                tempTooltipProvider.Update(element);
                ShowSingleTooltip(tempTooltipProvider, GraphTooltipEventSource.ShowGraphTooltipCall);
            }

            public void HideGraphTooltip(VisualElement element)
            {
                tempTooltipProvider.Update(element);
                HideSingleTooltip(tempTooltipProvider, false);
            }

            private void UpdateAllPortTooltipsFromModifiers(EventModifiers modifiers)
            {
                bool holdingCtrl = (modifiers & EventModifiers.Control) != 0;
                bool holdingShift = (modifiers & EventModifiers.Shift) != 0;
                if (holdingCtrl)
                {
                    UpdateAllPortTooltips(!holdingShift ? PortTooltipState.Solo : PortTooltipState.All);
                }
                else if (!holdingShift)
                {
                    UpdateAllPortTooltips(PortTooltipState.None);
                }
            }

            private void UpdateHoveredTooltip([CanBeNull] VisualElement target)
            {
                lastTooltipProvider.Update(currentTooltipProvider);
                currentTooltipProvider.Update(target);
                if (currentTooltipProvider.Equals(lastTooltipProvider))
                {
                    // Provider didn't change.
                    return;
                }

                HideSingleTooltip(lastTooltipProvider, false);
                ShowSingleTooltip(currentTooltipProvider, GraphTooltipEventSource.PointerEvent);
            }

            private void HideSingleTooltip(TooltipProvider tooltipProvider, bool forceHide)
            {
                if (tooltipProvider.Element == null || !tooltips.TryGetValue(tooltipProvider.Element, out GraphTooltipView view))
                {
                    return;
                }

                view.Hide(forceHide);
            }

            private void ShowSingleTooltip(TooltipProvider tooltipProvider, GraphTooltipEventSource evtSource)
            {
                VisualElement source = tooltipProvider.Element;
                if (source == null)
                {
                    return;
                }

                GraphTooltipView tooltipElement;
                if (tooltipProvider.Element is IHasGraphTooltip customTooltipProvider)
                {
                    // Provider creates a modified tooltip, use its custom logic.
                    if (!tooltips.TryGetValue(source, out tooltipElement))
                    {
                        tooltips.Add(source, tooltipElement = customTooltipProvider.CreateTooltipView());
                        graphView.AddElement(tooltipElement);
                        source.RegisterCallbackOnce<DetachFromPanelEvent>(OnProviderDetachFromPanel);
                    }
                    else
                    {
                        tooltipElement.visible = true;
                    }
                    tooltipElement.UpdateTooltip(evtSource);
                    customTooltipProvider.PositionTooltip(tooltipElement);
                }
                else
                {
                    // Provider is default, use the basic logic.
                    if (!tooltips.TryGetValue(source, out tooltipElement))
                    {
                        tooltips.Add(source, tooltipElement = new GraphTooltipView(tooltipProvider.Tooltip));
                        graphView.AddElement(tooltipElement);
                        source.RegisterCallbackOnce<DetachFromPanelEvent>(OnProviderDetachFromPanel);
                    }
                    else
                    {
                        tooltipElement.visible = true;
                    }

                    tooltipElement.UpdateTooltip(evtSource);

                    // Position the tooltip based on the location of the owner.
                    tooltipElement.SetPosition(tooltipProvider.Element);
                }
            }

            private void SetStateForTooltipsUnderRoot<TTarget>(VisualElement root, bool show)
                where TTarget : VisualElement, IHasGraphTooltip
            {
                root?.Query<TTarget>().ForEach(e =>
                {
                    if (show)
                    {
                        tempTooltipProvider.Update(e);
                        ShowSingleTooltip(tempTooltipProvider, GraphTooltipEventSource.ForceShow);
                    }
                    else
                    {
                        tempTooltipProvider.Update(e);
                        HideSingleTooltip(tempTooltipProvider, true);
                    }
                });
            }

            private void UpdateAllPortTooltips(PortTooltipState state)
            {
                if (state == PortTooltipState.Solo)
                {
                    if (portTooltipsState == PortTooltipState.All)
                    {
                        SetStateForTooltipsUnderRoot<PortView>(target, false);
                    }

                    portTooltipsState = currentlyHoveredNode != null ? PortTooltipState.Solo : PortTooltipState.None;
                    if (currentlyHoveredNode == currentSoloPortTooltipNode)
                    {
                        return;
                    }

                    // Reset the previous node.
                    SetStateForTooltipsUnderRoot<PortView>(currentSoloPortTooltipNode, false);
                    // Show tooltips for the newly hovered node.
                    SetStateForTooltipsUnderRoot<PortView>(currentlyHoveredNode, true);
                    // Update state
                    currentSoloPortTooltipNode = currentlyHoveredNode;
                    return;
                }

                if (state == portTooltipsState)
                {
                    return;
                }

                portTooltipsState = state;
                currentSoloPortTooltipNode = null;

                switch (state)
                {
                    case PortTooltipState.None:
                        SetStateForTooltipsUnderRoot<PortView>(target, false);
                        break;
                    case PortTooltipState.All:
                        SetStateForTooltipsUnderRoot<PortView>(target, true);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(state), state, null);
                }
            }

            private void OnProviderDetachFromPanel(DetachFromPanelEvent evt)
            {
                VisualElement provider = (VisualElement)evt.target;
                if (tooltips.Remove(provider, out GraphTooltipView tooltipView))
                {
                    graphView.RemoveElement(tooltipView);
                }
            }
        }
    }
}
