using Lattice;
using Lattice.Base;
using Lattice.StandardLibrary;
using Unity.Assertions;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Examples
{
    [LatticeNodes]
    public static class SamplesNodes
    {
        public static float2 MovementInput()
        {
            float2 result = new(0, 0);
            result.y += Input.GetKey(KeyCode.W) ? 1 : 0;
            result.x -= Input.GetKey(KeyCode.A) ? 1 : 0;
            result.y -= Input.GetKey(KeyCode.S) ? 1 : 0;
            result.x += Input.GetKey(KeyCode.D) ? 1 : 0;

            return result;
        }

        public static (float2 pos, bool left, bool middle, bool right) MouseInput()
        {
            return (new(Input.mousePosition.x, Input.mousePosition.y), Input.GetMouseButton(0), Input.GetMouseButton(2),
                Input.GetMouseButton(1));
        }

        public static float3 SimpleStateMachine(this ref int state, bool inputIsOn)
        {
            if (inputIsOn)
            {
                state = state == 0 ? 1 : 0;
            }

            return state == 0 ? float3.zero : new float3(1f, 1f, 1f);
        }

        public static void DefaultSideInputs([Prop] bool flag, [Prop] float3 quat, [Prop] Entity entity) { }

        public static float3 PositionMover(this ref float3 position, float2 movement)
        {
            movement *= 0.2f;

            position.x += movement.x;
            position.y += movement.y;

            return position;
        }

        public static (int split1, int split2, int split3) MultiReturn(int source)
        {
            return (source / 2, source / 2, source / 3);
        }

        // This function retains state from the previous frame.
        public static int StateOnly(this ref int state)
        {
            state += 1;
            return state;
        }

        public static void WriteState(ref int state)
        {
            state = 5;
        }

        // This function retains state from the previous frame.
        public static float3 PositionState(this ref float3 state)
        {
            return state;
        }

        public static int Num()
        {
            return 5;
        }

        public static float3 Sin3(float3 input)
        {
            return math.sin(input);
        }

        public static float Time()
        {
            return UnityEngine.Time.time;
        }

        public static float3 Time3()
        {
            return UnityEngine.Time.time;
        }

        public static Entity? NullEntity()
        {
            return Entity.Null;
        }

        public static float3 OffsetSin3(Entity e)
        {
            return math.sin(e.Index + UnityEngine.Time.time);
        }

        public static void WriteToPosition(float3 value, ref float3 destination)
        {
            destination = value;
        }

        public static void WriteOnly(ref float3 destination)
        {
            destination = new float3(1, 1, 1);
        }

        public static void ProjectItem1(this ref float3 state, ref (float3, float3) write)
        {
            write.Item1 = state;
        }

        public static void AssertEqual(int x, int y)
        {
            Assert.AreEqual(x, y);
        }

        public static int PlusOne(this ref int x, bool addOne)
        {
            if (addOne)
            {
                x += 1;
            }
            return x;
        }
    }
}
