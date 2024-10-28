using System;
using System.Collections.Generic;
using Lattice.Nodes;
using Lattice.Utils;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace Lattice.StandardLibrary
{
    /// <summary>Use this component to run a Lattice Graph on an entity.</summary>
    public class LatticeExecutorAuthoring : MonoBehaviour
    {
        [Tooltip("The set of Lattice Graphs to execute on this entity.")]
        public LatticeGraph[] Graphs;
        public class Baker : Baker<LatticeExecutorAuthoring>
        {
            public override void Bake(LatticeExecutorAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);

                if (authoring.Graphs == null)
                {
                    return;
                }

                LatticeExecutor executor = new()
                {
                    Graphs = new List<LatticeGraph>()
                };

                HashSet<Type> nodeTypesEncountered = new();

                // Bake graphs
                foreach (var g in authoring.Graphs)
                {
                    BakeGraph(g);
                }

                AddComponentObject(entity, executor);

                // Not all entities executing a Lattice graph keep state. We could make this conditional.
                // For now they just store an empty state component.
                AddComponentObject(entity, new LatticeState());

                void BakeGraph(LatticeGraph graph)
                {
                    if (graph.IsMalformed())
                    {
                        Debug.LogError(
                            $"A Lattice Graph was malformed and will not be baked. Graph [{GraphUtils.GetAssetPathRuntime(graph)}]. Entity [{entity}] [{graph.name}] [{authoring.gameObject.name}]",
                            graph);
                        return;
                    }

                    executor.Graphs.Add(graph);

                    DependsOn(graph);

                    // Add all components from ECS components in the graph 
                    // todo(perf): this could be cached to not loop through all nodes
                    HashSet<Type> componentsAdded = new();
                    foreach (var node in graph.nodes)
                    {
                        
                        // This can't be in node.Bake() because it needs to keep track globally which ECS Components have already
                        // been added to the entity.
                        if (node is EcsComponentNode ecsComponentNode)
                        {
                            var componentType = ecsComponentNode.ComponentType.type;
                            
                            // Make sure to request a LocalTransform if the script uses it.
                            if (componentType == typeof(LocalTransform))
                            {
                                GetEntity(TransformUsageFlags.Dynamic);
                            }
                            
                            if (ecsComponentNode.AddDuringBake && !componentsAdded.Contains(componentType))
                            {
                                // todo: Special case the locatransform
                                if (componentType == typeof(LocalTransform))
                                {
                                    AddComponent(GetEntity(TransformUsageFlags.Dynamic), componentType);
                                }
                                else
                                {
                                    AddComponent(GetEntity(TransformUsageFlags.None), componentType);
                                }

                                componentsAdded.Add(componentType);
                            }
                        }
                    }

                    foreach (var node in graph.nodes)
                    {
                        LatticeNode vNode = (LatticeNode)node;
                        if (vNode is not IBakedLatticeNode bakedNode)
                        {
                            continue;
                        }

                        // Call the FirstBakeForGraph on the first node that gets baked for the graph.
                        // Useful for generalized setup for all nodes of a certain type.
                        var nodeType = node.GetType();
                        if (!nodeTypesEncountered.Contains(nodeType))
                        {
                            bakedNode.FirstBakeForType(this);
                            nodeTypesEncountered.Add(nodeType);
                        }

                        // Bake the node. This is just an arbitrary overridable function nodes can implement to do stuff
                        // during bake-time.
                        try
                        {
                            bakedNode.Bake(this, authoring);
                        }
                        catch (Exception e)
                        {
                            Debug.LogException(e);
                        }
                    }
                }
            }
        }
    }
    
    /// <summary>
    ///     Implement this interface on a <see cref="LatticeNode" /> to get callbacks when this node is baked onto an
    ///     entity.
    /// </summary>
    public interface IBakedLatticeNode
    {
        /// <summary>
        ///     Called when the graph is baked. Called once, on the first time this node type is encountered, while baking an
        ///     entity. This is useful if you need to initialize baking data such as Buffers on the baker that should be shared
        ///     among all nodes of this type.
        /// </summary>
        public void FirstBakeForType(IBaker baker) { }

        /// <summary>Called when the VSGraph is baked. Called once for every instance of this node in the graph.</summary>
        public void Bake(IBaker baker, LatticeExecutorAuthoring authoring);
    }
}
