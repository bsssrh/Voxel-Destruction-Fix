using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace VoxelDestructionPro.Jobs.Destruction
{
    [BurstCompile]
    public struct ClearDestructionFlagsJob : IJobFor
    {
        public NativeArray<byte> flags;

        public void Execute(int index)
        {
            flags[index] = 0;
        }
    }

    [BurstCompile]
    public struct GatherDestructionFlagsJob : IJob
    {
        [ReadOnly]
        public NativeArray<byte> flags;

        public NativeList<int> outputIndex;

        public void Execute()
        {
            for (int i = 0; i < flags.Length; i++)
            {
                if (flags[i] != 0)
                {
                    outputIndex.AddNoResize(i);
                }
            }
        }
    }
}
