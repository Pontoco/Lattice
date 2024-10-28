using System;
using System.Collections.Generic;
using System.Reflection;
using Lattice.Base;
using Lattice.IR;
using Lattice.StandardLibrary;
using SRTK;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

[assembly:
    RegisterGenericComponentType(
        typeof(BakeDataLatticeNode<BlobAssetReference<BlobTransformClip>, AnimatedTransform>.BakeDataBuffer))]

namespace Lattice.StandardLibrary
{
    /// <summary>Blob container for the curve data.</summary>
    public struct BlobTransformClip
    {
        public BlobCurve PositionX;
        public BlobCurve PositionY;
        public BlobCurve PositionZ;
        public BlobCurve RotationX;
        public BlobCurve RotationY;
        public BlobCurve RotationZ;
        public BlobCurve RotationW;
    }

    /// <summary>
    ///     Evaluates an Animation Clip containing root position and rotation curves. Bakes data to a blob asset for ECS
    ///     storage.
    /// </summary>
    [NodeCreateMenu("Lattice/Utility/Animated Transform")]
    [Serializable]
    public class AnimatedTransform : BakeDataLatticeNode<BlobAssetReference<BlobTransformClip>, AnimatedTransform>
    {
        public AnimationClip Clip;

        /// <inheritdoc />
        protected override BlobAssetReference<BlobTransformClip>? BakeData(
            IBaker baker, LatticeExecutorAuthoring authoring)
        {
            if (Clip == null)
            {
                Debug.LogWarning($"No animation clip provided. [{this}]");
                return null;
            }

            using BlobBuilder builder = new(Allocator.Temp);
            ref var clip = ref builder.ConstructRoot<BlobTransformClip>();

            var curves = AnimationUtility.GetCurveBindings(Clip);

            foreach (var c in curves)
            {
                if (c.propertyName == "m_LocalPosition.x")
                {
                    AnimationCurve curve = AnimationUtility.GetEditorCurve(Clip, c);
                    BlobCurve.CreateAt(ref clip.PositionX, curve, builder);
                }

                if (c.propertyName == "m_LocalPosition.y")
                {
                    AnimationCurve curve = AnimationUtility.GetEditorCurve(Clip, c);
                    BlobCurve.CreateAt(ref clip.PositionY, curve, builder);
                }

                if (c.propertyName == "m_LocalPosition.z")
                {
                    AnimationCurve curve = AnimationUtility.GetEditorCurve(Clip, c);
                    BlobCurve.CreateAt(ref clip.PositionZ, curve, builder);
                }

                if (c.propertyName == "m_LocalRotation.x")
                {
                    AnimationCurve curve = AnimationUtility.GetEditorCurve(Clip, c);
                    BlobCurve.CreateAt(ref clip.RotationX, curve, builder);
                }

                if (c.propertyName == "m_LocalRotation.y")
                {
                    AnimationCurve curve = AnimationUtility.GetEditorCurve(Clip, c);
                    BlobCurve.CreateAt(ref clip.RotationY, curve, builder);
                }

                if (c.propertyName == "m_LocalRotation.z")
                {
                    AnimationCurve curve = AnimationUtility.GetEditorCurve(Clip, c);
                    BlobCurve.CreateAt(ref clip.RotationZ, curve, builder);
                }
                if (c.propertyName == "m_LocalRotation.w")
                {
                    AnimationCurve curve = AnimationUtility.GetEditorCurve(Clip, c);
                    BlobCurve.CreateAt(ref clip.RotationW, curve, builder);
                }
            }

            var blobRef = builder.CreateBlobAssetReference<BlobTransformClip>(Allocator.Persistent);
            baker.AddBlobAsset(ref blobRef, out var hash);
            return blobRef;
        }

        /// <inheritdoc />
        public override void CompileToIR(GraphCompilation compilation)
        {
            base.CompileToIR(compilation);

            var blobRef = compilation.GetPrimaryNode(this);
            var evaluateNode = compilation.AddNode(this,
                FunctionIRNode.FromStaticMethod<AnimatedTransform>(nameof(EvaluateClip)));
            evaluateNode.AddInput("clip", blobRef);
            compilation.MapInputPort(this, "time", evaluateNode, "time");
            compilation.SetPrimaryNode(this, evaluateNode);
            compilation.MapOutputPort(this, "rigidTransform", evaluateNode);

            if (GetPort("time").GetEdges().Count == 0)
            {
                var defaultTime = new FunctionIRNode(
                    typeof(CoreIRNodes).GetMethod(nameof(CoreIRNodes.DefaultValue),
                                           BindingFlags.Public | BindingFlags.Static)!
                                       .MakeGenericMethod(typeof(float)));
                compilation.AddNode(this, defaultTime);
                evaluateNode.AddInput("time", defaultTime);
            }
        }

        /// <summary>Evaluate the blob clip.</summary>
        public static RigidTransform EvaluateClip(BlobAssetReference<BlobTransformClip> clip, float time)
        {
            ref BlobTransformClip c = ref clip.Value;
            
            var pos = new float3(
                c.PositionX.IsEmpty() ? 0 : c.PositionX.Evaluate(time),
                c.PositionY.IsEmpty() ? 0 : c.PositionY.Evaluate(time),
                c.PositionZ.IsEmpty() ? 0 : c.PositionZ.Evaluate(time));

            var rot = new quaternion(
                c.RotationX.IsEmpty() ? 0 : c.RotationX.Evaluate(time),
                c.RotationY.IsEmpty() ? 0 : c.RotationY.Evaluate(time),
                c.RotationZ.IsEmpty() ? 0 : c.RotationZ.Evaluate(time),
                c.RotationW.IsEmpty() ? 0 : c.RotationW.Evaluate(time));
            rot = math.normalizesafe(rot);

            return new RigidTransform(rot, pos);
        }

        /// <inheritdoc />
        protected override IEnumerable<PortData> GenerateOutputPorts()
        {
            yield return new PortData("rigidTransform");
        }

        /// <inheritdoc />
        protected override IEnumerable<PortData> GenerateInputPorts()
        {
            yield return new PortData("time", optional: true, defaultType: typeof(float));
        }
    }
}
