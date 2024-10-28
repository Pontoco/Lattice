/************************************************************************************
| File: BlobCurve.cs                                                                |
| Project: lieene.Curve                                                             |
| Created Date: Thu Aug 27 2020                                                     |
| Author: Lieene Guo                                                                |
| -----                                                                             |
| Last Modified: Wed Oct 14 2020                                                    |
| Modified By: Lieene Guo                                                           |
| -----                                                                             |
| MIT License                                                                       |
|                                                                                   |
| Copyright (c) 2020 Lieene@ShadeRealm                                              |
|                                                                                   |
| Permission is hereby granted, free of charge, to any person obtaining a copy of   |
| this software and associated documentation files (the "Software"), to deal in     |
| the Software without restriction, including without limitation the rights to      |
| use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies     |
| of the Software, and to permit persons to whom the Software is furnished to do    |
| so, subject to the following conditions:                                          |
|                                                                                   |
| The above copyright notice and this permission notice shall be included in all    |
| copies or substantial portions of the Software.                                   |
|                                                                                   |
| THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR        |
| IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,          |
| FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE       |
| AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER            |
| LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,     |
| OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE     |
| SOFTWARE.                                                                         |
|                                                                                   |
| -----                                                                             |
| HISTORY:                                                                          |
| Date      	By	Comments                                                        |
| ----------	---	----------------------------------------------------------      |
************************************************************************************/


using System;
using System.Runtime.InteropServices;
using System.Diagnostics;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Assertions;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System.Runtime.CompilerServices;

namespace SRTK
{
    //using static MathX;
    using static CurveExt;
    using static math;
    using static BlobCurveSegment;
    using Debug = UnityEngine.Debug;

    public enum WrapMode : short
    {
        Clamp = 0,
        Loop = 1,
        PingPong = 2,
    }

    public struct BlobCurveSampler : IComponentData
    {
        public BlobAssetReference<BlobCurve> Curve;
        internal BlobCurveCache Cache;
        public BlobCurveSampler(BlobAssetReference<BlobCurve> curve)
        {
            Curve = curve;
            Cache = BlobCurveCache.Empty;
        }
    }

    public struct BlobCurveCache
    {
        public static readonly BlobCurveCache Empty = new BlobCurveCache() { Index = int.MinValue, NeighborhoodTimes = float.NaN };
        public float2 NeighborhoodTimes;
        public int Index;
    }

    public static partial class CurveExt
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static float Evaluate([NoAlias] ref this BlobAssetReference<BlobCurve> blob, in float time)
        => blob.Value.Evaluate(time);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static float EvaluateIgnoreWrapMode([NoAlias] ref this BlobAssetReference<BlobCurve> blob, in float time)
        => blob.Value.EvaluateIgnoreWrapMode(time);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static float Evaluate([NoAlias] ref this BlobCurveSampler sampler, in float time)
        => sampler.Curve.Value.Evaluate(time, ref sampler.Cache);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static float EvaluateIgnoreWrapMode([NoAlias] ref this BlobCurveSampler sampler, in float time)
        => sampler.Curve.Value.EvaluateIgnoreWrapMode(time, ref sampler.Cache);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static WrapMode ToNative(this UnityEngine.WrapMode mode)
        {
            switch (mode)
            {
                default:
                case UnityEngine.WrapMode.Default: return WrapMode.Clamp;
                case UnityEngine.WrapMode.Loop: return WrapMode.Loop;
                case UnityEngine.WrapMode.PingPong: return WrapMode.PingPong;
                case UnityEngine.WrapMode.Clamp:
                case UnityEngine.WrapMode.ClampForever: return WrapMode.Clamp;
            }
        }

        public const float OneThird = 1f / 3f;
        public const float TwoThird = 2f / 3f;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ModPlus([NoAlias] in float value, in float range)
        {
            Assert.IsTrue(range > 0);
            var mod = value % range;
            return select(mod + range, mod, mod >= 0);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Approximately([NoAlias] in this float value, in float equals)
        => abs(value - equals) < 1.1921e-07F;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BlobCurve
    {
        public struct CurveHeader
        {
            [NoAlias] public WrapMode WrapModePrev;
            [NoAlias] public WrapMode WrapModePost;
            [NoAlias] public int SegmentCount;
            [NoAlias] public float StartTime;
            [NoAlias] public float EndTime;
            public float Duration => EndTime - StartTime;
            [NoAlias] public BlobArray<float> Times;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            unsafe public int SearchIgnoreWrapMode(in float time, [NoAlias] ref BlobCurveCache cache, [NoAlias] out float t)
            {
                float wrappedTime, duration;
                wrappedTime = clamp(time, StartTime, EndTime);
                bool isPrev = wrappedTime < cache.NeighborhoodTimes.x;
                bool isPost = wrappedTime > cache.NeighborhoodTimes.y;
                if (cache.Index >= 0 & !(isPrev | isPost))
                {
                    duration = cache.NeighborhoodTimes.y - cache.NeighborhoodTimes.x;
                    t = select((wrappedTime - cache.NeighborhoodTimes.x) / duration, 0, duration == 0);
                    return cache.Index;
                }

                float* times = (float*)Times.GetUnsafePtr();
                int lo = 0, hi = SegmentCount - 1;
                cache.Index = clamp(cache.Index, lo, hi);
                int2 neighborhoodIDs = int2(cache.Index - 1, cache.Index + 1);
                float4 neighborhoodTimes = *(float4*)(times + cache.Index);
                isPrev &= neighborhoodTimes.x <= wrappedTime;
                isPost &= wrappedTime <= neighborhoodTimes.w;
                if (isPrev | isPost)
                {
                    cache.NeighborhoodTimes = isPrev ? neighborhoodTimes.xy : neighborhoodTimes.zw;
                    duration = cache.NeighborhoodTimes.y - cache.NeighborhoodTimes.x;
                    t = select((wrappedTime - cache.NeighborhoodTimes.x) / duration, 0, duration == 0);
                    cache.Index = isPrev ? neighborhoodIDs.x : neighborhoodIDs.y;
                    return cache.Index;
                }
                //Unity.Debug.Log($"{InNeighborhood=any(InNeighborhood)} hint={hint} Neighbor={neighbor} time={time} Range={Neighborhood} found={InNeighborhood}");
                bool notFound = true;
                do
                {
                    cache.NeighborhoodTimes = *(float2*)(times + (cache.Index + 1));
                    var go_lo = wrappedTime < cache.NeighborhoodTimes.x;
                    var go_hi = wrappedTime > cache.NeighborhoodTimes.y;
                    notFound = go_lo | go_hi;
                    lo = math.select(lo, (int)(cache.Index + 1), go_hi);
                    hi = math.select(hi, (int)(cache.Index - 1), go_lo);
                    cache.Index = math.select((int)cache.Index, lo + ((hi - lo) >> 1), notFound);
                }
                while (notFound & (lo <= hi));
                duration = cache.NeighborhoodTimes.y - cache.NeighborhoodTimes.x;
                t = select((wrappedTime - cache.NeighborhoodTimes.x) / duration, 0, duration.Approximately(0));
                return cache.Index;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            unsafe public int SearchIgnoreWrapMode(in float time, [NoAlias] out float t)
            {
                float wrappedTime, duration;
                wrappedTime = clamp(time, StartTime, EndTime);
                float* times = (float*)Times.GetUnsafePtr();
                float2 timeRange = *(float2*)(times + 1);
                if (wrappedTime <= timeRange.y)
                {
                    duration = timeRange.y - timeRange.x;
                    t = select((wrappedTime - timeRange.x) / duration, 0, duration == 0);
                    return 0;
                }
                int lo = 0, hi = SegmentCount - 1, i = 0;
                //Unity.Debug.Log($"{InNeighborhood=any(InNeighborhood)} hint={hint} Neighbor={neighbor} time={time} Range={Neighborhood} found={InNeighborhood}");
                bool notFound = true;
                do
                {
                    timeRange = *(float2*)(times + (i + 1));
                    var go_lo = wrappedTime < timeRange.x;
                    var go_hi = wrappedTime > timeRange.y;
                    notFound = go_lo | go_hi;
                    lo = math.select(lo, i + 1, go_hi);
                    hi = math.select(hi, i - 1, go_lo);
                    i = math.select(i, lo + ((hi - lo) >> 1), notFound);
                }
                while (notFound & (lo <= hi));
                duration = timeRange.y - timeRange.x;
                t = select((wrappedTime - timeRange.x) / duration, 0, duration == 0);
                return i;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            unsafe public int Search(in float time, [NoAlias] ref BlobCurveCache cache, [NoAlias] out float t)
            {
                float wrappedTime, duration;
                bool preClamp = WrapModePrev == WrapMode.Clamp;
                bool postClamp = WrapModePost == WrapMode.Clamp;
                if (preClamp & postClamp) wrappedTime = clamp(time, StartTime, EndTime);
                else
                {
                    bool left = time < StartTime;
                    bool right = time > EndTime;
                    if (left | right)
                    {
                        var wrapMode = left ? WrapModePrev : WrapModePost;
                        switch (wrapMode)
                        {
                            default:
                            case WrapMode.Clamp: wrappedTime = left ? StartTime : EndTime; break;
                            case WrapMode.Loop: wrappedTime = ModPlus(time - StartTime, Duration) + StartTime; break;
                            case WrapMode.PingPong:
                                duration = Duration;
                                var offset = ModPlus(time - StartTime, duration);
                                int loopCounter = (int)(floor((time - StartTime) / Duration));
                                bool isMirror = (loopCounter & 1) == 1;
                                wrappedTime = StartTime + (isMirror ? Duration - offset : offset);
                                break;
                        }
                    }
                    else wrappedTime = time;
                }

                bool isPrev = wrappedTime < cache.NeighborhoodTimes.x;
                bool isPost = wrappedTime > cache.NeighborhoodTimes.y;
                if (cache.Index >= 0 & !(isPrev | isPost))
                {
                    duration = cache.NeighborhoodTimes.y - cache.NeighborhoodTimes.x;
                    t = select((wrappedTime - cache.NeighborhoodTimes.x) / duration, 0, duration == 0);
                    return cache.Index;
                }

                float* times = (float*)Times.GetUnsafePtr();
                int lo = 0, hi = SegmentCount - 1;
                cache.Index = clamp(cache.Index, lo, hi);
                int2 neighborhoodIDs = int2(cache.Index - 1, cache.Index + 1);
                float4 neighborhoodTimes = *(float4*)(times + cache.Index);
                isPrev &= neighborhoodTimes.x <= wrappedTime;
                isPost &= wrappedTime <= neighborhoodTimes.w;
                if (isPrev | isPost)
                {
                    cache.NeighborhoodTimes = isPrev ? neighborhoodTimes.xy : neighborhoodTimes.zw;
                    duration = cache.NeighborhoodTimes.y - cache.NeighborhoodTimes.x;
                    t = select((wrappedTime - cache.NeighborhoodTimes.x) / duration, 0, duration == 0);
                    cache.Index = isPrev ? neighborhoodIDs.x : neighborhoodIDs.y;
                    return cache.Index;
                }
                //Unity.Debug.Log($"{InNeighborhood=any(InNeighborhood)} hint={hint} Neighbor={neighbor} time={time} Range={Neighborhood} found={InNeighborhood}");
                bool notFound = true;
                do
                {
                    cache.NeighborhoodTimes = *(float2*)(times + (cache.Index + 1));
                    var go_lo = wrappedTime < cache.NeighborhoodTimes.x;
                    var go_hi = wrappedTime > cache.NeighborhoodTimes.y;
                    notFound = go_lo | go_hi;
                    lo = math.select(lo, cache.Index + 1, go_hi);
                    hi = math.select(hi, cache.Index - 1, go_lo);
                    cache.Index = math.select(cache.Index, lo + ((hi - lo) >> 1), notFound);
                }
                while (notFound & (lo <= hi));
                duration = cache.NeighborhoodTimes.y - cache.NeighborhoodTimes.x;
                t = select((wrappedTime - cache.NeighborhoodTimes.x) / duration, 0, duration == 0);
                return cache.Index;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            unsafe public int Search(in float time, [NoAlias] out float t)
            {
                float wrappedTime, duration;
                bool preClamp = WrapModePrev == WrapMode.Clamp;
                bool postClamp = WrapModePost == WrapMode.Clamp;
                if (preClamp & postClamp) wrappedTime = clamp(time, StartTime, EndTime);
                else
                {
                    bool left = time < StartTime;
                    bool right = time > EndTime;
                    if (left | right)
                    {
                        var wrapMode = left ? WrapModePrev : WrapModePost;
                        switch (wrapMode)
                        {
                            default:
                            case WrapMode.Clamp: wrappedTime = left ? StartTime : EndTime; break;
                            case WrapMode.Loop: wrappedTime = ModPlus(time - StartTime, Duration) + StartTime; break;
                            case WrapMode.PingPong:
                                duration = Duration;
                                var offset = ModPlus(time - StartTime, duration);
                                int loopCounter = (int)(floor((time - StartTime) / Duration));
                                bool isMirror = (loopCounter & 1) == 1;
                                wrappedTime = StartTime + (isMirror ? Duration - offset : offset);
                                break;
                        }
                    }
                    else wrappedTime = time;
                }
                float* times = (float*)Times.GetUnsafePtr();
                float2 timeRange = *(float2*)(times + 1);
                if (wrappedTime <= timeRange.y)
                {
                    duration = timeRange.y - timeRange.x;
                    t = select((wrappedTime - timeRange.x) / duration, 0, duration == 0);
                    return 0;
                }
                int lo = 0, hi = SegmentCount - 1, i = 0;
                //Unity.Debug.Log($"{InNeighborhood=any(InNeighborhood)} hint={hint} Neighbor={neighbor} time={time} Range={Neighborhood} found={InNeighborhood}");
                bool notFound = true;
                do
                {
                    timeRange = *(float2*)(times + (i + 1));
                    var go_lo = wrappedTime < timeRange.x;
                    var go_hi = wrappedTime > timeRange.y;
                    notFound = go_lo | go_hi;
                    lo = math.select(lo, i + 1, go_hi);
                    hi = math.select(hi, i - 1, go_lo);
                    i = math.select(i, lo + ((hi - lo) >> 1), notFound);
                }
                while (notFound & (lo <= hi));
                duration = timeRange.y - timeRange.x;
                t = select((wrappedTime - timeRange.x) / duration, 0, duration == 0);
                return i;
            }

        }

        internal CurveHeader m_Header;
        public BlobArray<BlobCurveSegment> Segments;

        unsafe public ref CurveHeader Header => ref UnsafeUtility.AsRef<CurveHeader>(UnsafeUtility.AddressOf(ref this.m_Header));
        unsafe public ref BlobArray<float> Times => ref UnsafeUtility.AsRef<BlobArray<float>>(UnsafeUtility.AddressOf(ref this.m_Header.Times));
        public WrapMode WrapModePrev => m_Header.WrapModePrev;
        public WrapMode WrapModePost => m_Header.WrapModePost;
        public int SegmentCount => m_Header.SegmentCount;
        public float StartTime => m_Header.StartTime;
        public float EndTime => m_Header.EndTime;
        public float Duration => m_Header.Duration;


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        unsafe public float EvaluateIgnoreWrapMode(in float time, [NoAlias] ref BlobCurveCache cache)
        {
            var i = m_Header.SearchIgnoreWrapMode(time, ref cache, out var t);
            return Segments[i].Sample(PowerSerial(t));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        unsafe public float EvaluateIgnoreWrapMode(in float time)
        {
            var i = m_Header.SearchIgnoreWrapMode(time, out var t);
            return Segments[i].Sample(PowerSerial(t));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        unsafe public float Evaluate(in float time, [NoAlias] ref BlobCurveCache cache)
        {
            var i = m_Header.Search(time, ref cache, out var t);
            return Segments[i].Sample(PowerSerial(t));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Evaluate(in float time)
        {
            var i = m_Header.Search(time, out var t);
            return Segments[i].Sample(PowerSerial(t));
        }
        
        public bool IsEmpty() {
            return Segments.Length == 0;
        }

        unsafe public static BlobAssetReference<BlobCurve> Create(AnimationCurve curve, Allocator allocator = Allocator.Persistent)
        {
            InputCurveCheck(curve);
            var builder = new BlobBuilder(Allocator.Temp);
            ref var data = ref builder.ConstructRoot<BlobCurve>();
            CreateAt(ref data, curve, builder);
            return builder.CreateBlobAssetReference<BlobCurve>(allocator);
        }

        public static void CreateAt(ref BlobCurve data, AnimationCurve curve, BlobBuilder builder)
        {
            var keys = curve.keys;
            int keyFrameCount = keys.Length;
            bool hasOnlyOneKeyframe = keyFrameCount == 1;
            int segmentCount = math.select(keyFrameCount - 1, 1, hasOnlyOneKeyframe);
            
            data.m_Header.SegmentCount = segmentCount;
            data.m_Header.WrapModePrev = curve.preWrapMode.ToNative();
            data.m_Header.WrapModePost = curve.postWrapMode.ToNative();

            if (hasOnlyOneKeyframe)
            {
                var key0 = keys[0];
                var timeBuilder = builder.Allocate(ref data.m_Header.Times, 4);
                timeBuilder[0] = timeBuilder[1] = timeBuilder[2] = timeBuilder[3] = key0.time;
                builder.Allocate(ref data.Segments, 1)[0] = new BlobCurveSegment(key0, key0);
            }
            else
            {
                var timeBuilder = builder.Allocate(ref data.m_Header.Times, keyFrameCount + 2);
                var segBuilder = builder.Allocate(ref data.Segments, segmentCount);
                for (int i = 0, j = 1; i < segmentCount; i = j++)
                {
                    var keyI = keys[i];
                    timeBuilder[j] = keyI.time;
                    segBuilder[i] = new BlobCurveSegment(keyI, keys[j]);
                }
                data.m_Header.StartTime = keys[0].time;
                data.m_Header.EndTime = timeBuilder[keyFrameCount] = keys[segmentCount].time;
                timeBuilder[0] = float.MaxValue;
                timeBuilder[keyFrameCount + 1] = float.MinValue;
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void InputCurveCheck(AnimationCurve curve)
        {
            if (curve == null) throw new NullReferenceException("Input curve is null");
            if (curve.length == 0) throw new ArgumentException("Input curve is empty (no keyframe)");
            var keys = curve.keys;
            for (int i = 0, len = keys.Length; i < len; i++)
            {
                var k = keys[i];
                if (k.weightedMode != WeightedMode.None)
                { Debug.LogWarning($"Weight Not Supported! Key[{i},Weight[{k.weightedMode},In{k.inWeight},Out{k.outWeight}],Time{k.time},Value{k.value}]"); }
            }
        }

    }

    /// <summary>
    /// AnimationCurveSegment using Cubic Bezier spline
    /// </summary>
    public struct BlobCurveSegment
    {
        public float4 Factors;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Sample(in float4 timeSerial) => dot(Factors, timeSerial);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float4 PowerSerial(in float t)
        {
            float sq = t * t;
            return float4(sq * t, sq, t, 1);
        }

        public static readonly float4x4 S_HermiteMat = float4x4
        (// v0  m0  m1  v1
            02, 01, 01, -2, //t^3
            -3, -2, -1, 03, //t^2
            00, 01, 00, 00, //t^1
            01, 00, 00, 00  //t^0
        );

        public static readonly float4x4 S_BezierMat = float4x4
        (// p0  p1  p2  p3
            -1, 03, -3, 01, //t^3
            03, -6, 03, 00, //t^2
            -3, 03, 00, 00, //t^1
            01, 00, 00, 00  //t^0
        );

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float4 UnityFactor(float v0, float t0, float t1, float v1, float duration)
        => select(HermiteFactor(v0, t0 * duration, t1 * duration, v1), BezierFactor(v0, v0, v0, v0), isinf(t0) | isinf(t1));


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float4 HermiteFactor(float v0, float m0, float m1, float v1)
        => mul(S_HermiteMat, float4(v0, m0, m1, v1));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float4 BezierFactor(float p0, float p1, float p2, float p3)
        => mul(S_BezierMat, float4(p0, p1, p2, p3));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float4 LinearFactor(float p0, float p3)
        {
            float offset = (p3 - p0) * OneThird;
            return BezierFactor(p0, p0 + offset, p3 - offset, p3);
        }

        /// <summary>
        /// Create from scratch
        /// </summary>
        public BlobCurveSegment(float4 factors) { Factors = factors; }

        /// <summary>
        /// Convert From Keyframe Pair
        public BlobCurveSegment(Keyframe k0, Keyframe k1) => this = Unity(k0.value, k0.outTangent, k1.inTangent, k1.value, k1.time - k0.time);

        /// <summary>
        /// Convert From UnityEngine.AnimationCurve parameter
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BlobCurveSegment Unity(float v0, float tangent0, float tangent1, float v1, float duration)
        => new BlobCurveSegment(UnityFactor(v0, tangent0, tangent1, v1, duration));

        /// <summary>
        /// Convert From Cubic Hermite spline parameter
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BlobCurveSegment Hermite(float v0, float m0, float m1, float v1)
        => new BlobCurveSegment(HermiteFactor(v0, m0, m1, v1));

        /// <summary>
        /// Convert From Cubic Bezier spline parameter
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BlobCurveSegment Bezier(float p0, float p1, float p2, float p3)
        => new BlobCurveSegment(BezierFactor(p0, p1, p2, p3));

        /// <summary>
        /// Convert Linear Curve
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BlobCurveSegment Linear(float p0, float p3)
        => new BlobCurveSegment(LinearFactor(p0, p3));
    }
}
