using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using GrEmit;
using Lattice.Editor.Views;
using Lattice.IR;
using Unity.Entities;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace Lattice.Editor.Tools
{
    public static class MenuItems
    {
        /// <summary>Finds all graphs in the project and recompiles them. Useful for checking globally for compilation errors.</summary>
        [MenuItem("Lattice/Tools/Recompile All Graphs")]
        public static void RecompileAll()
        {
            var paths = AssetDatabase.FindAssets($"t:{nameof(LatticeGraph)}");
            List<LatticeGraph> graphs = new();
            foreach (var path in paths)
            {
                LatticeGraph graph =
                    AssetDatabase.LoadAssetAtPath<LatticeGraph>(AssetDatabase.GUIDToAssetPath(path));
                graphs.Add(graph);
            }

            Stopwatch timer = Stopwatch.StartNew();
            try
            {
                GraphCompiler.CompileStandalone(graphs);
            }
            catch (Exception)
            {
                Debug.LogError("ICE: Graph compilation threw fatal error.");
                throw;
            }
            timer.Stop();

            Debug.Log(
                $"(Lattice) Compiled all lattice graphs in project. ({timer.ElapsedMilliseconds}ms) [{graphs.Count} graphs]:\n" +
                string.Join("\n", graphs));
        }

        [MenuItem("Lattice/Tools/View GraphViz")]
        public static void ProjectWideGraphViz()
        {
            var paths = AssetDatabase.FindAssets($"t:{nameof(LatticeGraph)}");
            List<LatticeGraph> graphs = new();
            foreach (var path in paths)
            {
                LatticeGraph graph =
                    AssetDatabase.LoadAssetAtPath<LatticeGraph>(AssetDatabase.GUIDToAssetPath(path));
                graphs.Add(graph);
            }

            var compilation = GraphCompiler.CompileStandalone(graphs);
            string dotString = GraphCompilation.ToDot(compilation);

            var p = FileUtil.GetUniqueTempPathInProject() + ".dot";
            File.WriteAllText(p, dotString);
            LatticeGraphToolbar.OpenGraphviz(dotString);
        }

        [MenuItem("Lattice/Tools/Save Debug Assembly")]
        public static void SaveAssembly()
        {
            var paths = AssetDatabase.FindAssets($"t:{nameof(LatticeGraph)}");
            List<LatticeGraph> graphs = new();
            foreach (var path in paths)
            {
                graphs.Add(AssetDatabase.LoadAssetAtPath<LatticeGraph>(AssetDatabase.GUIDToAssetPath(path)));
            }

            var graph = GraphCompiler.CompileStandalone(graphs, AssemblyBuilderAccess.RunAndSave);

            TypeBuilder typeBuilder = graph.CodeGenModule.DefineType("LatticeStaticFunctions",
                TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed | TypeAttributes.Abstract);

            foreach (var phase in graph.GetAllPhases())
            {
                MethodBuilder method = typeBuilder.DefineMethod("LatticePhase_" + phase.Name,
                    MethodAttributes.Public | MethodAttributes.Static, CallingConventions.Any, typeof(void),
                    new[] { typeof(IRExecution), typeof(EntityManager) });

                GroboIL emit = new(method);

                HashSet<IRNode> nodesInPhase = graph.GetNodesInPhase(phase).ToHashSet();
                if (!ILGeneration.EmitNodeExecutionIL(emit, graph, nodesInPhase))
                {
                    Debug.LogError("Cannot save assembly. IL generation failed.");
                    return;
                }

                Debug.Log(
                    $"(Lattice) Compiled phase: [{phase.Name}] [{nodesInPhase.Count} nodes]:\n{string.Join("\n", nodesInPhase)}");
                Debug.Log(emit);
            }

            typeBuilder.CreateType();

            var assemblyFileName = graph.CodeGenAssembly.GetName().Name + ".dll";
            graph.CodeGenAssembly.Save(assemblyFileName);
            string destPath = Path.GetFullPath(Path.Combine("Library", "ScriptAssemblies", assemblyFileName));
            File.Delete(destPath);
            File.Move(assemblyFileName, destPath);
            Debug.Log($"Wrote to [{destPath}]");
        }

        /// <summary>If true, nodes will execute faster, but their values will not be visible in the Lattice Window.</summary>
        private static bool disableDebug;
        public const string DisableDebugMenu = "Lattice/Options/Disable Debug";

        [MenuItem(DisableDebugMenu)]
        private static void PerformAction()
        {
            disableDebug = !disableDebug;
            EditorPrefs.SetBool("LATTICE_DISABLE_DEBUG", !disableDebug);
        }

        [MenuItem(DisableDebugMenu, true)]
        private static bool PerformActionValidation()
        {
            var isChecked = EditorPrefs.GetBool("LATTICE_DISABLE_DEBUG", true);
            Menu.SetChecked(DisableDebugMenu, isChecked);
            return true;
        }
    }
}
