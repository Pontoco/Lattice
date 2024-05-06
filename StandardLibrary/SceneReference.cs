using System;
using System.Collections.Generic;
using Lattice.Base;
using Lattice.StandardLibrary;
using Unity.Entities;
using UnityEngine;

[assembly: RegisterGenericComponentType(typeof(FindEntity.BakeDataBuffer))]

namespace Lattice.StandardLibrary
{
    [NodeCreateMenu("Utils/Find Entity At Bake Time")]
    [Serializable]
    public class FindEntity : BakeDataLatticeNode<Entity, FindEntity>
    {
        public string GameObjectName;

        public override string DefaultName => "Find Entity in Subscene";

        protected override IEnumerable<PortData> GenerateOutputPorts()
        {
            yield return new PortData("PrefabEntity");
        }

        protected override Entity? BakeData(IBaker baker, LatticeExecutorAuthoring authoring)
        {
            // Bake the prefab to entities and store the reference onto the graph's entity.
            GameObject[] rootsInScene = authoring.gameObject.scene.GetRootGameObjects();
            Transform found = null;
            foreach (var root in rootsInScene)
            {
                if (root.name == GameObjectName)
                {
                    found = root.transform;
                    break;
                }

                found = root.transform.Find(GameObjectName);
            }
            
            if (found == null)
            {
                Debug.LogError($"Scene reference [{GameObjectName}] could not be found in Scene [{authoring.gameObject.scene.path}]. [{this}]");
                return null;
            }

            return baker.GetEntity(found, TransformUsageFlags.Dynamic);
        }

    }
}
