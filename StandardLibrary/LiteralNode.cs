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

    [Serializable, NodeCreateMenu("Math/Float1")]
    internal class Float1 : LiteralNode<float> { }

    [Serializable, NodeCreateMenu("Math/Float3")]
    internal class Float3 : LiteralNode<float3> { }

    [Serializable, NodeCreateMenu("Math/Int1")]
    internal class Int1 : LiteralNode<int> { }

    [Serializable, NodeCreateMenu("Math/Bool1")]
    internal class Bool1 : LiteralNode<bool> { }

    [Serializable, NodeCreateMenu("Math/Color")]
    internal class ColorNode : LiteralNode<Color> { }
}
