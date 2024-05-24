using System;
using Lattice.Editor.Utils;
using UnityEditor;
using UnityEngine.UIElements;

namespace Lattice.Editor
{
    [Serializable]
    public abstract class BaseGraphWindow : EditorWindow
    {
        /// <summary>Called by Unity when the window is enabled / opened</summary>
        protected virtual void CreateGUI()
        {
            rootVisualElement.name = "graphRootView";
            rootVisualElement.styleSheets.Add(
                AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.pontoco.lattice/Editor/UI/BaseGraphView.uss"));
            
            // Add the USS variables that provide icons from the Entities package.
            StyleHelpers.AddCommonEntitiesVariables(rootVisualElement);
        }
    }
}
