using System;
using Lattice.Base;
using Lattice.StandardLibrary;
using Unity.Mathematics;
using UnityEngine;

namespace GTF
{
    [LatticeNodes]
    public static class PlatformerSimple
    {
        public struct PlayerState
        {
           public float3 position;
           public float3 velocity;

           public override string ToString()
           {
               return $"(Pos: {position}, Vel: {velocity})";
           }
        }
        
        public static bool HandleGround(ref PlayerState state, [Prop] float height)
        {
            if (state.position.y <= height && state.velocity.y < 0)
            {
                state.position.y = height;
                state.velocity.y = 0;

                return true;
            }

            return false;
        }

        public static void ZeroOutRotation(bool isInputDown, ref float rot)
        {
            if (isInputDown)
            {
                rot = 0;
            }
        }

        public static void RotateThatThing(ref float thingToRotate)
        {
            thingToRotate += 5;
        }

        public static quaternion SimpleRotate(this ref float rot, bool onGround, [Prop] float radPerSecond)
        {
            if (!onGround)
            {
                rot += radPerSecond * Time.deltaTime;
            }
            return quaternion.AxisAngle(new (0,0,1), rot);
        }
        
        public static void StepPlayer(ref PlayerState state, [Prop] float gravity)
        {
            state.position += state.velocity;
            state.velocity.y -= gravity * Time.deltaTime;
        }

        public static float3 State(this ref PlayerState state)
        {
            return state.position;
        }

        public static void HandleMovement(ref PlayerState state, float2 input, bool jump, [Prop] float jumpVelocity)
        {
            state.position.x += input.x * .05f;

            if (jump)
            {
                state.velocity.y = jumpVelocity;
            }
        }
    }
}
