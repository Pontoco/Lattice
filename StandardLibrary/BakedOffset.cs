using System.Collections.Generic;
using Lattice.Base;
using Lattice.IR;
using Lattice.StandardLibrary;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[assembly: RegisterGenericComponentType(typeof(ChildWorldLocation.BakeDataBuffer))]

namespace Lattice.StandardLibrary
{
    // todo(john): The matrix math here could definitely be optimized to use only the precise values it needs.

    /// <summary>
    ///     Bakes a relative offset for a child object. This offset is calculated at bake time and is relative to the
    ///     graph entity. The child object may no longer even exist at runtime!
    /// </summary>
    /// <returns>
    ///     The final transform the child object, relative to the provided input root transform. If no root is provided as
    ///     input, multiplies the baked offset with the LocalTransform of this entity.
    /// </returns>
    [NodeCreateMenu("Utils/Child World Location")]
    public class ChildWorldLocation : BakeDataLatticeNode<float4x4, ChildWorldLocation>
    {
        public struct ChildWorldLocationData
        {
            public float4x4 Data;
        }
        
        public string GameObjectName;

        protected override IEnumerable<PortData> GenerateInputPorts()
        {
            yield return new PortData("RootTransform", optional:true);
        }
        
        protected override IEnumerable<PortData> GenerateOutputPorts()
        {
            yield return new PortData("WorldLocation");
        }

        protected override float4x4? BakeData(IBaker baker, LatticeExecutorAuthoring authoring)
        {
            Transform rootTransform = authoring.transform;
            Transform targetTransform = rootTransform.Find(GameObjectName);
            if (targetTransform == null)
            {
                // Don't bake an offset if we can't find the child object.
                return null;
            }

            // childw = rootw * childrel
            // rootw-1 * childw = childrel
            Matrix4x4 relative = rootTransform.worldToLocalMatrix * targetTransform.localToWorldMatrix;

            // Bake the prefab to entities and store the reference onto the graph's entity.
            // Todo: TransformUsageFlags.Dynamic is not necessary if an input is provided on port 1.
            return relative;
        }

        public override void CompileToIR(GraphCompilation compilation)
        {
            base.CompileToIR(compilation);
            
            var bakedData = compilation.Mappings[this].Nodes[0];

            var getChildLocation = compilation.AddNode(this, FunctionIRNode.FromStaticMethod<ChildWorldLocation>(nameof(GetChildLocation)));
            getChildLocation.AddInput("offset", bakedData);
            getChildLocation.AddInput("entity", compilation.GetImplicitEntity(Graph));
            compilation.MapInputPort(this, "RootTransform", getChildLocation, "rootTransform");
            compilation.MapOutputPort(this, "WorldLocation", getChildLocation);
            compilation.SetPrimaryNode(this, getChildLocation);
        }

        private static LocalTransform GetChildLocation(EntityManager em, Entity entity, LocalTransform? rootTransform, float4x4 offset)
        {
            // If the user has specified a base transform, use that to calculate final position.
            // Otherwise just pull the transform off of this entity.
            LocalTransform root = rootTransform ?? em.GetComponentData<LocalTransform>(entity);

            // Get the baked relative transform.
            float4x4 localToWorld = math.mul(root.ToMatrix(), offset);

            return new LocalTransform
            {
                Position = localToWorld.TransformPoint(0),
                Rotation = localToWorld.TransformRotation(quaternion.identity),
                Scale = 1f
            };
        }
    }
}
