using System;

namespace Lattice.Editor
{
    /// <summary>Specifies which node types this editor applies to.</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class NodeCustomEditor : Attribute
    {
        public readonly Type NodeType;

        public NodeCustomEditor(Type nodeType)
        {
            NodeType = nodeType;
        }
    }
}
