using UnityEngine;
using VoxelDestructionPro.Settings;

namespace VoxelDestructionPro.Lite
{
    /// <summary>
    /// Minimal manager for Lite examples (supplies defaults for LiteVoxLoader).
    /// </summary>
    public class LiteVoxelManager : MonoBehaviour
    {
        public static LiteVoxelManager Instance { get; private set; }

        [Header("Defaults")]
        [Tooltip("Material assigned to the generated voxel mesh.")]
        public Material defaultMaterial;

        [Tooltip("Mesh settings for Lite-generated voxel objects.")]
        public MeshSettingsObj meshSettings;

        [Tooltip("Dynamic settings for destruction (copied at runtime).")]
        public DynSettings dynamicSettings;

        private IsoSettings runtimeIsoSettings;
        private DynSettings runtimeDynamicSettings;
        private MeshSettingsObj runtimeMeshSettings;

        public Material DefaultMaterial => defaultMaterial;
        public MeshSettingsObj MeshSettings => runtimeMeshSettings;
        public DynSettings DynamicSettings => runtimeDynamicSettings;
        public IsoSettings IsolationSettings => runtimeIsoSettings;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            InitDefaults();
        }

        private void InitDefaults()
        {
            runtimeMeshSettings = meshSettings != null
                ? meshSettings
                : ScriptableObject.CreateInstance<MeshSettingsObj>();

            if (dynamicSettings != null)
            {
                runtimeDynamicSettings = Instantiate(dynamicSettings);
            }
            else
            {
                runtimeDynamicSettings = ScriptableObject.CreateInstance<DynSettings>();
                runtimeDynamicSettings.destructionMode = DynSettings.DestructionMode.Remove;
            }

            runtimeDynamicSettings.fragmentColliderMode = DynSettings.FragmentColliderMode.MeshCollider;

            runtimeIsoSettings = ScriptableObject.CreateInstance<IsoSettings>();
            runtimeIsoSettings.isolationMode = IsoSettings.IsolationMode.None;
            runtimeIsoSettings.runIsolationOnStart = false;
            runtimeIsoSettings.minVoxelCount = 0;
            runtimeIsoSettings.isolationFragmentPrefab = null;
        }
    }
}
