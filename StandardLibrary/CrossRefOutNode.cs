using System;
using System.Collections.Generic;
using Lattice.Base;
using Lattice.IR;
using Unity.Entities;
using UnityEngine.Assertions;
using UnityEngine.Scripting.APIUpdating;

namespace Lattice.Nodes
{
    [MovedFrom(true, sourceAssembly:"Lattice.Runtime")]
    [NodeCreateMenu("Lattice/Utility/Reference")]
    [Serializable]
    public class CrossRefOutNode : CrossRefNode
    {
        protected override IEnumerable<PortData> GenerateInputPorts()
        {
            yield return new PortData("qualifier", optional: true)
            {
                defaultType = typeof(Entity)
            };
        }

        protected override IEnumerable<PortData> GenerateOutputPorts()
        {
            yield return new PortData("output")
            {
                defaultType = typeof(object) //GetResolvedType() // todo: circular reference via Graph.Initialize.
            };
        }

        public override void CompileToIR(GraphCompilation compilation)
        {
            if (ResolvedNode == null)
            {
                compilation.AddNode(this, new MalformedIRNode("No node selected in Reference node."));
                return;
            }
            
            compilation.AddNode(this, new QualifierTransformIRNode());
        }

        public override void AddAdditionalEdges(GraphCompilation compilation)
        {
            // Add edge to the resolved node in the other graph.
            // Add a direct edge in the compilation between the two graphs.
            // We have to do this in the second pass here, to guarantee the other node is created.
            GraphCompilation.Mapping otherNodeMapping = compilation.Mappings[ResolvedNode];

            // If the referenced port is missing on the other node, just do nothing.
            if (!otherNodeMapping.OutputPortMap.ContainsKey(OtherPort))
            {
                compilation.ReplaceNodeWithMalformed(this,
                    new Exception($"Port [{OtherPort}] not found on node [{ResolvedNode}]."));
                return;
            }
            
            var referencedNode = otherNodeMapping.OutputPortMap[OtherPort];
            Assert.IsNotNull(referencedNode);

            compilation.Mappings[this].Nodes[0].AddInput("input", referencedNode);
            
            // The 'qualifier' input is provided by the default graph compilation input connection pass.
        }
    }
}
