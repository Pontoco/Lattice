using System;
using System.Collections.Generic;
using System.Linq;
using Lattice.Base;
using Lattice.IR;
using Unity.Assertions;
using Unity.Collections;
using Unity.Entities;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Pool;
#if UNITY_EDITOR
using UnityEditor;
#endif

// ReSharper disable PartialTypeWithSinglePart

namespace Lattice
{
    /// <summary>Attach this component to an entity and it will execute this LatticeGraph for the entity.</summary>
    public class LatticeExecutor : IComponentData
    {
        public List<LatticeGraph> Graphs;
    }

    /// <summary>
    ///     Marks a singleton entity that stores any global state in the lattice graph. (State that is not associated with
    ///     any specific entity).
    /// </summary>
    public struct LatticeGlobalState : IComponentData { }

    /// <summary>
    ///     Lattice setup for the frame. This should run at the start of the gameplay frame, after any new entities have
    ///     been created for the frame. It must run before all <see cref="LatticePhaseSystem" />.
    /// </summary>
#if LATTICE_CUSTOM_PHASES
    [DisableAutoCreation]
#else
    [UpdateInGroup(typeof(SimulationSystemGroup))]
#endif
    public partial class LatticeBeginSystem : SystemBase
    {
        /// <summary>Holds the state and values while executing. (In the future state may move to be stored on entities directly).</summary>
        public IRExecution SharedContext;

        // Marks if Lattice is currently executing. (Ie. if false, phases executing should be an error)
        public bool CurrentlyExecuting;

        // The set of phases than ran this frame. Used for error checking.
        public HashSet<Type> RunPhasesThisFrame = new();

        public long UpdateCount; // Used for tests.
        
        // Configurations for the compiler used during this session. Null to use the default values pulled from prefs.
        public bool? EnableDebug;

        private bool InDebug()
        {
            if (EnableDebug.HasValue)
            {
                return EnableDebug.Value;
            }
            
#if UNITY_EDITOR
            return !EditorPrefs.GetBool("LATTICE_DISABLE_DEBUG", true);
#else
            return false;
#endif
        }

        protected override void OnCreate()
        {
            SharedContext = new IRExecution(GraphCompiler.RecompileIfNeeded(), InDebug());
            GraphCompiler.OnGraphCompilation += OnRecompile;
            ExecutionHistory.Add(SharedContext);

            var globalState = EntityManager.CreateSingleton<LatticeGlobalState>();
            EntityManager.AddComponentObject(globalState, new LatticeState());
        }

        /// <summary>Update the executor with the latest graph if we've recompiled.</summary>
        private void OnRecompile(GraphCompilation globalGraph)
        {
            SharedContext = new IRExecution(globalGraph, InDebug());
            ExecutionHistory.Add(SharedContext);
        }

        protected override void OnUpdate()
        {
            // Detect if new graphs are present in any executors, and add them to the compilation.
            using (CollectionPool<HashSet<LatticeGraph>, LatticeGraph>.Get(
                       out HashSet<LatticeGraph> graphsInCompilation))
            {
                foreach (var executor in SystemAPI.Query<LatticeExecutor>())
                {
                    foreach (var g in executor.Graphs)
                    {
                        graphsInCompilation.Add(g);
                    }
                }

                // Add missing graphs to compilation.
                foreach (var g in graphsInCompilation)
                {
                    if (!GraphCompiler.GlobalGraph.SourceGraphs.Contains(g))
                    {
                        GraphCompiler.AddToCompilation(g);
                    }
                }
            }

            // Recompile Lattice graphs, if necessary.
            // Update the graph in place. State remains on the entities.
            var compilation = GraphCompiler.RecompileIfNeeded();
            if (compilation != SharedContext.Graph)
            {
                SharedContext = new IRExecution(compilation, InDebug());
                ExecutionHistory.Add(SharedContext);
            }

            if (SharedContext.Graph.CannotBeExecuted)
            {
                Debug.LogWarning("Lattice graph had fatal compilation errors, and could not be executed.");
                return;
            }

            // Clear per-frame debugging information.
            bool debug = InDebug();

            if (SharedContext.DebugData != null)
            {
                if (debug)
                {
                    SharedContext.DebugData.Values.Clear();
                    SharedContext.DebugData.NodesRunThisFrame.Clear();
                }
                else
                {
                    SharedContext.DebugData = null;
                }
            }
            else
            {
                if (debug)
                {
                    SharedContext.DebugData = new();
                }
            }

            // Categorize entities into buckets for each lattice graph.
            // This tells execution which entities each node should execute for. 
            // ===============================================================
            SharedContext.EntitiesByQualifier.Clear();
            SharedContext.EntityToLatticeIndex.Clear();
            SharedContext.StateDict.Clear();
            foreach (LatticeGraph graph in SharedContext.Graph.SourceGraphs)
            {
                EntityIRNode entityNode = SharedContext.Graph.GetImplicitEntity(graph);
                Qualifier qualifier = entityNode.QualifierId;

                SharedContext.EntitiesByQualifier[qualifier] = new NativeList<Entity>(128, Allocator.Temp);
                SharedContext.EntityToLatticeIndex[qualifier] = new NativeHashMap<Entity, int>(128, Allocator.Temp);
            }

            // Add global state to the execution inputs.
            var globalLatticeEntity = SystemAPI.GetSingletonEntity<LatticeGlobalState>();
            LatticeState globalState = EntityManager.GetComponentObject<LatticeState>(globalLatticeEntity);
            SharedContext.StateDict.Add(Entity.Null, globalState);

            foreach (var (executor, entity) in SystemAPI.Query<LatticeExecutor>().WithEntityAccess())
            {
                foreach (var g in executor.Graphs)
                {
                    Qualifier graphQualifier = SharedContext.Graph.GetImplicitEntity(g).QualifierId;

                    NativeList<Entity> entityList = SharedContext.EntitiesByQualifier[graphQualifier];
                    entityList.Add(entity);

                    SharedContext.EntityToLatticeIndex[graphQualifier].Add(entity, entityList.Length - 1);

                    // Pull the state for this entity into our state dictionary.
                    if (EntityManager.HasComponent<LatticeState>(entity))
                    {
                        LatticeState state = EntityManager.GetComponentObject<LatticeState>(entity);
                        SharedContext.StateDict[entity] = state;
                    }
                }
            }

            CurrentlyExecuting = true; // Share the execution SharedContext for phases to begin executing.
            RunPhasesThisFrame.Clear();

            // (Graph execution is done in follow-up LatticePhaseSystem passes.)
        }

        protected override void OnDestroy()
        {
            GraphCompiler.OnGraphCompilation -= OnRecompile;
        }
    }

    /// <summary>
    ///     The base class for all Lattice Phase systems. These systems execute a portion of the Lattice graph at a point
    ///     during the frame. Each node in the Lattice Graph is tagged by one of these phases.
    /// </summary>
    /// <remarks>
    ///     These phases must be placed in the frame between <see cref="LatticeBeginSystem" /> and
    ///     <see cref="LatticeEndSystem" />.
    /// </remarks>
    /// <remarks>
    ///     Phases must be on the same schedule as all other lattice systems. You can't have a Lattice Phase in Fixed
    ///     *and* Render. (yet)
    /// </remarks>
    /// <seealso cref="Metadata.ExecutionPhase" />
    public abstract partial class LatticePhaseSystem : SystemBase, ILatticePhaseSystem
    {
        protected override void OnUpdate()
        {
            LatticeBeginSystem lattice = World.GetExistingSystemManaged<LatticeBeginSystem>();

            // Don't update if the lattice system is disabled.
            if (!lattice.Enabled)
            {
                return;
            }

            if (lattice.SharedContext.Graph.CannotBeExecuted)
            {
                return;
            }

            if (!lattice.CurrentlyExecuting)
            {
                Debug.LogError(
                    $"A LatticePhaseSystem tried to execute, but Lattice has already finished for the frame. (or has not started). ({GetType().Name}");
                return;
            }

            lattice.SharedContext.ExecutePhase(EntityManager, GetType());
            lattice.RunPhasesThisFrame.Add(GetType());

            World.GetExistingSystemManaged<LatticeDebugUpdateSystem>().LatticeExecutedThisFrame = true;
        }
    }

#if !LATTICE_CUSTOM_PHASES
    /// <summary>
    ///     The default execution phase that all nodes run in, unless otherwise specified. This runs near the start of the
    ///     SimulationSystemGroup.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(LatticeBeginSystem))]
    [LatticeDefaultPhase]
    public partial class LatticeDefaultPhase : LatticePhaseSystem { }
#endif

    /// <summary>Marks that Lattice has finished executing for this gameplay frame.</summary>
#if LATTICE_CUSTOM_PHASES
    [DisableAutoCreation]
#else
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
#endif
    public partial class LatticeEndSystem : SystemBase
    {
        protected override void OnUpdate()

        {
            LatticeBeginSystem lattice = World.GetExistingSystemManaged<LatticeBeginSystem>();
            if (!lattice.Enabled)
            {
                return;
            }

            Assert.IsTrue(lattice.CurrentlyExecuting || lattice.SharedContext.Graph.CannotBeExecuted);
            lattice.CurrentlyExecuting = false; // Mark that the frame has ended.

            if (lattice.SharedContext.Graph.CannotBeExecuted)
            {
                return;
            }

            // Cleanup scratch values used during graph execution.
            lattice.SharedContext.ClearScratch();

            // Sanity check. Verify entities are not destroyed during the lattice phase systems.
            foreach (NativeList<Entity> entities in lattice.SharedContext.EntitiesByQualifier.Values)
            {
                foreach (var e in entities)
                {
                    if (!EntityManager.Exists(e) || !EntityManager.HasComponent<LatticeExecutor>(e))
                    {
                        Debug.LogError(
                            $"A lattice entity was destroyed before all phases finished. [{EntityManager.GetName(e)}] Entities with " +
                            $"LatticeExecutor cannot be destroyed until after LatticeEndSystem.");
                    }
                }
            }

            // This is a some-what slow debugging check, but worth doing. It verifies that all nodes that should have
            // been executed, were actually executed at least once this frame. This makes sure the user's phase setup
            // is all correct. This could be made a lot faster, though.
            if (lattice.SharedContext.DebugData != null)
            {
                GraphCompilation graph = lattice.SharedContext.Graph;
                foreach (IRNode n in graph.Nodes)
                {
                    Qualifier? q = graph.CompileNode(n).Qualifier;
                    bool shouldHaveRun = !q.HasValue || lattice.SharedContext.EntitiesByQualifier[q.Value].Length > 0;
                    if (shouldHaveRun && !lattice.SharedContext.DebugData.NodesRunThisFrame.Contains(n))
                    {
                        Debug.LogError($"ICE: Node did not run. [{n}]");
                    }
                }
            }

            lattice.UpdateCount++;

            // Sanity checking to make sure all phases ran. Needs editor for TypeCache.
#if UNITY_EDITOR
            List<Type> allPhases = TypeCache.GetTypesDerivedFrom<LatticePhaseSystem>().ToList();
            foreach (Type p in allPhases)
            {
                if (!lattice.RunPhasesThisFrame.Contains(p))
                {
                    Debug.LogError(
                        $"Lattice Phase did not run. [{p.Name}]. Lattice phases cannot be skipped, and must run between LatticeBeginSystem and LatticeEndSystem. " +
                        $"Expected phases: [{string.Join(",", allPhases.Select(p => p.Name))}]");
                }
            }
#endif
        }
    }

    /// <summary>Updates the debug visuals of any open graphs, only if the lattice graph was executed this frame.</summary>
    [UpdateInGroup(typeof(PresentationSystemGroup), OrderLast = true)]
    public partial class LatticeDebugUpdateSystem : SystemBase
    {
        public delegate void LatticeExecuteEvent(IRExecution execution);

        public static event LatticeExecuteEvent OnLatticeExecute;

        public bool LatticeExecutedThisFrame;

        private static ProfilerMarker profileLatticeCallback = new("OnLatticeExecute");

        protected override void OnUpdate()
        {
            if (LatticeExecutedThisFrame)
            {
                LatticeExecutedThisFrame = false;
                LatticeBeginSystem lattice = World.GetExistingSystemManaged<LatticeBeginSystem>();
                using (profileLatticeCallback.Auto())
                {
                    OnLatticeExecute?.Invoke(lattice.SharedContext);
                }
            }
        }
    }
}
