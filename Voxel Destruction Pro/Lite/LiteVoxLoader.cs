using System.IO;
using UnityEngine;
using VoxelDestructionPro.VoxDataProviders;
using VoxelDestructionPro.VoxelObjects;

using VoxelDestructionPro;

namespace VoxelDestructionPro.Lite
{
    /// <summary>
    /// Lite loader: creates a DynamicVoxelObj + MeshCollider and lets VoxFileDataProvider load the .vox data.
    /// </summary>
    public class LiteVoxLoader : MonoBehaviour
    {
        [Tooltip("Path inside StreamingAssets without extension (e.g. Models/ship).")]
        public string modelPath;

        [Tooltip("Model index inside the .vox file.")]
        public int modelIndex;

        [Tooltip("Optional Lite manager override. If null, LiteVoxelManager.Instance is used.")]
        public LiteVoxelManager manager;

        private void Start()
        {
            Load();
        }

        public void Load()
        {
            LiteVoxelManager activeManager = manager != null ? manager : LiteVoxelManager.Instance;
            if (activeManager == null)
            {
                Debug.LogError("[LiteVoxLoader] No LiteVoxelManager found in the scene.");
                return;
            }

            string objectName = string.IsNullOrWhiteSpace(modelPath)
                ? "Lite Voxel"
                : Path.GetFileNameWithoutExtension(modelPath);

            GameObject voxRoot = new GameObject(objectName);
            voxRoot.transform.SetParent(transform, false);

            var dynamicVoxel = voxRoot.AddComponent<DynamicVoxelObj>();
            dynamicVoxel.meshSettings = activeManager.MeshSettings;
            dynamicVoxel.dynamicSettings = activeManager.DynamicSettings;
            dynamicVoxel.isoSettings = activeManager.IsolationSettings;

            GameObject meshObject = new GameObject("Voxel Mesh");
            meshObject.transform.SetParent(voxRoot.transform, false);

            var meshFilter = meshObject.AddComponent<MeshFilter>();
            var meshRenderer = meshObject.AddComponent<MeshRenderer>();
            var meshCollider = meshObject.AddComponent<MeshCollider>();

            meshRenderer.material = activeManager.DefaultMaterial;
            meshCollider.cookingOptions = MeshColliderCookingOptions.None;

            dynamicVoxel.targetFilter = meshFilter;
            dynamicVoxel.targetCollider = meshCollider;

            var provider = voxRoot.AddComponent<VoxFileDataProvider>();
            provider.modelPath = modelPath;
            provider.modelIndex = Mathf.Max(0, modelIndex);
            provider.useModelCaching = FindObjectOfType<VoxelManager>() != null;
        }
    }
}
