using Lattice.Base;
using Unity.Assertions;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Lattice.StandardLibrary
{
    /// <summary>The Lattice Standard Library is a default set of nodes that are provided by the Lattice package.</summary>
    [LatticeNodes("Lattice")]
    public static class StandardNodes
    {
        /// <summary>How long this entity has been alive and enabled. Time in seconds.</summary>
        public static float TimeAlive(this ref float seconds, EntityManager em, Entity e)
        {
            seconds += em.World.Time.DeltaTime;
            return seconds;
        }

        /// <summary>Evaluates an animation curve using the input float. Useful for simple animations and interpolation.</summary>
        public static float SimpleAnimate([Prop] AnimationCurve curve, float time, float outputScale = 1)
        {
            return curve.Evaluate(time) * outputScale;
        }

        /// <summary>
        ///     Spawns the given prefab, when the 'spawn' input is true. Includes options for specifying position, owner
        ///     (LEG), and parenting under the owner.
        /// </summary>
        public static Entity? Spawn(EntityManager em, Entity prefab, bool spawn = false,
                                    float3? position = default, Entity? owner = null, [Prop] bool parent = false)
        {
            if (spawn)
            {
                var spawned = em.Instantiate(prefab);

                // Set the position
                if (position.HasValue)
                {
                    var transform = em.GetComponentData<LocalTransform>(spawned);
                    transform.Position = position.Value;
                    em.SetComponentData(spawned, transform);
                }

                // Add to owner's Linked Entity Group
                if (owner != null)
                {
                    var leg = em.GetBuffer<LinkedEntityGroup>(owner.Value);
                    leg.Add(spawned);
                }

                // If we're parenting, parent transform under owner.
                if (parent)
                {
                    Assert.IsTrue(owner.HasValue);
                    em.AddComponentData(spawned, new Parent
                    {
                        Value = owner.Value,
                    });
                }
                return spawned;
            }
            return null;
        }

        /// <summary>Splits a LocalTransform into its fields.</summary>
        public static (float3 position, quaternion rotation, float scale) SplitLocalTransform(LocalTransform transform)
        {
            return (transform.Position, transform.Rotation, transform.Scale);
        }
        
        /// <summary>
        /// Splits the RigidTransform into its fields.
        /// </summary>
        public static (float3 position, quaternion rotation) SplitRigidTransform(RigidTransform transform)
        {
            return (transform.pos, transform.rot);
        }
    }

    [LatticeNodes("Lattice/Logic")]
    public static class StdLogicNodes
    {
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

        /// <summary>The logical 'not' operator.</summary>
        public static bool Not(bool value)
        {
            return !value;
        }
    }
    
    /// <summary>
    /// General math nodes.
    /// </summary>
    [LatticeNodes("Lattice/Math")]
    public static class Math {
        /// <summary>
        /// Scales a 2D value by a number.
        /// </summary>
        public static float2 Scale(float2 input, [Prop] float factor)
        {
            return factor * input;
        }
        
        /// <summary>
        /// Stores a 2D position value, which can be updated each frame by an input velocity.
        /// </summary>
        public static float2 Position(this ref float2 state, float2 velocity = new())
        {
            state += velocity;
            return state;
        }
        
        public static float3 ToFloat3(float2 input)
        {
            return new float3(input.x, input.y, 0);
        }
    }

    /// <summary>
    ///     Nodes for splitting/makin some simple types. Once we have a general split/make node, these will be obsolete.
    ///     Note: All of these nodes can be defined yourself.
    /// </summary>
    [LatticeNodes("Lattice/Constant/Structs")]
    public static class StdStructNodes
    {
        public static float2 MakeFloat2(float x = 0, float y = 0)
        {
            return new(x, y);
        }

        public static float3 MakeFloat3(float x = 0, float y = 0, float z = 0)
        {
            return new(x, y, z);
        }

        public static int2 MakeInt2(int x = 0, int y = 0)
        {
            return new(x, y);
        }

        public static int3 MakeInt3(int x = 0, int y = 0, int z = 0)
        {
            return new(x, y, z);
        }

        public static quaternion MakeEuler(float x = 0, float y = 0, float z = 0)
        {
            return quaternion.Euler(x, y, z);
        }

        public static (float x, float y, float z) SplitFloat3(float3 value)
        {
            return (value.x, value.y, value.z);
        }

        public static (float x, float y) SplitFloat2(float2 value)
        {
            return (value.x, value.y);
        }
    }

    /// <summary>
    ///     Nodes for splitting/makin some simple types. Once we have a general split/make node, these will be obsolete.
    ///     Note: All of these nodes can be defined yourself.
    /// </summary>
    [LatticeNodes("Lattice/Input")]
    public static class Input
    {
        /// <summary>Returns a 2D vector pointing in the direction of the current wasd input. ie. (1, 0)</summary>
        public static float2 WasdInput()
        {
            var w = UnityEngine.Input.GetKey(KeyCode.W);
            var a = UnityEngine.Input.GetKey(KeyCode.A);
            var s = UnityEngine.Input.GetKey(KeyCode.S);
            var d = UnityEngine.Input.GetKey(KeyCode.D);

            float2 result = new();
            result.y += w ? 1 : 0;
            result.y -= s ? 1 : 0;
            result.x += d ? 1 : 0;
            result.x -= a ? 1 : 0;

            return result;
        }

        /// <summary>
        ///     Returns true while the key is held. Use <see cref="StdLogicNodes.BecomesTrue" /> or
        ///     <see cref="StdLogicNodes.BecomesFalse" /> if you need to know if a key was pressed this frame.
        /// </summary>
        /// <param name="code"></param>
        /// <returns></returns>
        public static bool KeyIsDown([Prop] KeyCode code)
        {
            return UnityEngine.Input.GetKey(code);
        }
    }
}
