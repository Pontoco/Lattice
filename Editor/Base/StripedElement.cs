using UnityEngine;
using UnityEngine.UIElements;

namespace Lattice.Editor
{
    /// <summary>A VisualElement with a striped background.</summary>
    internal sealed class StripedElement : VisualElement
    {
        private static readonly CustomStyleProperty<Color> stripeColor = new("--stripe-color");
        private static readonly CustomStyleProperty<float> stripeWidth = new("--stripe-width");
        private static readonly CustomStyleProperty<float> stripeSpacing = new("--stripe-spacing");
        private static readonly CustomStyleProperty<float> stripeAngle = new("--stripe-angle");

        /// <summary>The color used by the stripe. Alternates with transparent.</summary>
        public Color StripeColor { get; set; } = Color.black;
        
        /// <summary>The width of the stripe, alternates with <see cref="StripeSpacing"/>.</summary>
        public float StripeWidth { get; set; } = 10;
        
        /// <summary>The width of the space between stripes, alternates with <see cref="StripeWidth"/>.</summary>
        public float StripeSpacing { get; set; } = 10;
        
        /// <summary>The angle the stripes face in degrees. 0 is straight up, and the direction moves clockwise.</summary>
        public float StripeAngle { get; set; } = 45;

        public StripedElement()
        {
            pickingMode = PickingMode.Ignore;
            RegisterCallback<CustomStyleResolvedEvent>(StyleResolved);
            generateVisualContent += GenerateVisualContent;
            return;

            void StyleResolved(CustomStyleResolvedEvent evt)
            {
                bool modified = false;
                if (evt.customStyle.TryGetValue(stripeColor, out Color color))
                {
                    StripeColor = color;
                    modified = true;
                }

                if (evt.customStyle.TryGetValue(stripeWidth, out float width))
                {
                    StripeWidth = width;
                    modified = true;
                }

                if (evt.customStyle.TryGetValue(stripeSpacing, out float spacing))
                {
                    StripeSpacing = spacing;
                    modified = true;
                }

                if (evt.customStyle.TryGetValue(stripeAngle, out float angle))
                {
                    StripeAngle = angle;
                    modified = true;
                }

                if (modified)
                {
                    MarkDirtyRepaint();
                }
            }

            void GenerateVisualContent(MeshGenerationContext obj)
            {
                Painter2D painter = obj.painter2D;
                painter.strokeColor = StripeColor;
                painter.lineWidth = StripeWidth;
                painter.lineCap = LineCap.Butt;

                float angleDeg = 180 - StripeAngle % 180;
                if (StripeAngle < 0)
                {
                    angleDeg += 180;
                }

                Vector2 size = localBound.size;
                float angleRad = angleDeg * Mathf.Deg2Rad;
                float stripeIncrementUnadjusted = StripeSpacing + StripeWidth;
                (float extraOffset, float offsetLength, float stripeIncrement) = angleDeg switch
                {
                    <= 45f => (
                        size.y * Mathf.Tan(angleRad),
                        size.y / Mathf.Cos(angleRad),
                        stripeIncrementUnadjusted / Mathf.Cos(angleRad)
                    ),
                    >= 135f => (
                        size.y * Mathf.Tan(Mathf.PI - angleRad),
                        size.y / Mathf.Cos(Mathf.PI - angleRad),
                        stripeIncrementUnadjusted / Mathf.Cos(Mathf.PI - angleRad)
                    ),
                    _ => (
                        size.x * Mathf.Tan(Mathf.Abs(angleRad - Mathf.PI * 0.5f)),
                        size.x / Mathf.Cos(Mathf.Abs(angleRad - Mathf.PI * 0.5f)),
                        stripeIncrementUnadjusted / Mathf.Cos(Mathf.Abs(angleRad - Mathf.PI * 0.5f))
                    )
                };

                offsetLength += StripeWidth;

                painter.BeginPath();
                if (angleDeg is <= 45 or >= 135)
                {
                    Vector2 direction = new(-Mathf.Sin(angleRad), -Mathf.Cos(angleRad));
                    Vector2 offsetA = -direction * StripeWidth;
                    Vector2 offsetB = direction * offsetLength;

                    // draws along width
                    float y = angleDeg <= 45 ? size.y : 0;
                    for (var x = 0f; x < size.x + extraOffset + StripeWidth; x += stripeIncrement)
                    {
                        Vector2 position = new(x, y);
                        painter.MoveTo(position + offsetA);
                        painter.LineTo(position + offsetB);
                    }
                }
                else
                {
                    Vector2 direction = new(-Mathf.Sin(angleRad), -Mathf.Cos(angleRad));
                    Vector2 offsetA = -direction * StripeWidth;
                    Vector2 offsetB = direction * offsetLength;

                    // draws along height
                    float yAdjustment = angleDeg < 90 ? 0 : extraOffset;
                    for (var y = 0f; y < size.y + extraOffset + StripeWidth; y += stripeIncrement)
                    {
                        Vector2 position = new(size.x, y - yAdjustment);
                        painter.MoveTo(position + offsetA);
                        painter.LineTo(position + offsetB);
                    }
                }

                painter.Stroke();
            }
        }
    }
}
