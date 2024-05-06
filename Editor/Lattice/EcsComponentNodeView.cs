using Lattice.Base;
using Lattice.Editor.SearchProviders;
using Lattice.IR;
using Lattice.Nodes;
using Lattice.Utils;
using Unity.Entities;
using Unity.Entities.Editor;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lattice.Editor.Views
{
    [NodeCustomEditor(typeof(EcsComponentNode))]
    public class EcsComponentNodeView : LatticeNodeView
    {
        private Button selectSystem;

        protected override void CreateNodeInspector()
        {
            base.CreateNodeInspector();
            
            styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.pon.lattice/Editor/UI/EcsComponentNodeView.uss"));

            // var bakeComponent = new Toggle("Add Component to Entity");
            // bakeComponent.tooltip = "Add this component during bake?";
            // bakeComponent.BindProperty(FindSerializedProperty(nameof(EcsComponentNode.AddDuringBake)));
            // controlsContainer.Add(bakeComponent);
        }

        /// <inheritdoc />
        public override void Initialize(BaseGraphView owner, BaseNode node)
        {
            base.Initialize(owner, node);
            UpdateVisuals();
        }

        /// <inheritdoc />
        protected override VisualElement CreateSettingsView()
        {
            VisualElement root = new();
            root.Add(base.CreateSettingsView());
            
            root.Add(new Label("Execute After System:"));
            selectSystem = new Button(() =>
            {
                AllSystemsSearchProvider search = AllSystemsSearchProvider.Instance;
                search.Filter = type => typeof(LatticePhaseSystem).IsAssignableFrom(type.BaseType);
                search.Callback = system =>
                {
                    if (system == LatticePhases.GetLatticeDefaultPhase())
                    {
                        ((EcsComponentNode)NodeTarget).SystemView = new SerializableType(null);
                    }
                    else
                    {
                        ((EcsComponentNode)NodeTarget).SystemView = new SerializableType(system);
                    }

                    UpdateVisuals();
                    GraphCompiler.RecompileIfNeeded(true);
                };
                SearchWindow.Open(
                    new SearchWindowContext(GUIUtility.GUIToScreenPoint(Event.current.mousePosition),
                        800),
                    search);
            });
            root.Add(selectSystem);
            
            return root;
        }

        public void UpdateVisuals()
        {
            EcsComponentNode node = (EcsComponentNode)NodeTarget;
            if (node.SystemView.IsMissing())
            {
                selectSystem.text = $"System Missing ({node.SystemView.serializedType})";
            }
            else if (node.SystemView.type == null)
            {
                selectSystem.text = "Immediate";
            }
            else
            {
                selectSystem.text = $"{node.SystemView.type.Name.Replace("LatticePhase", "")}";
            }

            // Update the title icon with the component type.
            TypeIndex type = node.ComponentType.type == null ? TypeIndex.Null : TypeManager.GetTypeIndex(node.ComponentType.type);
            string componentClass;
            if (type.IsChunkComponent)
                componentClass = UssClasses.Inspector.ComponentTypes.ChunkComponent;
            else if (type.IsBuffer)
                componentClass = UssClasses.Inspector.ComponentTypes.BufferComponent;
            else if (type.IsSharedComponentType)
                componentClass = UssClasses.Inspector.ComponentTypes.SharedComponent;
            else if (type.IsManagedComponent)
                componentClass = UssClasses.Inspector.ComponentTypes.ManagedComponent;
            else if (type.IsComponentType)
                componentClass = type.IsZeroSized 
                    ? UssClasses.Inspector.ComponentTypes.Tag 
                    : UssClasses.Inspector.ComponentTypes.Component;
            else
                componentClass = "component--unknown";
            
            SetTitleIcon(componentClass);
        }
    }
}
