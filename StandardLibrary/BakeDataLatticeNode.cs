using System;
using Lattice.IR;
using Lattice.Nodes;
using Unity.Collections;
using Unity.Entities;

namespace Lattice.StandardLibrary
{
    /// <summary>Implements logic for a node that bakes data to the entity, and looks it up at runtime when it evaluates.</summary>
    /// <typeparam name="TTypeOwner">A necessary type parameter so that this baker adds a unique buffer per derived class. Otherwise children of the same value type will conflict during baking.</typeparam>
    // ReSharper disable once UnusedTypeParameter
    public abstract class BakeDataLatticeNode<T, TTypeOwner> : LatticeNode, IBakedLatticeNode where T : unmanaged
    {
        /// <summary>
        ///     Implement this to define the data that gets baked to the entity. This node will return this value upon
        ///     execution. Returns null to bake no data.
        /// </summary>
        protected abstract T? BakeData(IBaker baker, LatticeExecutorAuthoring authoring);

        public override void CompileToIR(GraphCompilation compilation)
        {
            IRNode node = compilation.AddNode(this,
                FunctionIRNode.FromStaticMethod<BakeDataLatticeNode<T, TTypeOwner>>(nameof(GetBakedData)));
            node.AddInput("entity", compilation.GetImplicitEntity(Graph));
            compilation.SetPrimaryNode(this, node);
        }

        private static T GetBakedData(LatticeNode node, EntityManager em, Entity entity)
        {
            // Yank the value out of the storage buffer!
            var buf = em.GetBuffer<BakeDataBuffer>(entity);
            foreach (BakeDataBuffer bakedRef in buf)
            {
                if (bakedRef.Guid == new FixedString64Bytes(node.GUID))
                {
                    return bakedRef.Entity;
                }
            }

            throw new Exception($"Couldn't find baked data for node [{node.GUID}] on entity [{entity}].");
        }

        public void FirstBakeForType(IBaker baker)
        {
            baker.AddBuffer<BakeDataBuffer>(baker.GetEntity(TransformUsageFlags.None));
        }

        public void Bake(IBaker baker, LatticeExecutorAuthoring authoring)
        {
            var data = BakeData(baker, authoring);
            if (data.HasValue)
            {
                // Bake the user data into the buffer, associated with this specific node instance.
                baker.AppendToBuffer(baker.GetEntity(TransformUsageFlags.None), new BakeDataBuffer
                {
                    Guid = GUID,
                    Entity = data.Value,
                });
            }
        }

        /// <summary>A buffer to store the data in.</summary>
        public struct BakeDataBuffer : IBufferElementData
        {
            // todo(perf): Use Hash128 instead for performance.
            public FixedString64Bytes Guid; // The guid of the specific node instance within the graph.
            public T Entity; // The output entity of the prefab we baked.
        }
    }
}
