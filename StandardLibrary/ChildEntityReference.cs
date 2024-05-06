using System;
using System.Collections.Generic;
using Lattice.Base;
using Lattice.IR;
using Lattice.StandardLibrary;
using Unity.Entities;

[assembly: RegisterGenericComponentType(typeof(ChildEntityReference.BakeDataBuffer))]

namespace Lattice.StandardLibrary
{
    /// <summary>
    ///     Returns an entity reference to the child with the given Gameobject name. The entity reference is baked at
    ///     compile-time.
    /// </summary>
    [NodeCreateMenu("Utils/Child Entity Reference")]
    [Serializable]
    public class ChildEntityReference : BakeDataLatticeNode<Entity, ChildEntityReference>
    {
        public string GameObjectName;

        protected override IEnumerable<PortData> GenerateInputPorts()
        {
            // Adds/removes the Disabled component.
            yield return new PortData("enabled", optional: true, defaultType: typeof(bool));
        }

        protected override IEnumerable<PortData> GenerateOutputPorts()
        {
            yield return new PortData("prefabEntity");
        }

        protected override Entity? BakeData(IBaker baker, LatticeExecutorAuthoring authoring)
        {
            return baker.GetEntity(authoring.transform.Find(GameObjectName), TransformUsageFlags.Dynamic);
        }

        public override void CompileToIR(GraphCompilation compilation)
        {
            base.CompileToIR(compilation);

            var bakedEntity = compilation.Mappings[this].Nodes[0];
            compilation.MapOutputPort(this, "prefabEntity", bakedEntity);

            // Add enabling / disabling.
            if (GetPort("enabled").GetEdges().Count > 0)
            {
                var enableNode = compilation.AddNode(this,
                    FunctionIRNode.FromStaticMethod<ChildEntityReference>(nameof(SetEnabled)));
                enableNode.AddInput("entity", bakedEntity);
                compilation.MapInputPort(this, "enabled", enableNode, "enabled");
            }
            else
            {
                compilation.MapInputPort(this, "enabled", null);
            }
        }

        public static void SetEnabled(EntityManager em, Entity entity, bool enabled)
        {
            em.SetEnabled(entity, enabled);
        }
    }
}
