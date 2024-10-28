using System;
using System.Collections.Generic;
using System.Linq;
using Lattice.Base;
using Lattice.IR;
using Lattice.Nodes;
using Unity.Assertions;
using Unity.Entities;
using UnityEngine;

namespace Lattice.StandardLibrary
{
    [NodeCreateMenu("Lattice/Utility/Previous Value")]
    public class PreviousNode : CrossRefNode
    {
        protected override IEnumerable<PortData> GenerateInputPorts()
        {
            yield return new PortData("default", optional: true);
        }

        protected override IEnumerable<PortData> GenerateOutputPorts()
        {
            yield return new PortData("previous");
        }
        public override void CompileToIR(GraphCompilation compilation)
        {
            if (ResolvedNode == null) {
                compilation.AddNode(this, new MalformedIRNode("No node selected."));
                return;
            }

            var prevNode = compilation.AddNode(this, new PreviousIRNode());
            
            if (!GetPort("default").GetEdges().Any()) {
                // Hmm. We need it to be the inferred type! Which.. means we need generics gah.
                // compilation.AddNode(this, FunctionIRNode.FromStaticMethod<PreviousNode>(nameof(GetDefaultValue), typeof()) );
            }
        }
        
        public static T GetDefaultValue<T>()
        {
            return default;
        }

        public override void AddAdditionalEdges(GraphCompilation compilation)
        {
            // Add edge to the resolved node in the other graph.
            // Add a direct edge in the compilation between the two graphs.
            // We have to do this in the second pass here, to guarantee the other node is created.
            GraphCompilation.Mapping otherNodeMapping = compilation.Mappings[ResolvedNode];

            // If the referenced port is missing on the other node, just do nothing.
            if (!otherNodeMapping.OutputPortMap.TryGetValue(OtherPort, out IRNodeRef node))
            {
                compilation.ReplaceNodeWithMalformed(this,
                    new Exception($"Port [{OtherPort}] not found on node [{ResolvedNode}]."));
                return;
            }
            
            var referencedNode = node.Node;
            Assert.IsNotNull(referencedNode);

            PreviousIRNode prevNode = (PreviousIRNode)compilation.Mappings[this].PrimaryNode.Node;
            Debug.Log(referencedNode);
            prevNode.BackRef = compilation.GetNodeRef(referencedNode);
        }
    }
}
