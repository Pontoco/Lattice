using JetBrains.Annotations;
using Lattice.Base;
using Lattice.Editor;
using Lattice.Editor.Utils;
using Lattice.Editor.Views;
using Unity.Entities.UI;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lattice.StandardLibrary.Editor
{
    [UsedImplicitly]
    [NodeCustomEditor(typeof(ScriptNode))]
    public class ScriptNodeView : LatticeNodeView
    {
        private new ScriptNode Target => (ScriptNode)NodeTarget;

        public ScriptNodeView()
        {
            // Open script at method when the node is double-clicked.
            var clickable = new Clickable(() => SourceUtility.OpenAtMethod(Target.Method.Resolve()));
            clickable.activators.Clear();
            clickable.activators.Add(new ManipulatorActivationFilter
            {
                button = MouseButton.LeftMouse,
                clickCount = 2
            });
            this.AddManipulator(clickable);
        }

        /// <inheritdoc />
        public override PortView AddPort(Direction direction, EdgeConnectorListener listener, PortData portData)
        {
            PortView p = base.AddPort(direction, listener, portData);

            if (portData.hasDefault)
            {
                // Note: I attempted to use PropertyElement to render the inspectors for the properties, and handle the 
                // undo/redo myself. This failed miserably and I wasted like 10 hours attempting to get this working.
                // Just use SerializedProperty + PropertyField instead. I got it working, but failed to get Undo/Redo
                // to function correctly. :(

                var defaultWrapper = Target.GetPortDefaultWrapper(portData.identifier);
                int idx = Target.PortDefaultValues.IndexOf(defaultWrapper);
                SerializedProperty serializedProperty = FindSerializedProperty(nameof(ScriptNode.PortDefaultValues))
                                                        .GetArrayElementAtIndex(idx).FindPropertyRelative("Value");
                if (serializedProperty != null)
                {
                    PropertyField propertyField = new(serializedProperty)
                    {
                        label = ""
                    };
                    propertyField.AddToClassList("hide-label");
                    p.Add(propertyField);
                    propertyField.Bind(Owner.SerializedGraph);
                }
                else
                {
                    Debug.LogError($"Couldn't find property path for on " +
                                   $"port [{NodeTarget.DefaultName}]:[{portData.identifier}]");
                }
            }

            return p;
        }
    }
}
