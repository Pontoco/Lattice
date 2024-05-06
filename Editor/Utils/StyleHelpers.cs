using System;
using System.Reflection;
using Unity.Entities.Editor;
using UnityEditor;
using UnityEngine.UIElements;
using EntitiesResources = Unity.Entities.Editor.Resources;

namespace Lattice.Editor.Utils
{
    internal static class StyleHelpers
    {
        /// <summary>
        /// Adds the common dark mode USS variables (mostly icons) from the Entities package.
        /// See com.unity.entities/Editor Default Resources/uss/Common/variables.uss and variables_dark.uss.
        /// </summary>
        /// <param name="rootElement">The root uss element to add the StyleSheets to.</param>
        public static void AddCommonEntitiesVariables(VisualElement rootElement)
        {
            // Adds the common variables (mostly icons) from the Entities package.
            // See Common/variables.uss, variables_dark.uss, variables_light.uss
            AddEntitiesStyleTemplate(rootElement, EntitiesResources.Templates.Variables);
            rootElement.AddToClassList("variables");
        }

        /// <summary>
        /// Adds specific USS variables (mostly icons) from the Entities package.<br/>
        /// Forces the dark mode version of the style sheet.
        /// </summary>
        /// <param name="rootElement">The root uss element to add the StyleSheets to.</param>
        /// <param name="template">The template that contains the styles to add.</param>
        public static void AddEntitiesStyleTemplate(VisualElement rootElement, VisualElementTemplate template)
        {
            // Dark mode requires no extra style-loading logic.
            if (EditorGUIUtility.isProSkin)
            {
                template.AddStyles(rootElement);
                return;
            }
            
            // For light mode, we load the elements as usual,
            // but we compare to see if a light-mode style was added,
            // which we then swap out for the dark mode version.
            template.AddStyles(rootElement);

            VisualElementStyleSheetSet styleSheets = rootElement.styleSheets;
            for (var i = styleSheets.count - 1; i >= 0; i--)
            {
                StyleSheet styleSheet = styleSheets[i];
                if (!styleSheet.name.EndsWith("_light", StringComparison.Ordinal))
                    continue;
                string assetPath = AssetDatabase.GetAssetPath(styleSheet);
                StyleSheet darkStyleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>($"{assetPath[..^"_light.uss".Length]}_dark.uss");
                // Failed to load a dark mode version of the light StyleSheet.
                if (darkStyleSheet == null)
                    continue;

                // Swap the light sheet for the dark.
                styleSheets.Remove(styleSheet);
                styleSheets.Add(darkStyleSheet);
            }
        }
    }
}
