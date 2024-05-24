using System;
using System.Collections.Generic;
using Lattice.Base;
using Unity.Assertions;
using UnityEngine;

// todo: implement qualifiers

namespace Lattice.Nodes
{
    [Serializable]
    public abstract class CrossRefNode : LatticeNode
    {
        [SerializeField]
        protected LatticeGraph OtherGraph;

        [SerializeField]
        protected string OtherNode;

        [SerializeField]
        protected string OtherPort; // Must be non-null. "full value" of node is no longer a thing.

        private LatticeNode resolved;

        public LatticeNode ResolvedNode
        {
            get
            {
                if (resolved == null || resolved.GUID != OtherNode)
                {
                    if (OtherGraph == null)
                    {
                        resolved = null;
                    }
                    else
                    {
                        // I think this is unnecessary -- OnEnable() is called when accessing and loading this reference.
                        // OtherGraph.Initialize();
                        resolved = OtherGraph.nodes.Find(n => n.GUID == OtherNode) as LatticeNode;
                    }
                }

                return resolved;
            }
        }

        public string GetResolvedPath()
        {
            Assert.IsNotNull(ResolvedNode);

            var path =
                $"{ResolvedNode.Graph.name}.{ResolvedNode.Name}";

            if (!string.IsNullOrEmpty(OtherPort) && ResolvedNode.OutputPorts.Count > 1)
            {
                path += "." + OtherPort;
            }

            return path;
        }

        /// <summary>
        ///     If the node has a set reference, but that target node could not be found. (As opposed to having no target node
        ///     set at all).
        /// </summary>
        public bool IsDisconnected()
        {
            return ResolvedNode == null && (OtherGraph != null || !string.IsNullOrEmpty(OtherNode));
        }

        public void SetTarget(BaseNode node, string port)
        {
            OtherGraph = (LatticeGraph)node.Graph;
            OtherNode = node.GUID;
            OtherPort = port;
            UpdateAllPorts();
        }

        public override IEnumerable<LatticeGraph> GetDependencies()
        {
            if (OtherGraph != null)
            {
                yield return OtherGraph;
            }
        }
    }
}
