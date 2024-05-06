using Drawing;
using Lattice.Base;
using Lattice.StandardLibrary;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace Samples.Dashing
{
    [LatticeNodes]
    public static class Dashing
    {
        // The base movement velocity from the joystick.
        public static float3 BaseMovement(float2 joystick, [Prop] float movementSpeed)
        {
            return new float3(joystick.x, 0, joystick.y) * movementSpeed;
        }
        
        // The dash velocity, if we were dashing.
        public static (float3 dashVelocity, bool canDash) DashVelocity(float3 currentVelocity, float3 baseMovement, [Prop] float dashSpeed)
        {
            if (math.length(currentVelocity) == 0 && math.length(baseMovement) == 0)
            {
                return (float3.zero, false);
            }
            
            var dashBasis = math.length(baseMovement) > 0 ? baseMovement : currentVelocity;
            return (dashSpeed * math.normalize(dashBasis), math.length(dashBasis) > .1f);
        }
        
        // Don't allow dashing if we wouldn't move anywhere.
        public static bool CanDash(float3 dashVelocity)
        {
            return math.length(dashVelocity) > 0;
        }
        
        // A general ability cooldown node. It counts down after firing.
        public static bool AbilityCooldown(ref this float cooldownTime, bool requested, [Prop] float cooldown)
        {
            cooldownTime = math.max(0, cooldownTime - World.DefaultGameObjectInjectionWorld.Time.DeltaTime);

            if (cooldownTime == 0 && requested)
            {
                cooldownTime = cooldown;
                return true;
            }

            return false;
        }

        public static float3 Movement(float3 baseMovement, bool isDashing, float3 dashVelocity)
        {
            if (isDashing)
            {
                baseMovement = dashVelocity;
            }

            return baseMovement;
        }
    }
}
