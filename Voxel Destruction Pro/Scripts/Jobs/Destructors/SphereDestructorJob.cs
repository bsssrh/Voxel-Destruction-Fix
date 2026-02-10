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
    /// Finds all voxels that fall into a defined sphere shape
    /// </summary>
    [BurstCompile]
    public struct SphereDestructorJob : IJobFor
    {
        [NativeDisableParallelForRestriction]
        public NativeArray<byte> flags;

        public float3 targetPoint;
        public float getRadiusSqr;

        public int2 indexLength;
        public int3 minBound;
        public int3 maxBound;
    
        public void Execute(int zOffset)
        {
            int z = minBound.z + zOffset;
            int xLen = indexLength.x;
            int xyLen = xLen * indexLength.y;
            int zBaseIndex = z * xyLen;
            float dz = z - targetPoint.z;
            float dzSqr = dz * dz;

            for (int y = minBound.y; y <= maxBound.y; y++)
            {
                float dy = y - targetPoint.y;
                float dySqr = dy * dy;
                int index = zBaseIndex + y * xLen + minBound.x;
                for (int x = minBound.x; x <= maxBound.x; x++)
                {
                    if (x < minBound.x || x > maxBound.x || y < minBound.y || y > maxBound.y || z < minBound.z || z > maxBound.z)
                    {
                        index++;
                        continue;
                    }

                    float dx = x - targetPoint.x;
                    float distance = dx * dx + dySqr + dzSqr;
                    if (distance <= getRadiusSqr)
                    {
                        flags[index] = 1;
                    }

                    index++;
                }
            }
        }
    }
}
