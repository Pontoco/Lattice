using System;
using System.Collections.Generic;
using Lattice.Base;
using Lattice.IR;
using Lattice.Nodes;
using Lattice.Utils;
using Unity.Collections;

namespace Lattice.StandardLibrary
{
    /// <summary>Combines several booleans with the OR operation.</summary>
    [NodeCreateMenu("Lattice/Logic/Or")]
    public class OrNode : LatticeNode
    {
        private const string PortBooleans = "booleans";
        private const string PortResult = "result";
        public override string DefaultName => "or";

        protected override IEnumerable<PortData> GenerateInputPorts()
        {
            yield return new PortData(PortBooleans)
            {
                acceptMultipleEdges = true,
                defaultType = typeof(bool),
                vertical = true
            };
        }

        protected override IEnumerable<PortData> GenerateOutputPorts()
        {
            yield return new PortData(PortResult)
            {
                defaultType = typeof(bool),
                vertical = true
            };
        }

        public override void CompileToIR(GraphCompilation compilation)
        {
            IRNode collect = compilation.AddNode(this, new CollectIRNode<bool>());
            compilation.MapInputPort(this, PortBooleans, collect, CollectIRNode.PortInputs);

            IRNode and = compilation.AddNode(this, FunctionIRNode.Create<NativeArray<bool>, bool>(Or));
            and.AddInput("booleans", collect);
            
            compilation.SetPrimaryNode(this, and);
            compilation.MapOutputPort(this, PortResult, and);
        }

        // "or" all of the inputs together
        private static bool Or(NativeArray<bool> booleans)
        {
            bool result = false;
            foreach (var input in booleans)
            {
                result |= input;
            }

            return result;
        }
    }
}
