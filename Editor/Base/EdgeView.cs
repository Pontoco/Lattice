using JetBrains.Annotations;
using Lattice.Base;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lattice.Editor.Views
{
    /// <summary>
    ///     Represents the visual aspect of an edge in the graph. Edges should only know about portViews, not nodes,
    ///     because they are often dangling, connected to false portViews, etc.
    /// </summary>
    public sealed class EdgeView : Edge
    {
        public const string UssClassName = "edge";
        public const string HiddenUssClassName = UssClassName + "--hidden";
        public const string VirtualUssClassName = UssClassName + "--virtual";
        public const string ForcedHoverUssClassName = UssClassName + "--forced-hover";

        private static readonly CustomStyleProperty<string> edgeStyleProperty = new("--edge-style");
        private static readonly CustomStyleProperty<string> curveStyleProperty = new("--curve-style");
        private static readonly CustomStyleProperty<Color> virtualEdgeColorProperty = new("--virtual-edge-color");

        // Null when the edge is being dragged
        [CanBeNull]
        public SerializableEdge SerializedEdge => userData as SerializableEdge;

        private Color virtualEdgeColor = Color.red;
        private bool isHidden;
        private bool isVirtual;
        private EdgeControlWithHiddenEdges edgeControlComplex;

        /// <summary>Whether the edge is tunnelled across the graph. If hidden, only the ends of the edge are visible by default.</summary>
        public bool IsHidden
        {
            get => isHidden;
            set
            {
                if (SerializedEdge != null)
                {
                    SerializedEdge.IsHidden = value;
                }

                if (isHidden == value)
                {
                    return;
                }

                isHidden = value;
                EnableInClassList(HiddenUssClassName, isHidden);
            }
        }

        /// <summary>Whether the edge is practically dangling, it is a fake edge in the resulting graph.</summary>
        public bool IsVirtual
        {
            get => isVirtual;
            set
            {
                if (isVirtual == value)
                {
                    return;
                }

                isVirtual = value;
                EnableInClassList(VirtualUssClassName, isVirtual);
                UpdateEdgeControl();
            }
        }

        public EdgeView()
        {
            styleSheets.Add(
                AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.pontoco.lattice/Editor/UI/EdgeView.uss"));
        }

        /// <inheritdoc />
        protected override EdgeControl CreateEdgeControl() =>
            edgeControlComplex = new EdgeControlWithHiddenEdges
            {
                capRadius = 4f,
                interceptWidth = 6f
            };

        /// <summary>Whether the edge is valid. Ie. whether the edge is well formed and can find its inputs and outputs.</summary>
        public bool IsValid()
        {
            if (input == null || output == null)
            {
                // Debug.LogError("I don't think edges should ever have null inputs or outputs. They should always be connected at least to a false port");
                return false; // it's definitely invalid though.
            }

            // // Edges connected to false portViews have valid nodes, but the port on that node is invalid.
            // if (input is FalsePort || output is FalsePort)
            // {
            //     return false;
            // }

            return true;
        }

        protected override void OnCustomStyleResolved(ICustomStyle styles)
        {
            base.OnCustomStyleResolved(styles);

            // Collect the --edge-style property that defines how the edge is rendered.
            if (styles.TryGetValue(edgeStyleProperty, out string edgeState))
            {
                edgeControlComplex.EdgeStyle = edgeState switch
                {
                    "dashed" => EdgeControlWithHiddenEdges.RenderStyle.Dashed,
                    "hidden" => EdgeControlWithHiddenEdges.RenderStyle.Hidden,
                    _ => EdgeControlWithHiddenEdges.RenderStyle.Visible
                };
            }
            else
            {
                edgeControlComplex.EdgeStyle = EdgeControlWithHiddenEdges.RenderStyle.Visible;
            }
            
            // --curve-style property that defines the edge's curve.
            if (styles.TryGetValue(curveStyleProperty, out string curveStyle))
            {
                edgeControlComplex.CurveStyle = curveStyle switch
                {
                    "straight" => EdgeControlWithHiddenEdges.CurveType.Straight,
                    _ => EdgeControlWithHiddenEdges.CurveType.Default
                };
            }
            else
            {
                edgeControlComplex.CurveStyle = EdgeControlWithHiddenEdges.CurveType.Default;
            }

            if (!styles.TryGetValue(virtualEdgeColorProperty, out virtualEdgeColor))
            {
                virtualEdgeColor = Color.red;
            }

            UpdateEdgeControl();
        }

        /// <inheritdoc />
        public override bool UpdateEdgeControl()
        {
            bool validity = base.UpdateEdgeControl();
            // ReSharper disable once InvertIf
            if (IsVirtual && !selected)
            {
                edgeControlComplex.inputColor = virtualEdgeColor;
                edgeControlComplex.outputColor = virtualEdgeColor;
            }
            return validity;
        }
    }
}
