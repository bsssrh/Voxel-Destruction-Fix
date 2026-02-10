using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace VoxelDestructionPro.Jobs.Destruction
{
    /// <summary>
    /// Finds all voxels that fall into a defined line shape
    /// </summary>
    [BurstCompile]
    public struct LineDestructorJob : IJobFor
    {
        [NativeDisableParallelForRestriction]
        public NativeArray<byte> flags;

        public float3 startPoint;
        public float3 endPoint;
        public float3 lineVec;
        public float radiusSqr;
        public float invLenSqr;

        public int2 indexLength;
        public int3 minBound;
        public int3 maxBound;

        public void Execute(int zOffset)
        {
            int z = minBound.z + zOffset;
            int xLen = indexLength.x;
            int xyLen = xLen * indexLength.y;
            int zBaseIndex = z * xyLen;

            for (int y = minBound.y; y <= maxBound.y; y++)
            {
                int index = zBaseIndex + y * xLen + minBound.x;
                for (int x = minBound.x; x <= maxBound.x; x++)
                {
                    if (x < minBound.x || x > maxBound.x || y < minBound.y || y > maxBound.y || z < minBound.z || z > maxBound.z)
                    {
                        index++;
                        continue;
                    }

                    float3 point = new float3(x, y, z);
                    if (IsPointWithinRadius(point, startPoint, endPoint, radiusSqr))
                    {
                        flags[index] = 1;
                    }

                    index++;
                }
            }
        }

        private bool IsPointWithinRadius(float3 point, float3 start, float3 end, float radiusSqr)
        {
            float3 pointVec = point - start;
            float t = math.dot(pointVec, lineVec) * invLenSqr;

            t = math.clamp(t, 0f, 1f);

            float3 closestPoint = start + t * lineVec;
            float3 diff = point - closestPoint;

            return math.lengthsq(diff) <= radiusSqr;
        }
    }
}
