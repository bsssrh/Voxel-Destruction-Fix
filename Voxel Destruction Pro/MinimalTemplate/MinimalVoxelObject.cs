using UnityEngine;

namespace VoxelDestructionPro.Minimal
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class MinimalVoxelObject : MonoBehaviour
    {
        [SerializeField] private float voxelSize = 1f;

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private MinimalVoxelData voxelData;

        public MinimalVoxelData VoxelData => voxelData;

        private void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
        }

        public void AssignVoxelData(MinimalVoxelData data)
        {
            voxelData = data;
            RebuildMesh();
        }

        public void RebuildMesh()
        {
            if (meshFilter == null)
                meshFilter = GetComponent<MeshFilter>();

            if (meshRenderer == null)
                meshRenderer = GetComponent<MeshRenderer>();

            if (voxelData == null)
            {
                meshFilter.sharedMesh = null;
                return;
            }

            Mesh mesh = MinimalMesher.BuildMesh(voxelData, voxelSize);
            meshFilter.sharedMesh = mesh;
        }
    }
}
