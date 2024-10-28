using System.Collections.Generic;
using System.Linq;
using Lattice.Editor.Utils;
using UnityEditor;
using UnityEngine.UIElements;
using EntitiesResources = Unity.Entities.Editor.Resources;

namespace Lattice.Editor.Views
{
    /// <summary>Draws selected and pinned nodes using <see cref="LatticeNodeEditor"/>.</summary>
    internal sealed class LatticeInspector : VisualElement
    {
        private readonly LatticeGraphView graphView;
        private readonly Dictionary<LatticeNodeView, LatticeNodeEditor> viewsToEditors = new();
        private readonly ScrollView scrollView;
        private readonly VisualElement emptyState;

        /// <inheritdoc />
        public override VisualElement contentContainer => scrollView.contentContainer;

        public LatticeInspector(LatticeGraphView graphView)
        {
            this.graphView = graphView;
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.pontoco.lattice/Editor/UI/LatticeInspector.uss");
            StyleHelpers.AddEntitiesStyleTemplate(this, EntitiesResources.Templates.Inspector.InspectorStyle);
            StyleHelpers.AddEntitiesStyleTemplate(this, EntitiesResources.Templates.Inspector.EntityInspector);
            styleSheets.Add(styleSheet);

            scrollView = new ScrollView();
            hierarchy.Add(scrollView);
            scrollView.StretchToParentSize();

            emptyState = new VisualElement();
            emptyState.AddToClassList("empty-state");
            emptyState.Add(new Label("Select a node to view its state."));

            graphView.OnNodeSelectionChanged += Rebuild;
        }

        /// <summary>Re-adds the appropriate node editors based on pinning and selection.</summary>
        public void Rebuild()
        {
            Clear();
            foreach (
                LatticeNodeView nodeView in
                // Pinned editors:
                graphView.NodeViews.Where(v => LatticeNodeEditor.IsPinned(v.Target.FileId))
                         // Selection:
                         .Concat(graphView.selection.OfType<LatticeNodeView>())
                         // Sort by Y and then X position:
                         .OrderBy(v => v.worldBound.y)
                         .ThenBy(v => v.worldBound.x)
            )
            {
                if (!viewsToEditors.TryGetValue(nodeView, out LatticeNodeEditor editor))
                {
                    viewsToEditors.Add(nodeView, editor = new LatticeNodeEditor(nodeView));

                    // Because editors can remove themselves from the hierarchy, we monitor for an empty state here.
                    editor.RegisterCallback<DetachFromPanelEvent, LatticeInspector>(static (_, args) => args.UpdateEmptyState(), this);
                }

                // Because we're appending pinned editors and the selection, make sure to not Add twice.
                if (editor.panel == null)
                {
                    Add(editor);
                }
            }

            UpdateEmptyState();
        }

        private void UpdateEmptyState()
        {
            if (childCount == 0)
            {
                if (emptyState.panel != null)
                {
                    // Nothing changed.
                    return;
                }
                hierarchy.Add(emptyState);
                scrollView.style.display = DisplayStyle.None;
            }
            else
            {
                if (emptyState.panel == null)
                {
                    // Nothing changed.
                    return;
                }
                emptyState?.RemoveFromHierarchy();
                scrollView.style.display = DisplayStyle.Flex;
            }
        }
    }
}
