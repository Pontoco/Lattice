using System;
using System.Collections.Generic;
using Lattice.Base;
using Lattice.StandardLibrary;
using Unity.Entities;
using UnityEngine;

[assembly: RegisterGenericComponentType(typeof(PrefabReference.BakeDataBuffer))]

namespace Lattice.StandardLibrary
{
    /// <summary>
    ///     Allows selecting a GameObject prefab, and returns an entity reference to the prefab, suitable for
    ///     instantiating at runtime.
    /// </summary>
    /// <remarks>
    ///     This is somewhat tricky to implement, because the Entity reference to a prefab depends on the subscene it was
    ///     baked in! During bake of this graph, we store the entity reference into the Entity containing the graph. This
    ///     allows the entity reference to be properly remapped.
    /// </remarks>
    /// <remarks>Then, when executing this node, we pull the remapped Entity reference out of the dynamic buffer.</remarks>
    [Serializable]
    [NodeCreateMenu("Lattice/Utility/Prefab Reference")]
    public class PrefabReference : BakeDataLatticeNode<Entity, PrefabReference>
    {
        [HideLabel]
        public GameObject Prefab;

        protected override IEnumerable<PortData> GenerateOutputPorts()
        {
            yield return new PortData("prefabEntity");
        }

        protected override Entity? BakeData(IBaker baker, LatticeExecutorAuthoring authoring)
        {
            baker.DependsOn(Prefab);
            return baker.GetEntity(Prefab, TransformUsageFlags.Dynamic);
        }
    }
}
