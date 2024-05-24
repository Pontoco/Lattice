using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lattice.Editor
{
    /// <summary>
    /// The window view at Edit/Preferences/Lattice.
    /// See <see cref="LatticePreferences"/> for the preferences object.
    /// </summary>
    internal sealed class LatticePreferencesProvider : SettingsProvider
    {
        private const string UssClassName = "lattice-preferences-window";
        private const string TitleUssClassName = UssClassName + "__title";
        private const string SettingsUssClassName = UssClassName + "__settings";

        /// <inheritdoc />
        public LatticePreferencesProvider(string path, SettingsScope scopes, IEnumerable<string> keywords = null) : base(path, scopes, keywords) { }
        
        [SettingsProvider]
        public static SettingsProvider CreateProvider()
            => new LatticePreferencesProvider("Preferences/Lattice", SettingsScope.User)
            {
                activateHandler = Activate
            };

        private static void Activate(string searchContext, VisualElement rootElement)
        {
            var preferences = LatticePreferences.instance;
            // The ScriptableSingleton<T> is not directly editable by default.
            // Change the hideFlags to make the SerializedObject editable.
            preferences.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;

            StyleSheet stylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.pontoco.lattice/Editor/UI/LatticePreferencesWindow.uss");
            rootElement.styleSheets.Add(stylesheet);
            rootElement.AddToClassList(UssClassName);

            Label title = new("Lattice");
            title.AddToClassList(TitleUssClassName);
            rootElement.Add(title);

            InspectorElement settings = new(preferences);
            rootElement.Add(settings);
            settings.AddToClassList(SettingsUssClassName);

            rootElement.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
            settings.RegisterCallbackOnce<GeometryChangedEvent>(RemoveScriptProperty);
            return;

            // Removes the script property field from the inspector.
            void RemoveScriptProperty(GeometryChangedEvent evt)
            {
                var field = settings.Q<PropertyField>("PropertyField:m_Script");
                if (field != null)
                {
                    field.RemoveFromHierarchy();
                    return;
                }
                
                // If the script wasn't removed, try again.
                settings.RegisterCallbackOnce<GeometryChangedEvent>(RemoveScriptProperty);
            }
        }
        

        private static void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            // Restore the original flags
            var preferences = LatticePreferences.instance;
            preferences.hideFlags = HideFlags.HideAndDontSave;
            preferences.Save();
        }
    }
}
