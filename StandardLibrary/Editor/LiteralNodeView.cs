using System;
using System.Collections.Generic;
using Lattice.Base;
using Lattice.Editor;
using Lattice.Editor.Views;
using Lattice.Nodes;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Lattice.StandardLibrary.Editor
{
    [NodeCustomEditor(typeof(LiteralNode<>))]
    public class LiteralNodeView : LatticeNodeView
    {
        public const string ValueUssClassName = UssClassName + "__value";

        private static readonly Dictionary<Type, Type> LiteralTypeLookup = new();
        protected PropertyField LiteralPropertyField;

        public override void Initialize(BaseGraphView owner, BaseNode node)
        {
            base.Initialize(owner, node);
            
            styleSheets.Add(
                AssetDatabase.LoadAssetAtPath<StyleSheet>(
                    "Packages/com.pontoco.lattice/Editor/UI/LiteralNodeView.uss"));

            AddToClassList(UssClassName);
        }

        /// <inheritdoc />
        protected override void CreateNodeInspector()
        {
            base.CreateNodeInspector();
            
            LatticeNode n = (LatticeNode)NodeTarget;
            
            // Style property field:
            LiteralPropertyField = controlsContainer.Q<PropertyField>(nameof(LiteralNode<int>.Value));
            LiteralPropertyField.AddToClassList(ValueUssClassName);
            LiteralPropertyField.label = "";
            LiteralPropertyField.AddToClassList("hide-label");

            if (n.OutputPorts.Count != 1)
            {
                // This node should always have one output. Internal System Error.
                title = "ICE: Node Malformed. Incorrect outputs";
            }
            
            Type literalType = GetLiteralTypeFromNodeType(n);
            
            // Add a USS class with the type.
            LiteralPropertyField.AddToClassList($"{ValueUssClassName}--{literalType.Name}");

            rightTitleContainer.Add(LiteralPropertyField);
        }

        /// <summary>Get the literal type associated with the node.</summary>
        private static Type GetLiteralTypeFromNodeType(BaseNode node)
        {
            Type nodeType = node.GetType();
            if (LiteralTypeLookup.TryGetValue(nodeType, out Type literalType))
            {
                return literalType;
            }

            Type queryType = nodeType;
            while (queryType!.BaseType != typeof(LatticeNode))
            {
                queryType = queryType.BaseType;
            }
            literalType = queryType.GetGenericArguments()[0];
            LiteralTypeLookup.Add(nodeType, literalType);
            return literalType;
        }
    }

    [NodeCustomEditor(typeof(ColorNode))]
    public class ColorNodeView : LiteralNodeView
    {
        /// <inheritdoc />
        public override void Initialize(BaseGraphView owner, BaseNode node)
        {
            base.Initialize(owner, node);

            TrySetColorFieldToHDR();
            return;

            void TrySetColorFieldToHDR()
            {
                ColorField colorField = LiteralPropertyField.Q<ColorField>();
                if (colorField == null)
                {
                    LiteralPropertyField.RegisterCallbackOnce<GeometryChangedEvent>(_ => TrySetColorFieldToHDR());
                    return;
                }
                colorField.hdr = true;
            }
        }
    }
}
