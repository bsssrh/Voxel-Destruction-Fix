using UnityEngine;

namespace VoxelDestructionPro.Interfaces
{
    public interface IVoxelMeshReady
    {
        void OnMeshGenerated(Mesh mesh);
    }
}
