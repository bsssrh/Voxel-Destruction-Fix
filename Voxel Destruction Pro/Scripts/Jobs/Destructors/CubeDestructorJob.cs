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
    /// Finds all voxels that fall into a defined cube shape
    /// </summary>
    [BurstCompile]
    public struct CubeDestructorJob : IJobFor
    {
        [NativeDisableParallelForRestriction]
        public NativeArray<byte> flags;

        public float3 targetPoint;
        public float cubeHalfExtend;

        public int2 indexLength;
        public int3 minBound;
        public int3 maxBound;
    
        public void Execute(int zOffset)
        {
            int z = minBound.z + zOffset;
            int xLen = indexLength.x;
            int xyLen = xLen * indexLength.y;
            int zBaseIndex = z * xyLen;
            float dz = math.abs(z - targetPoint.z);

            for (int y = minBound.y; y <= maxBound.y; y++)
            {
                float dy = math.abs(y - targetPoint.y);
                int index = zBaseIndex + y * xLen + minBound.x;
                for (int x = minBound.x; x <= maxBound.x; x++)
                {
                    if (x < minBound.x || x > maxBound.x || y < minBound.y || y > maxBound.y || z < minBound.z || z > maxBound.z)
                    {
                        index++;
                        continue;
                    }

                    float dx = math.abs(x - targetPoint.x);
                    if (dx <= cubeHalfExtend &&
                        dy <= cubeHalfExtend &&
                        dz <= cubeHalfExtend)
                    {
                        flags[index] = 1;
                    }

                    index++;
                }
            }
        }
    }
}
