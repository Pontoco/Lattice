using Lattice.Base;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities.Serialization;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Lattice.Editor.BuildPlayer
{
    /// <summary>
    ///     Stores the AssetDatabase GUIDs of all Lattice Graphs for reference in the build. This is important so that we
    ///     can look up baked data per-graph at runtime.
    /// </summary>
    public class BuildHooks : BuildPlayerProcessor, IPostprocessBuildWithReport
    {
        // This must use BuildPlayerProcessor, not IPreprocessBuildWithReport, as BuildPlayerProcessor is run before
        // and this must run *before* the entities player processor that builds content archives.
        
        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            var guids = AssetDatabase.FindAssets($"t:{typeof(BaseGraph).FullName}");
            var graphs = new Object[guids.Length];
            for (int i = 0; i < guids.Length; i++)
            {
                string guid = guids[i];
                graphs[i] = AssetDatabase.LoadAssetAtPath<BaseGraph>(AssetDatabase.GUIDToAssetPath(guid));
            }

            var gids = new GlobalObjectId[graphs.Length];
            GlobalObjectId.GetGlobalObjectIdsSlow(graphs, gids);

            for (int i = 0; i < graphs.Length; i++)
            {
                BaseGraph baseGraph = ((BaseGraph)graphs[i]);
                baseGraph.runtimeAssetGuid =
                    UnsafeUtility.As<GlobalObjectId, RuntimeGlobalObjectId>(ref gids[i]);
                EditorUtility.SetDirty(baseGraph);
            }

            // This doesn't seem necessary:
            // AssetDatabase.SaveAssets();

            Debug.Log($"(Lattice) Built guids for [{graphs.Length}] Lattice Graphs.");
        }

        // MinValue seems to run before the entities player processor, but 0 and -100 do not.
        public override int callbackOrder => int.MinValue;

        // Override for PostprocessBuild as well, so it runs sooner.
        int IOrderedCallback.callbackOrder => int.MinValue;

        public void OnPostprocessBuild(BuildReport report)
        {
            var guids = AssetDatabase.FindAssets($"t:{typeof(BaseGraph).FullName}");
            foreach (string guid in guids)
            {
                BaseGraph graph = AssetDatabase.LoadAssetAtPath<BaseGraph>(AssetDatabase.GUIDToAssetPath(guid));
                graph.runtimeAssetGuid = new RuntimeGlobalObjectId();
                EditorUtility.SetDirty(graph);
            }
            
            AssetDatabase.SaveAssets();

            Debug.Log($"(Lattice) Reset guids for [{guids.Length}] Lattice Graphs.");
        }
    }
}
