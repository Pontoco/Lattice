using System.Collections.Generic;
using System.Linq;
using Lattice.StandardLibrary;
using UnityEditor;
using UnityEngine;
using UnityEngine.Pool;

namespace Lattice.Editor.Tools
{
    /// <summary>Implements dragging a Lattice Script from the project window to the inspector to add it to a GameObject.</summary>
    public static class InspectorDragDropHandler
    {
        [InitializeOnLoadMethod]
        public static void HookUpDragAndDropToInspector()
        {
            DragAndDrop.AddDropHandler(OnInspectorDrop);
        }

        private static DragAndDropVisualMode OnInspectorDrop(Object[] targets, bool perform)
        {
            using var __ = CollectionPool<List<LatticeGraph>, LatticeGraph>.Get(out var graphs);
            foreach (var obj in DragAndDrop.objectReferences)
            {
                if (obj is LatticeGraph g)
                {
                    graphs.Add(g);
                }
            }

            if (!graphs.Any())
            {
                return DragAndDropVisualMode.None; // this allows other drag and drop handlers to handle the drag
            }

            if (perform)
            {
                var graphsArr = graphs.ToArray();
                foreach (var target in targets)
                {
                    if (target is GameObject gameObject && gameObject.GetComponent<LatticeExecutorAuthoring>() == null)
                    {
                        Undo.AddComponent<LatticeExecutorAuthoring>(gameObject).Graphs = graphsArr;
                    }
                }
            }

            return DragAndDropVisualMode.Link;
        }
    }
}
