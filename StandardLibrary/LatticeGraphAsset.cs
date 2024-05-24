using UnityEngine;

namespace Lattice
{
    /// <summary>
    /// A stable UnityEngine.Object class for a Lattice Graph. This allows us to keep a stable GUID for LatticeGraph assets
    /// even if the underlying scripts or assemblies change, maintaining backwards compatibility if we update.
    /// </summary>
    [CreateAssetMenu(fileName = "NewGraph.asset", menuName = "Lattice Script", order = 0)]
    public class LatticeGraphAsset : LatticeGraph
    {
    }
}
