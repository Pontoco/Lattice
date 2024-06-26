using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lattice.Editor.Utils
{
    // todo: Can we use this to draw float3? Or something similar?
    
    // We need a drawer to display Vector4 on a single line because by default it's a toggle.
    [CustomPropertyDrawer(typeof(Vector4))]
    public class Vector4Drawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var vectorField = new Vector4Field { value = property.vector4Value };
            vectorField.RegisterValueChangedCallback(e =>
            {
                property.vector4Value = e.newValue;
                property.serializedObject.ApplyModifiedProperties();
            });

            return vectorField;
        }
    }
}
