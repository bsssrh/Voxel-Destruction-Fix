using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoxelDestructionPro.Data;

namespace VoxelDestructionPro.Jobs.CompoundColliders
{
    public struct CollisionGridBuilderJob : IJob
    {
        [ReadOnly] public NativeArray<Voxel> voxels;
        [ReadOnly] public int3 sourceLength;
        [ReadOnly] public int lod;
        [ReadOnly] public int3 gridLength;
        public NativeArray<byte> grid;

        public void Execute()
        {
            int gridIndex = 0;
            for (int z = 0; z < gridLength.z; z++)
            {
                for (int y = 0; y < gridLength.y; y++)
                {
                    for (int x = 0; x < gridLength.x; x++)
                    {
                        grid[gridIndex++] = IsCellSolid(x, y, z) ? (byte)1 : (byte)0;
                    }
                }
            }
        }

        private bool IsCellSolid(int cellX, int cellY, int cellZ)
        {
            int startX = cellX * lod;
            int startY = cellY * lod;
            int startZ = cellZ * lod;

            int endX = math.min(startX + lod, sourceLength.x);
            int endY = math.min(startY + lod, sourceLength.y);
            int endZ = math.min(startZ + lod, sourceLength.z);

            for (int z = startZ; z < endZ; z++)
            {
                for (int y = startY; y < endY; y++)
                {
                    int baseIndex = startX + sourceLength.x * (y + sourceLength.y * z);
                    for (int x = startX; x < endX; x++)
                    {
                        if (voxels[baseIndex + (x - startX)].active > 0)
                            return true;
                    }
                }
            }

            return false;
        }
    }
}
