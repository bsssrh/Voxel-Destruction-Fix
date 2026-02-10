using VoxelDestructionPro.Data;

namespace VoxelDestructionPro.Interfaces
{
    public interface IVoxelDataConsumer
    {
        void OnVoxelDataAssigned(VoxelData data);
    }
}
