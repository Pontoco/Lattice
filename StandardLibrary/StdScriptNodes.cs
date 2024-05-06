using Unity.Mathematics;
using Unity.Transforms;

namespace Lattice.StandardLibrary
{
    /// <summary>The Lattice Standard Library is a set of nodes that are available to all Lattice programs.</summary>
    /// <remarks>
    ///     These nodes have the same stability guarantees as the rest of the Lattice package, so be thoughtful about
    ///     naming, and what to expose to users. Refactoring these nodes can cause lots of compilation errors.
    /// </remarks>
    [LatticeNodes]
    public static class StdScriptNodes
    {
        /// <summary>Splits a LocalTransform into its fields.</summary>
        public static (float3 position, quaternion rotation) SplitLocalTransform(LocalTransform transform)
        {
            return (transform.Position, transform.Rotation);
        }

        /// <summary>
        ///     Returns true on the frame the input value goes from false to true. You can think of this as the 'rising edge'
        ///     in electronics, if you like.
        /// </summary>
        public static bool BecomesTrue(ref this bool previous, bool value)
        {
            bool trueThisFrame = !previous && value;
            previous = value;

            return trueThisFrame;
        }

        /// <summary>
        ///     Returns true on the frame the input value goes from true to false. You can think of this as the 'falling edge'
        ///     in electronics, if you like.
        /// </summary>
        public static bool BecomesFalse(ref this bool previous, bool value)
        {
            bool falseThisFrame = previous && !value;
            previous = value;

            return falseThisFrame;
        }
    }
}
