using System;
using System.Collections.Generic;
using Lattice.Base;
using Lattice.IR;
using Lattice.Nodes;
using Unity.Mathematics;
using UnityEngine;

namespace Lattice.StandardLibrary
{
    /// <summary>A literal value, like int or float3 or string.</summary>
    [Serializable]
    internal abstract class LiteralNode<T> : LatticeNode where T : unmanaged
    {
        public T Value;

        protected override IEnumerable<PortData> GenerateOutputPorts()
        {
            yield return new PortData("Output");
        }

        public override void CompileToIR(GraphCompilation compilation)
        {
            compilation.AddNode(this, FunctionIRNode.FromStaticMethod<LiteralNode<T>>(nameof(ReturnLiteral)));
        }

        private static T ReturnLiteral(LatticeNode node)
        {
            return ((LiteralNode<T>)node).Value;
        }
    }

    [Serializable, NodeCreateMenu("Lattice/Constant/Float")]
    internal class Float1 : LiteralNode<float>
    {
        /// <inheritdoc />
        public override string DefaultName => "float";
    }
    
    [Serializable, NodeCreateMenu("Lattice/Constant/Float2")]
    internal class Float2 : LiteralNode<float2>
    {
        /// <inheritdoc />
        public override string DefaultName => nameof(float2);
    }

    [Serializable, NodeCreateMenu("Lattice/Constant/Float3")]
    internal class Float3 : LiteralNode<float3>
    {
        /// <inheritdoc />
        public override string DefaultName => nameof(float3);
    }

    [Serializable, NodeCreateMenu("Lattice/Constant/Int")]
    internal class Int1 : LiteralNode<int>
    {
        /// <inheritdoc />
        public override string DefaultName => "int";
    }

    [Serializable, NodeCreateMenu("Lattice/Constant/Bool")]
    internal class Bool1 : LiteralNode<bool>
    {
        /// <inheritdoc />
        public override string DefaultName => "bool";
    }

    [Serializable, NodeCreateMenu("Lattice/Constant/Color")]
    internal class ColorNode : LiteralNode<Color>
    {
        public ColorNode()
        {
            Value = Color.red;
        }
        
        /// <inheritdoc />
        public override string DefaultName => nameof(UnityEngine.Color);
    }
}
