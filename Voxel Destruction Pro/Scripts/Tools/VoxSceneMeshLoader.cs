using UnityEngine;
using VoxelDestructionPro.VoxDataProviders;
using VoxelDestructionPro.VoxelObjects;

namespace VoxelDestructionPro.Tools
{
    [ExecuteAlways]
    public class VoxSceneMeshLoader : MonoBehaviour
    {
        [Min(0)]
        public int lastReloadedCount;

        public void LoadUnloadedMeshes()
        {
            int reloaded = 0;
            bool editorMode = !Application.isPlaying;

            VoxDataProvider[] providers = FindObjectsOfType<VoxDataProvider>(true);
            for (int i = 0; i < providers.Length; i++)
            {
                VoxDataProvider provider = providers[i];
                if (provider == null || !provider.isActiveAndEnabled)
                    continue;

                if (!NeedsMeshReload(provider))
                    continue;

                provider.Load(editorMode);
                reloaded++;
            }

            lastReloadedCount = reloaded;
        }

        private static bool NeedsMeshReload(VoxDataProvider provider)
        {
            VoxelObjBase obj = provider.GetComponent<VoxelObjBase>();
            if (obj == null)
                return false;

            if (obj.targetFilter == null)
                return true;

            return obj.targetFilter.sharedMesh == null;
        }
    }
}
