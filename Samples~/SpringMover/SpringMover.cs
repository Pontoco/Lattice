using Lattice.Base;
using Lattice.StandardLibrary;
using Unity.Mathematics;
using UnityEngine;

[LatticeNodes]
public static class SpringMover
{
    public static float3 DirectControlledPosition(this ref float3 pos, float2 joystickInput, [Prop] float speed)
    {
        pos.x += joystickInput.x * speed;
        pos.y += joystickInput.y * speed;
        return pos;
    }

    public static float3 SpringFloat3(this ref (float3 velocity, float3 position) state, float3 target,
                                      [Prop] float springK, [Prop] float springDamp)
    {
        float3 difference = target - state.position;
        float3 force = springK * difference - springDamp * state.velocity;
        state.velocity += force * Time.deltaTime;
        state.position += state.velocity * Time.deltaTime;

        return state.position;
    }
}
