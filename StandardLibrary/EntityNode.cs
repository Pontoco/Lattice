using System;
using System.Collections.Generic;
using Lattice.Base;
using Lattice.IR;
using UnityEngine.Scripting.APIUpdating;

namespace Lattice.Nodes
{
    /// <summary>
    ///     Returns the entity given by this node's qualifiers. Must be used within the SharedContext of a script running on an
    ///     entity.
    /// </summary>
    [MovedFrom(true, sourceAssembly:"Lattice.Runtime")]
    [Serializable]
    [NodeCreateMenu("Lattice/Utility/This Entity")]
    public class EntityNode : LatticeNode
    {
        private const string PortEntity = "Output";
        
        protected override IEnumerable<PortData> GenerateOutputPorts()
        {
            yield return new PortData(PortEntity);
        }

        public override string DefaultName => "Entity";

        public override void CompileToIR(GraphCompilation compilation)
        {
           // Just returns the implicit entity node.
           IRNode entityNode = compilation.GetImplicitEntity(Graph);
           
           compilation.Mappings[this].Nodes.Add(entityNode);
           compilation.MapOutputPort(this, PortEntity, entityNode);
           compilation.SetPrimaryNode(this, entityNode);
        }
    }
}
