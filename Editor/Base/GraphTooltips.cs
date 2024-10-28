using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lattice.Editor.Views
{
    /// <summary>
    ///     Enabling tooltips to be shown when hovering that element, with custom modifications and positioning logic.
    ///     See <see cref="LatticeGraphView.GraphViewTooltipManipulator" /> for implementation.
    /// </summary>
    internal interface IHasGraphTooltip
    {
        /// <summary>The text shown on the tooltip. Similar to <see cref="VisualElement.tooltip" />.</summary>
        public string Tooltip { get; }
        
        /// <summary>Create an instance of the view and add any required styles or modifications.</summary>
        public GraphTooltipView CreateTooltipView();
        
        /// <summary>Position <see cref="tooltipElement"/> based on this element.</summary>
        public void PositionTooltip(GraphTooltipView tooltipElement);
    }

    /// <summary>The reason that caused a <see cref="GraphTooltipView"/> to be shown.</summary>
    internal enum GraphTooltipEventSource
    {
        PointerEvent,
        ShowGraphTooltipCall,
        ForceShow,
        RedirectNode
    }

    /// <summary>The default view implementation for tooltips, which just sets the text of the label.</summary>
    internal class GraphTooltipView : GraphElement
    {
        /// <summary>The position of the tooltip. E.g. if Top is used, the tooltip should appear above the source element.</summary>
        public enum Position
        {
            Top,
            Bottom,
            Left,
            Right
        }
        
        public const string UssClassName = "tooltip-view";
        public const string ContainerUssClassName = "tooltip-view__container";
        public const string LabelUssClassName = UssClassName + "__label";
        public const string ForceEnabledClassName = UssClassName + "--enabled-by-force";

        private static readonly CustomStyleProperty<Color> fillColorStyle = new("--fill-color");
        private static readonly CustomStyleProperty<Color> outlineColorStyle = new("--outline-color");
        private static readonly CustomStyleProperty<Color> chevronColorStyle = new("--chevron-color");
        private static readonly CustomStyleProperty<string> chevronDirectionStyle = new("--chevron-direction");

        /// <summary>The direction the tooltip's chevron points.</summary>
        protected enum ChevronDirection
        {
            None,
            Left,
            Right
        }

        protected ChevronDirection Direction
        {
            get => direction;
            set
            {
                if (direction == value)
                {
                    return;
                }
                direction = value;
                MarkDirtyRepaint();
            }
        }

        /// <summary>The text shown on the tooltip label.</summary>
        protected string Text
        {
            get => label?.text;
            set
            {
                if (label == null)
                {
                    label = new Label(value)
                    {
                        pickingMode = PickingMode.Ignore
                    };
                    label.AddToClassList(LabelUssClassName);
                    container.Add(label);
                }
                else
                {
                    label.text = value;
                }
            }
        }

        /// <summary>If the view is currently forcibly enabled.</summary>
        protected bool IsForciblyVisible { get; private set; }

        private readonly VisualElement container;
        private Label label;
        private ChevronDirection direction;
        private Color fillColor = Color.black;
        private Color outlineColor = new(1, 1, 1, 0.2f);
        private Color chevronColor = new(1, 1, 1, 0.5f);

        private readonly IHasGraphTooltip source;
        private new readonly string tooltip;

        public GraphTooltipView(string tooltip)
        {
            this.tooltip = tooltip;
            layer = 100;

            styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.pontoco.lattice/Editor/UI/TooltipView.uss"));

            AddToClassList(UssClassName);
            pickingMode = PickingMode.Ignore;

            Add(container = new VisualElement
            {
                pickingMode = PickingMode.Ignore
            });
            container.AddToClassList(ContainerUssClassName);
            container.generateVisualContent += GenerateVisualContent;

            // Collect custom styles.
            RegisterCallback<CustomStyleResolvedEvent, GraphTooltipView>(static (evt, args) =>
            {
                if (evt.customStyle.TryGetValue(chevronDirectionStyle, out string anchorDirection))
                {
                    // Dirty logic is separated for Direction.
                    args.Direction = anchorDirection switch
                    {
                        "left" => ChevronDirection.Left,
                        "right" => ChevronDirection.Right,
                        _ => ChevronDirection.None
                    };
                }
                else
                {
                    // Reset to default if no style was found.
                    args.Direction = ChevronDirection.None;
                }

                bool dirty = evt.customStyle.TryGetValue(fillColorStyle, out args.fillColor) |
                             evt.customStyle.TryGetValue(outlineColorStyle, out args.outlineColor) |
                             evt.customStyle.TryGetValue(chevronColorStyle, out args.chevronColor);

                if (dirty)
                {
                    args.MarkDirtyRepaint();
                }
            }, this);
        }

        public GraphTooltipView(IHasGraphTooltip source) : this("")
        {
            this.source = source;
        }

        public virtual void UpdateTooltip(GraphTooltipEventSource evtSource)
        {
            Text = source?.Tooltip ?? tooltip;
            IsForciblyVisible = evtSource is GraphTooltipEventSource.ForceShow or GraphTooltipEventSource.RedirectNode;
            EnableInClassList(ForceEnabledClassName, IsForciblyVisible);
            EnableInClassList("hidden", Text == "");
        }

        private void GenerateVisualContent(MeshGenerationContext obj)
        {
            Rect rect = contentRect;

            Painter2D painter2D = obj.painter2D;
            painter2D.lineWidth = 1;

            // Draw background (chevron end when specified).
            painter2D.fillColor = fillColor;
            painter2D.strokeColor = outlineColor;
            painter2D.BeginPath();
            painter2D.MoveTo(Vector2.zero);
            painter2D.LineTo(new Vector2(rect.xMax, 0));
            if (direction == ChevronDirection.Right)
            {
                painter2D.LineTo(new Vector2(rect.xMax + rect.center.y, rect.center.y));
            }
            painter2D.LineTo(rect.max);
            painter2D.LineTo(new Vector2(0, rect.yMax));
            if (direction == ChevronDirection.Left)
            {
                painter2D.LineTo(new Vector2(-rect.center.y, rect.center.y));
            }
            painter2D.ClosePath();
            painter2D.Fill();
            painter2D.Stroke();

            // Draw a stronger directional stroke on the chevron end.
            painter2D.strokeColor = chevronColor;
            switch (direction)
            {
                case ChevronDirection.Right:
                    painter2D.BeginPath();
                    painter2D.LineTo(new Vector2(rect.xMax, 0));
                    if (direction == ChevronDirection.Right)
                    {
                        painter2D.LineTo(new Vector2(rect.xMax + rect.center.y, rect.center.y));
                    }
                    painter2D.LineTo(rect.max);
                    painter2D.Stroke();
                    break;
                case ChevronDirection.Left:
                    painter2D.BeginPath();
                    painter2D.LineTo(new Vector2(0, rect.yMax));
                    painter2D.LineTo(new Vector2(-rect.center.y, rect.center.y));
                    painter2D.LineTo(Vector2.zero);
                    painter2D.Stroke();
                    break;
                case ChevronDirection.None:
                default:
                    break;
            }
        }

        /// <summary>Default behaviour for positioning the tooltip view.</summary>
        public void SetPosition(VisualElement owner, Position position = Position.Top)
        {
            // Position the tooltip based on the port location.
            IStyle tooltipStyle = style;
            Rect rect = owner.contentRect;
            float x = position switch
            {
                Position.Top or Position.Bottom => rect.center.x,
                Position.Right => rect.max.x,
                _ => rect.min.x
            };
            
            float y = position switch
            {
                Position.Left or Position.Right => rect.center.y,
                Position.Bottom => rect.max.y,
                _ => rect.min.y
            };
            
            Vector2 p = owner.ChangeCoordinatesTo(parent, new Vector2(x, y));
            tooltipStyle.left = p.x;
            tooltipStyle.top = p.y;
        }

        /// <summary>Tries to hide the tooltip.</summary>
        public void Hide(bool forceHide)
        {
            if (IsForciblyVisible && !forceHide)
            {
                return;
            }

            if (!visible)
            {
                // This may occur recursively, so check that the tooltip isn't already hidden.
                return;
            }

            IsForciblyVisible = false;
            visible = false;
            OnHide();
        }

        protected virtual void OnHide() { }
    }
}
