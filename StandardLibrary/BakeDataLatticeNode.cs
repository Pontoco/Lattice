using System;
using Lattice.IR;
using Lattice.Nodes;
using Unity.Entities;

namespace Lattice.StandardLibrary
{
    /// <summary>Implements logic for a node that bakes data to the entity, and looks it up at runtime when it evaluates.</summary>
    /// <typeparam name="TTypeOwner">A necessary type parameter so that this baker adds a unique buffer per derived class. Otherwise children of the same value type will conflict during baking.</typeparam>
    /// <typeparam name="T">The type of the baked data that will be stored on the entity.</typeparam>
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
            var buf = em.GetBuffer<BakeDataBuffer>(entity, true);
            foreach (BakeDataBuffer bakedRef in buf)
            {
                if (bakedRef.Guid == node.HashGuid())
                {
                    return bakedRef.Data;
                }
            }

            throw new Exception($"Couldn't find baked data for node [{node.HashGuid()}] on entity [{entity}].");
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
                    Guid = HashGuid(),
                    Data = data.Value,
                });
            }
        }

        /// <summary>A buffer to store the data in.</summary>
        public struct BakeDataBuffer : IBufferElementData
        {
            public Hash128 Guid; // The guid of the specific node instance within the graph.
            public T Data; // The data we baked onto the entity.
        }
    }
}
