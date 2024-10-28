using System;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lattice.Editor.Views
{
    /// <summary>A label and background that can be attached to a port.</summary>
    internal sealed class PortTagView : VisualElement
    {
        public const string UssClassName = "port-tag";
        public const string ContainerUssClassName = UssClassName + "__container";
        public const string LabelUssClassName = UssClassName + "__label";
        
        /// <summary>The text displayed on the tag's label.</summary>
        public string Text
        {
            get => label.text;
            set => label.text = value;
        }

        private readonly VisualElement container;
        private readonly Label label;
        private readonly PortView portView;
        private readonly Attacher attacher;

        /// <summary>Creates and attaches a tag (a label with a background) to the provided port.</summary>
        /// <param name="portView">The port to attach to.</param>
        /// <param name="text">The text displayed on the tag's label.</param>
        public PortTagView(PortView portView, string text)
        {
            pickingMode = PickingMode.Ignore;
            
            styleSheets.Add(
                AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.pontoco.lattice/Editor/UI/PortTagView.uss"));

            container = new VisualElement { pickingMode = PickingMode.Ignore };
            container.AddToClassList(ContainerUssClassName);
            
            this.portView = portView;
            AddToClassList(UssClassName);
            AddToClassList($"{UssClassName}--{portView.Location.ToString().ToLowerInvariant()}");
            
            label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(LabelUssClassName);
            container.Add(label);
            
            hierarchy.Add(container);

            if (portView.Location is PortViewLocation.Top or PortViewLocation.Bottom or PortViewLocation.BottomLeft)
            {
                portView.hierarchy.Insert(0, this);
            }
            else
            {
                // Ports on the left or right of a node are inside its container.
                // This means that the port tag cannot be made a child of the port view, because it'll be clipped.
                // In this case, we parent this element to the root of the container, and use an Attacher to position it.
                // To look as if it's still parented under the port view, we draw (see GenerateVisualContent) a
                // circular end cap with a hole in it that shows the port view through this element.
                
                BaseNodeView node = portView.Owner!;

                // Find the last plain container, and insert this tag view after it.
                // E.g. if there's an IconBadge or some specialized element, we want to be underneath it.
                int index = 0;
                for (; index < node.hierarchy.childCount; index++)
                {
                    if (node.hierarchy[index].GetType() != typeof(VisualElement))
                        break;
                }
                node.hierarchy.Insert(index, this);
                
                SpriteAlignment alignment = portView.Location switch
                {
                    PortViewLocation.Left or PortViewLocation.State => SpriteAlignment.LeftCenter,
                    PortViewLocation.Right => SpriteAlignment.RightCenter,
                    _ => throw new NotImplementedException()
                };
                attacher = new Attacher(this, portView, alignment)
                {
                    distance = 0
                };
                
                container.generateVisualContent += GenerateVisualContent;
            }
        }

        private void GenerateVisualContent(MeshGenerationContext obj)
        {
            SpriteAlignment alignment = portView.Location switch
            {
                PortViewLocation.Left or PortViewLocation.State => SpriteAlignment.LeftCenter,
                PortViewLocation.Right => SpriteAlignment.RightCenter,
                _ => SpriteAlignment.Custom
            };

            if (alignment == SpriteAlignment.Custom)
            {
                return;
            }

            Painter2D painter = obj.painter2D;
            painter.fillColor = container.resolvedStyle.backgroundColor;
            painter.strokeColor = container.resolvedStyle.borderTopColor;
            float borderWidth = container.resolvedStyle.borderTopWidth;
            painter.lineWidth = borderWidth;

            Rect rect = layout;
            rect.x = 0;
            rect.y = 0;

            VisualElement connector = portView.Q(PortView.ConnectorName);
            Rect connectorRect = connector.ChangeCoordinatesTo(container, connector.contentRect);
            Vector2 connectorCenter = connectorRect.center;
            float radius = rect.height * 0.5f;
            float innerRadius = radius - 2;

            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (alignment)
            {
                case SpriteAlignment.LeftCenter:
                {
                    PaintBorder();
                    painter.ClosePath();
                    painter.MoveTo(connectorCenter - new Vector2(0, innerRadius));
                    painter.Arc(
                        connectorCenter,
                        innerRadius,
                        Angle.Turns(0),
                        Angle.Turns(1)
                    );
                    painter.ClosePath();
                    painter.Fill(FillRule.OddEven);
                    PaintBorder();
                    painter.Stroke();

                    break;

                    void PaintBorder()
                    {
                        painter.BeginPath();
                        painter.MoveTo(new Vector2(rect.xMax, 0));
                        painter.Arc(
                            connectorCenter,
                            radius,
                            Angle.Turns(-0.25f),
                            Angle.Turns(0.25f)
                        );
                        painter.LineTo(new Vector2(rect.xMax, rect.yMax));
                    }
                }
                case SpriteAlignment.RightCenter:
                {
                    PaintBorder();
                    painter.ClosePath();
                    painter.MoveTo(connectorCenter - new Vector2(0, innerRadius));
                    painter.Arc(
                        connectorCenter,
                        innerRadius,
                        Angle.Turns(0),
                        Angle.Turns(1)
                    );
                    painter.ClosePath();
                    painter.Fill(FillRule.OddEven);
                    PaintBorder();
                    painter.Stroke();

                    break;

                    void PaintBorder()
                    {
                        painter.BeginPath();
                        painter.MoveTo(new Vector2(0, rect.yMax));
                        painter.Arc(
                            connectorCenter,
                            radius,
                            Angle.Turns(0.25f),
                            Angle.Turns(0.75f)
                        );
                        painter.LineTo(new Vector2(0, 0));
                    }
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
