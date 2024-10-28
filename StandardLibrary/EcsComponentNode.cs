using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Lattice.Base;
using Lattice.IR;
using Lattice.IR.Nodes;
using Lattice.StandardLibrary;
using Lattice.Utils;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Pool;

namespace Lattice.Nodes
{
    [Serializable]
    public struct EcsNodeTemplate : INodeTemplate
    {
        public Type ComponentType;

        public BaseNode Build()
        {
            return new EcsComponentNode
            {
                ComponentType = new SerializableType(ComponentType),
            };
        }

        /// <summary>The type of the node to create from this template.</summary>
        public Type NodeType => typeof(EcsComponentNode);
    }

    // Node doesn't directly appear in the menu, see EcsComponentNode.AvailableTemplates for node menu generation.
    /// <summary>Lets you drive a field on an ECS component.</summary>
    [Serializable]
    public class EcsComponentNode : LatticeNode
    {
        private const string PortComponentInput = "componentData";
        private const string PortEntityInput = "entity";
        
        [HideInInspector]
        public SerializableType ComponentType;

        [Setting("Add Component During Bake")]
        public bool AddDuringBake;

        private FieldInfo[] GetFields() => ComponentType.type.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                                   BindingFlags.NonPublic);

        /// <summary>
        ///     Reads from this component will always happen *after* this system runs. May be null, in which case reads happen
        ///     as soon as possible (ie. in the first execution pass of graph logic).
        /// </summary>
        [HideInInspector]
        public SerializableType SystemView;

        public override string DefaultName => ComponentType.type?.Name ?? "Component Missing";

        protected override IEnumerable<PortData> GenerateInputPorts()
        {
            if (ComponentType.type == null)
            {
                yield break; 
            }
            
            var componentFields = GetFields();
            
            // Entity port to control what entity you're reading/writing from. 
            yield return new PortData(PortEntityInput, optional: true)
            {
                acceptMultipleEdges = false,
                defaultType = typeof(Entity),
            };

            // Zero sized types can't be set to anything, so no input port.
            int visibleFields = componentFields.Count(f => f.GetCustomAttribute<LatticeReadOnlyAttribute>() == null);
            if (!TypeManager.GetTypeInfo(TypeManager.GetTypeIndex(ComponentType.type)).IsZeroSized && visibleFields > 1)
            {
                yield return new PortData(PortComponentInput, optional:true)
                {
                    acceptMultipleEdges = false,
                    defaultType = ComponentType.type
                };
            }

            // Every field on the component also gets an input port so you can set them individually.
            foreach (var field in componentFields)
            {
                if (field.GetCustomAttribute<LatticeReadOnlyAttribute>() != null)
                {
                    continue;
                }
                
                // I think this is true? If this trips, we can update this code.
                Assert.IsFalse(field.FieldType.IsNullable(), "IComponentData fields cannot be nullable.");
                
                yield return new PortData(field.Name + "_in",optional:true)
                {
                    acceptMultipleEdges = false,
                    
                    // All inputs have type nullable, so that you can conditionally set them with a value. 
                    defaultType = typeof(Nullable<>).MakeGenericType(field.FieldType),
                };
            }

            // Expose a state ref!
            // todo. :)
            // yield return new PortData("state", optional: true)
            // {
            //     acceptMultipleEdges = true,
            //     type = ComponentType.type,
            //     customTooltip = "Ref: "+ComponentType.type.Name,
            //     sizeInPixel = 6,
            //     secondaryPort = true,
            // };
        }

        protected override IEnumerable<PortData> GenerateOutputPorts()
        {
            if (ComponentType.type == null)
            {
                yield break; 
            }
            
            var componentFields = GetFields();

            // Zero sized types can't be set to anything, so no main port.
            int visibleFields = componentFields.Length;
            if (!TypeManager.GetTypeInfo(TypeManager.GetTypeIndex(ComponentType.type)).IsZeroSized && visibleFields > 1)
            {
                yield return new PortData("value_out")
                {
                    acceptMultipleEdges = true,
                    defaultType = ComponentType.type
                };
            }

            // Every field on the component also gets an port so you can read from them individually.
            foreach (var field in componentFields)
            {
                yield return new PortData(field.Name + "_out")
                {
                    acceptMultipleEdges = true,
                    defaultType = field.FieldType,
                };
            }
        }

        // Called via reflection. We use this extra helper method because EntityManager.SetComponentData has
        // other overloads, which makes it hard to access via reflection.
        private static void SetComponent<T>(EntityManager manager, Entity entity, T componentData)
            where T : unmanaged, IComponentData
        {
            manager.SetComponentData(entity, componentData);
        }

        // Called via reflection. We use this extra helper method because EntityManager.GetComponentData has
        // other overloads, which makes it hard to access via reflection.
        public static T GetComponent<T>(EntityManager manager, Entity entity)
            where T : unmanaged, IComponentData
        {
            return manager.GetComponentData<T>(entity);
        }

        // Compilation

        public override void CompileToIR(GraphCompilation compilation)
        {
            if (ComponentType.type == null)
            {
                throw new Exception($"Cannot find Component type: [{ComponentType.serializedType}]");
            }
            // todo: We need to figure out how to handle state refs for ECSComponentNode. :) 
            // Spawns several IR Nodes:
            //  - the write stage (taking inputs and writing to components)
            //      - one node to write the full component
            //      - one node for each field, to write that field to ECS
            //  - the read stage (reading from same components, at a potentially different phase)
            //      - one node to read the entire component
            //      - field accessors for each field on the component

            // Add a passthrough node if we have an entity input, so that both read and write can access it.
            IRNode entityInput;
            if (GetPort(PortEntityInput).GetEdges().Any())
            {
                entityInput = compilation.AddNode(this, "EntityInput", CoreIRNodes.Identity(typeof(Entity)));
                compilation.MapInputPort(this, PortEntityInput, entityInput, "value");
            }
            else
            {
                // Use the default entity node if none is passed.
                entityInput = compilation.GetImplicitEntity(Graph);
                compilation.MapInputPort(this, PortEntityInput, null);
            }

            // Write Stage:
            // Collects all of the inputs to the node and writes them into the ECS component.
            NodePort writeComponentPort = GetPort("componentData");
            using var _ = CollectionPool<List<IRNode>, IRNode >.Get(out var writers);
            if (writeComponentPort != null && writeComponentPort.GetEdges().Count > 0)
            {
                var writeFull = compilation.AddNode(this, "ECSWrite",
                    FunctionIRNode.FromStaticMethod<EcsComponentNode>(nameof(SetComponent), ComponentType.type));

                writeFull.AddInput("entity", entityInput);
                writers.Add(writeFull);

                compilation.MapInputPort(this, "componentData", writeFull);
            }
            else
            {
                compilation.MapInputPort(this, "componentData", null);
            }

            // Add a writer for every connected field.
            var componentFields = GetFields();
            foreach (var field in componentFields)
            {
                string port = field.Name + "_in";
                if (GetPort(port).GetEdges().Any())
                {
                    var writeField = compilation.AddNode(this, $"WriteField_{field.Name}", new WriteIComponentNode(field));
                    writeField.AddInput("entity", entityInput);
                    writers.Add(writeField);

                    compilation.MapInputPort(this, port, writeField, "value");
                }
                else
                {
                    compilation.MapInputPort(this, port, null);
                }
            }

            // Read Stage:
            // This reads back the value from the ECS Component, optionally at a specific later phase.
            // This lets a normal ECS System run in between the Write and Read.
            // It lets you specify the 'time' when this node views the underlying component data.

            var read = compilation.AddNode(this, "ECSRead",
                FunctionIRNode.FromStaticMethod<EcsComponentNode>(nameof(GetComponent), ComponentType.type));

            read.SystemPhase = SystemView?.type;

            read.AddInput("entity", entityInput);
            
            foreach (var writer in writers)
            {
                read.AddInput(IRNode.BarrierPort, writer);
            }

            // An edge is spawned between the two stages, but the value is unused. It's just important to order the side effects
            // of the two phases.

            compilation.SetPrimaryNode(this, read);
            compilation.Mappings[this].OutputPortMap["value_out"] = compilation.GetNodeRef(read);

            // Every field on the component also gets a port so you can read from them individually.
            foreach (var field in componentFields)
            {
                var fieldNode = compilation.AddFieldAccessor(this, read, field.Name, field.Name + "_out");

                if (field.FieldType == typeof(Entity))
                {
                    if (field.GetCustomAttribute<EntityNotNullAttribute>() != null)
                    {
                        // Add an assert node to catch the error early, if it ever becomes null.
                        var assertNonNull = compilation.AddNode(this,
                            FunctionIRNode.FromStaticMethod<EcsComponentNode>(nameof(AssertNonNull)));
                        assertNonNull.AddInput("e", fieldNode);
                        compilation.MapOutputPort(this, field.Name + "_out", assertNonNull);
                    }
                    else
                    {
                        // The Entity is potentially null. So lift it to nullability:
                        var liftEntity = compilation.AddNode(this,
                            FunctionIRNode.FromStaticMethod<EcsComponentNode>(nameof(LiftEntity)));
                        liftEntity.CheckExceptions = false;
                        liftEntity.AddInput("entity", fieldNode);
                        compilation.MapOutputPort(this, field.Name + "_out", liftEntity);
                    }
                }
            }
        }

        public static Entity? LiftEntity(Entity entity)
        {
            if (entity == Entity.Null)
            {
                return null;
            }

            return entity;
        }

        public static Entity AssertNonNull(Entity e)
        {
            if (e == Entity.Null)
            {
                throw new Exception("Found Entity.Null on a field with [EntityNotNull] attribute.");
            }
            return e;
        }

        // todo: Add special node creation options for the 'drop edge' node menu, just for fields, because we know the type.

        /// <summary>The set of pre-made node templates that should be exposed to the node creation menus.</summary>
        [AddToNodeMenu]
        public static IEnumerable<NodeTemplateMenuItem> AvailableTemplates()
        {
            // Exposes all public fields on ECS components to the node creation menu.
            // todo: We could *fairly* easily supported nested fields.

            foreach (var componentType in TypeManager.GetAllTypes())
            {
                if (componentType.Type == null 
                    || !typeof(IComponentData).IsAssignableFrom(componentType.Type)
                    || TypeManager.IsManagedType(componentType.TypeIndex))
                {
                    continue;
                }
                

                yield return new NodeTemplateMenuItem
                {
                    MenuPath = $"ECS Components/{componentType.Type.Namespace}/{componentType.Type.Name}",
                    Template = new EcsNodeTemplate
                    {
                        ComponentType = componentType.Type
                    }
                };
            }
        }
    }
}
