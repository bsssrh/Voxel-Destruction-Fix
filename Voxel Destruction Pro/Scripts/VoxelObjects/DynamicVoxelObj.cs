using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Profiling;
using Unity.Mathematics;
using VoxelDestructionPro;
using VoxelDestructionPro.Data;
using VoxelDestructionPro.Data.Args;
using VoxelDestructionPro.Data.Fragmenter;
using VoxelDestructionPro.Interfaces;
using VoxelDestructionPro.Jobs.Destruction;
using VoxelDestructionPro.Jobs.Fragmenter;
using VoxelDestructionPro.Settings;
using VoxelDestructionPro.Tools;
using VoxelDestructionPro.VoxDataProviders;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VoxelDestructionPro.VoxelObjects
{
    /// <summary>
    /// This voxel object contains the main destruction functions:
    /// AddDestruction, AddDestruction_Sphere, AddDestruction_Cube and AddDestruction_Line
    /// </summary>
    public class DynamicVoxelObj : IsolatedVoxelObj
    {
        [Header("Settings")]
        public DynSettings dynamicSettings;

        [Header("Voxel Material (custom filter)")]
        public VoxelMaterialType voxelMaterialType = VoxelMaterialType.Default;

        [Header("Collider Targets")]
        [Tooltip("Auto-populated colliders used only when CompoundBoxColliderManager is enabled on this object.")]
        public Collider[] targetColliders;

        private readonly List<Transform> colliderTransformBuffer = new List<Transform>();
        private readonly List<Collider> colliderComponentBuffer = new List<Collider>();
        private readonly List<Collider> colliderResults = new List<Collider>();
        private readonly HashSet<int> colliderInstanceIds = new HashSet<int>();

        [Header("Gizmos")]
        [Tooltip("Show a text label for the isolation origin gizmo.")]
        private bool showIsolationOriginLabel = false;

        // =========================
        // ✅ AUTO ISOLATION ORIGIN (STRICT NAME MATCH ONLY)
        // =========================
        [Header("Auto Isolation Origin")]
        [Tooltip("If enabled, isolationOrigin will be auto-assigned ONLY when object name matches known part tokens (Head/Body/la/ra/lf/rf/ll/lll/rl/rll). If name doesn't match, nothing is changed.")]
        public bool autoIsolationOriginByName = true;

        [Tooltip("Debug logs for auto isolation origin.")]
        public bool autoIsolationDebugLog = false;

        private bool _autoIsolationApplied = false;

        // =========================
        // ✅ AUTO-ADD VoxelColorModifier (no hard reference)
        // =========================
        [Header("Auto Add Components")]
        [Tooltip("If enabled, script will automatically add component named 'VoxelColorModifier' if missing (strict by type name, no namespace dependency).")]
        public bool autoAddVoxelColorModifier = true;

        private bool _autoColorModApplied = false;

        // =========================
        // ✅ Compound collider manager + build version cache
        // =========================
        private CompoundBoxColliderManager compoundColliderManager;
        private int _lastCompoundBuildVersion = -1;

        // Active states
        protected bool destructionActive;
        protected bool fragmenterActive;
        protected bool fragmentProcessingActive;

        private IDestructor destructor;
        private IFragmenter fragmenter;

        [HideInInspector]
        public Vector3 lastDestructionPoint;

        // Pending material filter for current destruction call
        private IEnumerable<VoxelMaterialType> pendingAffectedMaterials;
        private Dictionary<int, Color> pendingFragmentColors;

        // Events
        public EventHandler<VoxDestructionEventArgs> onVoxelDestruction;
        public Action<NativeList<int>> onVoxelsRemoved;
        public Action<NativeList<int>> onBeforeVoxelsRemoved;
        public Action<GameObject> onFragmentSpawned;

        // =========================
        // ✅ Ensure helper component exists (once)
        // =========================
        private void EnsureVoxelColorModifierOnce()
        {
            if (_autoColorModApplied) return;
            _autoColorModApplied = true;

            if (!autoAddVoxelColorModifier) return;

            // already exists?
            var existing = GetComponent("VoxelColorModifier");
            if (existing != null) return;

            // find type by Name across assemblies (safe if namespace differs)
            Type t = null;
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                var asm = assemblies[i];
                try
                {
                    var types = asm.GetTypes();
                    for (int k = 0; k < types.Length; k++)
                    {
                        if (types[k] != null && types[k].Name == "VoxelColorModifier")
                        {
                            t = types[k];
                            break;
                        }
                    }
                }
                catch
                {
                    // some assemblies may throw on GetTypes()
                }

                if (t != null) break;
            }

            if (t == null)
            {
                if (autoIsolationDebugLog)
                    Debug.LogWarning("[DynamicVoxelObj] autoAddVoxelColorModifier ON, but type 'VoxelColorModifier' was not found in assemblies.", this);
                return;
            }

            gameObject.AddComponent(t);

            if (autoIsolationDebugLog)
                Debug.Log("[DynamicVoxelObj] Added VoxelColorModifier automatically.", this);
        }

        // =========================
        // ✅ Apply auto isolation safely (strict match only)
        // =========================
        private void ApplyAutoIsolationOriginOnce()
        {
            if (_autoIsolationApplied) return;
            _autoIsolationApplied = true;

            if (!autoIsolationOriginByName)
                return;

            if (!TryComputeIsolationOriginFromNameAndParents(this.transform, out var origin))
            {
                // ✅ If no match -> DO NOTHING (keep inspector value)
                if (autoIsolationDebugLog)
                    Debug.Log($"[DynamicVoxelObj] Auto isolation skipped for '{name}' (no strict name match).", this);
                return;
            }

            isolationOrigin = origin;

            if (autoIsolationDebugLog)
                Debug.Log($"[DynamicVoxelObj] Auto isolationOrigin for '{name}' => {origin}", this);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying) return;

            // reset one-shot flags so changes apply in editor immediately
            _autoColorModApplied = false;
            _autoIsolationApplied = false;

            EnsureVoxelColorModifierOnce();
            ApplyAutoIsolationOriginOnce();

            // reset collider cache in editor when values change
            _lastCompoundBuildVersion = -1;
        }
#endif

        private static bool TryComputeIsolationOriginFromNameAndParents(
            Transform self,
            out IsoSettings.IsolationOrigin origin)
        {
            origin = IsoSettings.IsolationOrigin.None;
            if (self == null) return false;

            // IMPORTANT:
            // - NO name transformations.
            // - Only exceptions: "Head" and "Body" are capitalized.
            // - All others are already lowercase: la/ra/lf/rf/ll/lll/rl/rll
            string n = self.name;

            // Head
            if (n == "Head")
            {
                origin = IsoSettings.IsolationOrigin.ZNeg;
                return true;
            }

            // Body (center)
            if (n == "Body")
            {
                origin = IsoSettings.IsolationOrigin.None;
                return true;
            }

            // Legs
            if (n == "ll" || n == "lll" || n == "rl" || n == "rll")
            {
                origin = IsoSettings.IsolationOrigin.ZPos;
                return true;
            }

            // Arms
            if (n == "la" || n == "ra")
            {
                origin = IsoSettings.IsolationOrigin.XPos;
                return true;
            }

            // Ambiguous: lf / rf (forearm OR foot)
            if (n == "lf" || n == "rf")
            {
                // if under legs -> foot
                if (HasParentNamed(self, "ll", "lll", "rl", "rll"))
                {
                    origin = IsoSettings.IsolationOrigin.ZPos;
                    return true;
                }

                // if under arms -> forearm
                if (HasParentNamed(self, "la", "ra"))
                {
                    origin = IsoSettings.IsolationOrigin.XPos;
                    return true;
                }

                // default if unclear (still a strict match)
                origin = IsoSettings.IsolationOrigin.XPos;
                return true;
            }

            // ✅ name not in allowed list -> no match
            return false;
        }

        private static bool HasParentNamed(Transform t, params string[] exactNames)
        {
            if (t == null) return false;

            Transform p = t.parent;
            while (p != null)
            {
                string pn = p.name;
                for (int i = 0; i < exactNames.Length; i++)
                {
                    if (pn == exactNames[i])
                        return true;
                }
                p = p.parent;
            }
            return false;
        }

        protected override void CreateJobs()
        {
            base.CreateJobs();

            destructor ??= new VoxelDestructor(voxelData.length);

            if (dynamicSettings.destructionMode == DynSettings.DestructionMode.SingleFragment)
                fragmenter ??= new SingleFragmenter(voxelData);
            else if (dynamicSettings.destructionMode == DynSettings.DestructionMode.SphereBasedFragments)
                fragmenter ??= new SphereFragmenter(voxelData);
            else if (dynamicSettings.destructionMode == DynSettings.DestructionMode.VoxelFragment)
                fragmenter ??= new VoxelFragmenter(voxelData);
        }

        private void OnDrawGizmos()
        {
            DrawIsolationGizmo();
        }

        private void DrawIsolationGizmo()
        {
            Vector3 size = GetGizmoSize();
            if (size == Vector3.zero)
                return;

            Vector3 center = size * 0.5f;
            Vector3 localPoint = GetIsolationLocalPoint(size, center);

            Transform meshTransform = targetFilter != null ? targetFilter.transform : transform;
            Vector3 worldPoint = meshTransform.TransformPoint(localPoint);
            float gizmoSize = Mathf.Max(0.01f, 0.03f);

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(worldPoint, gizmoSize);
            Gizmos.DrawLine(meshTransform.position, worldPoint);
        }

        private Vector3 GetIsolationLocalPoint(Vector3 size, Vector3 center)
        {
            return isolationOrigin switch
            {
                IsoSettings.IsolationOrigin.XPos => new Vector3(size.x, center.y, center.z),
                IsoSettings.IsolationOrigin.XNeg => new Vector3(0f, center.y, center.z),
                IsoSettings.IsolationOrigin.YPos => new Vector3(center.x, size.y, center.z),
                IsoSettings.IsolationOrigin.YNeg => new Vector3(center.x, 0f, center.z),
                IsoSettings.IsolationOrigin.ZPos => new Vector3(center.x, center.y, size.z),
                IsoSettings.IsolationOrigin.ZNeg => new Vector3(center.x, center.y, 0f),
                _ => center
            };
        }

        private float GetGizmoVoxelSize()
        {
            if (voxelData != null)
                return GetSingleVoxelSize();

            return objectScale > 0f ? objectScale : 1f;
        }

        private Vector3 GetGizmoSize()
        {
            if (voxelData != null)
                return new Vector3(voxelData.length.x, voxelData.length.y, voxelData.length.z) * GetSingleVoxelSize();

            if (targetFilter != null && targetFilter.sharedMesh != null)
                return targetFilter.sharedMesh.bounds.size;

            return Vector3.Scale(Vector3.one, transform.localScale);
        }

        private void OnDrawGizmosSelected()
        {
            if (isolationOrigin == IsoSettings.IsolationOrigin.None || voxelData == null)
                return;

            float voxelSize = GetSingleVoxelSize();
            Vector3 size = new Vector3(voxelData.length.x, voxelData.length.y, voxelData.length.z) * voxelSize;
            Vector3 center = size * 0.5f;

            Vector3 localPoint = isolationOrigin switch
            {
                IsoSettings.IsolationOrigin.XPos => new Vector3(size.x, center.y, center.z),
                IsoSettings.IsolationOrigin.XNeg => new Vector3(0f, center.y, center.z),
                IsoSettings.IsolationOrigin.YPos => new Vector3(center.x, size.y, center.z),
                IsoSettings.IsolationOrigin.YNeg => new Vector3(center.x, 0f, center.z),
                IsoSettings.IsolationOrigin.ZPos => new Vector3(center.x, center.y, size.z),
                IsoSettings.IsolationOrigin.ZNeg => new Vector3(center.x, center.y, 0f),
                _ => center
            };

            Transform meshTransform = targetFilter != null ? targetFilter.transform : transform;
            Vector3 worldPoint = meshTransform.TransformPoint(localPoint);
            float gizmoSize = Mathf.Max(0.01f, voxelSize * 0.5f);

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(worldPoint, gizmoSize);
            Gizmos.DrawLine(meshTransform.position, worldPoint);
        }

        #region DestructionCalls

        public bool AddDestruction(DestructionData data, object fragmenterSettings = null)
        {
            if (destructionActive || fragmenterActive || fragmentProcessingActive || !isValidObject || !data.IsValidData())
            {
                pendingAffectedMaterials = null;
                return false;
            }

            // ✅ Custom material filtering
            if (!IsMaterialAllowed(pendingAffectedMaterials))
            {
                pendingAffectedMaterials = null;
                return false;
            }

            var args = new VoxDestructionEventArgs();
            args.DestructionDate = data;

            if (onVoxelDestruction != null)
            {
                onVoxelDestruction.Invoke(this, args);
                if (args.BlockDestruction)
                {
                    pendingAffectedMaterials = null;
                    return false;
                }
            }

            lastDestructionPoint = data.start;

            lockIsolatorRun = true;
            lockIsolatorRebuild = true;
            destructionActive = true;

            if (isActiveAndEnabled)
                StartCoroutine(_AddDestruction(data, fragmenterSettings));
            else
                pendingAffectedMaterials = null;

            return true;
        }

        public bool AddDestruction_Sphere(
            Vector3 position,
            float sphereRadius,
            IEnumerable<VoxelMaterialType> affectedMaterials = null,
            object fragmenterSettings = null)
        {
            pendingAffectedMaterials = affectedMaterials;

            DestructionData data = new DestructionData(
                DestructionData.DestructionType.Sphere,
                position,
                Vector3.zero,
                sphereRadius
            );

            return AddDestruction(data, fragmenterSettings);
        }

        public bool AddDestruction_Cube(
            Vector3 position,
            float cubeHalfExtends,
            IEnumerable<VoxelMaterialType> affectedMaterials = null,
            object fragmenterSettings = null)
        {
            pendingAffectedMaterials = affectedMaterials;

            DestructionData data = new DestructionData(
                DestructionData.DestructionType.Cube,
                position,
                Vector3.zero,
                cubeHalfExtends
            );

            return AddDestruction(data, fragmenterSettings);
        }

        public bool AddDestruction_Line(
            Vector3 start,
            Vector3 end,
            float radius,
            IEnumerable<VoxelMaterialType> affectedMaterials = null,
            object fragmenterSettings = null)
        {
            pendingAffectedMaterials = affectedMaterials;

            DestructionData data = new DestructionData(
                DestructionData.DestructionType.Line,
                start,
                end,
                radius
            );

            return AddDestruction(data, fragmenterSettings);
        }

        private bool IsMaterialAllowed(IEnumerable<VoxelMaterialType> allowedList)
        {
            if (allowedList == null)
                return true;

            foreach (var t in allowedList)
            {
                if (t == voxelMaterialType)
                    return true;
            }
            return false;
        }

        #endregion

        private IEnumerator _AddDestruction(DestructionData data, object fragmenterSettings)
        {
            data.start = targetFilter.transform.InverseTransformPoint(data.start) / GetSingleVoxelSize();
            if (data.destructionType == DestructionData.DestructionType.Line)
                data.end = targetFilter.transform.InverseTransformPoint(data.end) / GetSingleVoxelSize();

            destructor.Prepare(data);

            while (true)
            {
                if (destructor.isFinished())
                    break;

                yield return null;
            }

            NativeList<int> voxelIndex = destructor.GetData();
            NativeArray<int> voxelIndexArray = voxelIndex.AsArray();
            if (voxelIndexArray.Length > 1)
                voxelIndexArray.Sort();

            if (dynamicSettings.destructionMode == DynSettings.DestructionMode.Remove)
            {
                if (!voxelData.ActiveCountLarger(voxelIndex.Length))
                {
                    objectDestructionRequested = true;
                    destructionActive = false;
                    pendingAffectedMaterials = null;
                    yield break;
                }
            }
            else if (dynamicSettings.destructionMode == DynSettings.DestructionMode.SphereBasedFragments)
            {
                int sphereMin;

                if (fragmenterSettings is SphereFragmenterData sfd)
                    sphereMin = sfd.minSphereRadius;
                else
                    sphereMin = dynamicSettings.defaultSphereSettings.minSphereRadius;

                if (!voxelData.ActiveCountLarger(sphereMin) && voxelIndex.Length > sphereMin)
                {
                    objectDestructionRequested = true;
                    destructionActive = false;
                    pendingAffectedMaterials = null;
                    yield break;
                }
            }

            if (dynamicSettings.destructionMode != DynSettings.DestructionMode.Remove)
            {
                if (dynamicSettings.destructionMode == DynSettings.DestructionMode.SphereBasedFragments &&
                    fragmenterSettings is not SphereFragmenterData)
                    fragmenterSettings = dynamicSettings.defaultSphereSettings;

                if (dynamicSettings.destructionMode == DynSettings.DestructionMode.VoxelFragment &&
                    fragmenterSettings is not VoxelFragmenterData)
                    fragmenterSettings = dynamicSettings.defaultVoxelSettings;

                onBeforeVoxelsRemoved?.Invoke(voxelIndex);

                pendingFragmentColors = CacheFragmentColors(voxelIndex);
                fragmenter.StartFragmenting(voxelData, voxelIndex, fragmenterSettings);
                fragmenterActive = true;
            }
            else
            {
                pendingFragmentColors = null;
            }

            ClearVoxelsSorted(voxelData.voxels, voxelIndexArray);

            VoxelSlowDebugLogger.Log("AfterRemoveVoxels", name, voxelData);

            onVoxelsRemoved?.Invoke(voxelIndex);

            RequestCompoundColliderRebuild();

            destructionActive = false;
            lockIsolatorRun = false;
            lockIsolatorRebuild = false;

            pendingAffectedMaterials = null;

            if (voxelIndex.Length > 0)
            {
                RequestMeshRegeneration();

                if (isoSettings.isolationMode != IsoSettings.IsolationMode.None)
                    isolatorRequested = true;
            }
        }

        private static void ClearVoxelsSorted(NativeArray<Voxel> voxels, NativeArray<int> sortedIndices)
        {
            if (sortedIndices.Length == 0)
                return;

            Voxel emptyVoxel = Voxel.emptyVoxel;

            int rangeStart = sortedIndices[0];
            int rangeEnd = rangeStart;

            for (int i = 1; i < sortedIndices.Length; i++)
            {
                int current = sortedIndices[i];
                if (current <= rangeEnd + 1)
                {
                    if (current > rangeEnd)
                        rangeEnd = current;
                    continue;
                }

                ClearVoxelRange(voxels, rangeStart, rangeEnd, emptyVoxel);
                rangeStart = current;
                rangeEnd = current;
            }

            ClearVoxelRange(voxels, rangeStart, rangeEnd, emptyVoxel);
        }

        private static void ClearVoxelRange(NativeArray<Voxel> voxels, int start, int end, Voxel emptyVoxel)
        {
            int count = end - start + 1;
            if (count <= 0)
                return;

            int limit = start + count;
            for (int i = start; i < limit; i++)
                voxels[i] = emptyVoxel;
        }

        protected override void Update()
        {
            EnsureVoxelColorModifierOnce();
            ApplyAutoIsolationOriginOnce();

            // ✅ НЕ сканим каждый кадр: только если buildVersion изменился или массив пуст
            RefreshTargetCollidersIfNeeded();

            base.Update();

            if (fragmenterActive && fragmenter.IsFinished())
            {
                fragmenterActive = false;
                fragmentProcessingActive = true;

                StartCoroutine(FinishFragmenting());
            }
        }

        private void RefreshTargetCollidersIfNeeded()
        {
            if (!IsCompoundColliderModeActive())
            {
                targetColliders = null;
                _lastCompoundBuildVersion = -1;
                return;
            }

            compoundColliderManager ??= GetComponent<CompoundBoxColliderManager>();
            if (compoundColliderManager == null)
            {
                targetColliders = null;
                _lastCompoundBuildVersion = -1;
                return;
            }

            int v = compoundColliderManager.BuildVersion;

            bool needRefresh =
                v != _lastCompoundBuildVersion ||
                targetColliders == null ||
                targetColliders.Length == 0;

            if (!needRefresh)
                return;

            _lastCompoundBuildVersion = v;
            RefreshTargetCollidersFromRoot();
        }

        private IEnumerator FinishFragmenting()
        {
            VoxelData[] fragments = fragmenter.CreateFragments(voxelData, out Vector3[] positions);
            VoxelSlowDebugLogger.Log("AfterCreateFragments_Source", name, voxelData);

            if (fragments == null)
            {
                if (positions != null && fragmenter.UseVoxelFragments())
                {
                    int3 length = voxelData.length;
                    HashSet<int> spawnedIndices = new HashSet<int>();

                    for (int i = 0; i < positions.Length; i++)
                    {
                        Vector3 position = positions[i];
                        int x = Mathf.RoundToInt(position.x);
                        int y = Mathf.RoundToInt(position.y);
                        int z = Mathf.RoundToInt(position.z);
                        int index = x + length.x * (y + length.y * z);

                        if (!spawnedIndices.Add(index))
                            continue;

                        GameObject nObj = InstantiateVox(
                            dynamicSettings.voxelPrefab,
                            targetFilter.transform.TransformPoint(position * GetSingleVoxelSize()),
                            targetFilter.transform.rotation
                        );

                        DisableDataProviders(nObj);

                        nObj.transform.parent = fragmentParent;
                        nObj.transform.localScale = GetSingleVoxelSize() * Vector3.one;

                        ApplyFragmentColliderMode(nObj);
                        ApplyVoxelFragmentColor(nObj, positions[i]);

                        var dyn = nObj.GetComponent<DynamicVoxelObj>();
                        if (dyn != null)
                            dyn.voxelMaterialType = voxelMaterialType;

                        onFragmentSpawned?.Invoke(nObj);
                    }
                }

                fragmentProcessingActive = false;
                pendingFragmentColors = null;
                yield break;
            }

            VoxelFragmentGroup fragmentGroup = null;
            Dictionary<int, List<VoxelFragmentGroup.AttachmentData>> attachmentMap = null;
            Vector3[] fragmentWorldPositions = null;

            fragmentGroup = GetComponent<VoxelFragmentGroup>();
            if (fragmentGroup != null && fragmentGroup.HasAttachments())
            {
                if (fragmentGroup.autoCollectChildren)
                    fragmentGroup.RefreshAttachedObjects();

                fragmentWorldPositions = new Vector3[positions.Length];
                for (int p = 0; p < positions.Length; p++)
                {
                    fragmentWorldPositions[p] = targetFilter.transform.TransformPoint(positions[p] * GetSingleVoxelSize());
                }

                attachmentMap = fragmentGroup.BuildAttachmentMap(transform, fragmentWorldPositions);
            }

            for (int i = 0; i < fragments.Length; i++)
            {
                GameObject nObj = InstantiateVox(
                    dynamicSettings.fragmentPrefab,
                    targetFilter.transform.TransformPoint(positions[i] * GetSingleVoxelSize()),
                    targetFilter.transform.rotation
                );

                DisableDataProviders(nObj);

                nObj.transform.parent = fragmentParent;

                ApplyFragmentColliderMode(nObj);

                VoxelObjBase vox = nObj.GetComponent<VoxelObjBase>();

                if (vox != null)
                {
                    vox.scaleType = ScaleType.Voxel;
                    vox.objectScale = GetSingleVoxelSize();

                    if (vox is IsolatedVoxelObj iso)
                        iso.fragmentParent = fragmentParent;

                    vox.AssignVoxelData(fragments[i]);

                    var dyn = nObj.GetComponent<DynamicVoxelObj>();
                    if (dyn != null)
                        dyn.voxelMaterialType = voxelMaterialType;
                }
                else
                {
                    fragments[i].Dispose();
                }

                if (i == 0 && vox != null)
                    VoxelSlowDebugLogger.Log("FragmentAssigned_First", vox.name, vox.voxelData);

                fragmentGroup?.SpawnAttachmentsForFragment(i, nObj.transform, attachmentMap);

                onFragmentSpawned?.Invoke(nObj);
            }

            fragmentProcessingActive = false;
            pendingFragmentColors = null;
            yield break;
        }

        private Dictionary<int, Color> CacheFragmentColors(NativeList<int> voxelIndex)
        {
            if (dynamicSettings.destructionMode != DynSettings.DestructionMode.VoxelFragment)
                return null;

            if (voxelData == null || voxelData.voxels.Length == 0 || voxelData.palette.Length == 0)
                return null;

            var cache = new Dictionary<int, Color>(voxelIndex.Length);
            for (int i = 0; i < voxelIndex.Length; i++)
            {
                int index = voxelIndex[i];
                Voxel voxel = voxelData.voxels[index];
                if (voxel.active == 0)
                    continue;

                cache[index] = voxelData.palette[voxel.color];
            }

            return cache;
        }

        private void ApplyVoxelFragmentColor(GameObject obj, Vector3 position)
        {
            if (obj == null || pendingFragmentColors == null)
                return;

            int3 length = voxelData.length;
            int x = Mathf.RoundToInt(position.x);
            int y = Mathf.RoundToInt(position.y);
            int z = Mathf.RoundToInt(position.z);
            int index = x + length.x * (y + length.y * z);

            if (!pendingFragmentColors.TryGetValue(index, out Color color))
                return;

            VoxelObjBase vox = obj.GetComponent<VoxelObjBase>();
            if (vox == null)
                return;

            Voxel[] voxels = { new Voxel(0, 1) };
            Color[] palette = { color };
            VoxelData fragmentData = new VoxelData(voxels, palette, new int3(1, 1, 1));
            vox.AssignVoxelData(fragmentData);
        }

        private static void DisableDataProviders(GameObject obj)
        {
            if (obj == null)
                return;

            VoxDataProvider[] providers = obj.GetComponents<VoxDataProvider>();
            for (int i = 0; i < providers.Length; i++)
                providers[i].enabled = false;
        }

        public bool IsCompoundColliderModeActive()
        {
            compoundColliderManager ??= GetComponent<CompoundBoxColliderManager>();
            return compoundColliderManager != null &&
                   compoundColliderManager.isActiveAndEnabled &&
                   compoundColliderManager.enabledCompound;
        }

        private void RefreshTargetCollidersFromRoot()
        {
            if (!IsCompoundColliderModeActive())
            {
                targetColliders = null;
                return;
            }

            Transform collidersRoot = ResolveCompoundColliderRoot();
            if (collidersRoot == null)
            {
                targetColliders = Array.Empty<Collider>();
                return;
            }

            colliderTransformBuffer.Clear();
            collidersRoot.GetComponentsInChildren(true, colliderTransformBuffer);

            colliderResults.Clear();
            colliderInstanceIds.Clear();

            for (int i = 0; i < colliderTransformBuffer.Count; i++)
            {
                Transform t = colliderTransformBuffer[i];
                if (t == null)
                    continue;

                colliderComponentBuffer.Clear();
                t.GetComponents(colliderComponentBuffer);

                for (int j = 0; j < colliderComponentBuffer.Count; j++)
                {
                    Collider collider = colliderComponentBuffer[j];
                    if (collider == null || !collider.enabled)
                        continue;

                    int id = collider.GetInstanceID();
                    if (!colliderInstanceIds.Add(id))
                        continue;

                    colliderResults.Add(collider);
                }
            }

            targetColliders = colliderResults.ToArray();
        }

        private Transform ResolveCompoundColliderRoot()
        {
            if (compoundColliderManager == null)
                return null;

            compoundColliderManager.EnsureRoots();

            Transform target = compoundColliderManager.targetFilterOverride != null
                ? compoundColliderManager.targetFilterOverride
                : (targetFilter != null ? targetFilter.transform : transform);

            return target != null ? target.Find("CollidersRoot") : null;
        }

        private void ApplyFragmentColliderMode(GameObject fragmentObj)
        {
            if (fragmentObj == null || dynamicSettings == null)
                return;

            switch (dynamicSettings.fragmentColliderMode)
            {
                case DynSettings.FragmentColliderMode.MeshCollider:
                    DisableCompoundCollider(fragmentObj);
                    EnsureMeshCollider(fragmentObj);
                    break;
                case DynSettings.FragmentColliderMode.CompoundBoxCollider:
                    EnsureCompoundCollider(fragmentObj);
                    DisableMeshCollider(fragmentObj);
                    break;
                case DynSettings.FragmentColliderMode.PrefabDefault:
                default:
                    break;
            }
        }

        private static void DisableCompoundCollider(GameObject fragmentObj)
        {
            CompoundBoxColliderManager compound = fragmentObj.GetComponent<CompoundBoxColliderManager>();
            if (compound != null)
            {
                if (Application.isPlaying)
                    Destroy(compound);
                else
                    DestroyImmediate(compound);
            }

            if (fragmentObj.TryGetComponent(out VoxelObjBase voxelObj))
                voxelObj.SetCompoundColliderManager(null);
        }

        private static void EnsureCompoundCollider(GameObject fragmentObj)
        {
            CompoundBoxColliderManager compound = fragmentObj.GetComponent<CompoundBoxColliderManager>();
            if (compound == null)
                compound = fragmentObj.AddComponent<CompoundBoxColliderManager>();

            if (fragmentObj.TryGetComponent(out VoxelObjBase voxelObj))
                voxelObj.SetCompoundColliderManager(compound);
        }

        private static void EnsureMeshCollider(GameObject fragmentObj)
        {
            if (!fragmentObj.TryGetComponent(out VoxelObjBase voxelObj))
                return;

            Transform target = voxelObj.targetFilter != null ? voxelObj.targetFilter.transform : fragmentObj.transform;
            MeshCollider meshCollider = target.GetComponent<MeshCollider>();
            if (meshCollider == null)
                meshCollider = target.gameObject.AddComponent<MeshCollider>();

            meshCollider.enabled = true;
            voxelObj.targetCollider = meshCollider;
        }

        private static void DisableMeshCollider(GameObject fragmentObj)
        {
            if (!fragmentObj.TryGetComponent(out VoxelObjBase voxelObj))
                return;

            if (voxelObj.targetCollider is MeshCollider targetMeshCollider)
            {
                targetMeshCollider.sharedMesh = null;
                targetMeshCollider.enabled = false;
                voxelObj.targetCollider = null;
                return;
            }

            Transform target = voxelObj.targetFilter != null ? voxelObj.targetFilter.transform : fragmentObj.transform;
            MeshCollider meshCollider = target.GetComponent<MeshCollider>();
            if (meshCollider != null)
            {
                meshCollider.sharedMesh = null;
                meshCollider.enabled = false;
            }

            if (voxelObj.targetCollider == meshCollider)
                voxelObj.targetCollider = null;
        }

        public override void QuickSetup(VoxelManager manager)
        {
            base.QuickSetup(manager);
            dynamicSettings = manager.standardDynamicSettings;
        }

        protected override bool AssertVoxelObject()
        {
            if (dynamicSettings == null)
            {
                Debug.LogError("No dynamic Voxel object settings assigned!");
                return false;
            }

            return base.AssertVoxelObject();
        }

        protected override bool CanDestroyObject()
        {
            if (fragmenterActive)
                return false;

            return base.CanDestroyObject();
        }

        protected override void DisposeAll()
        {
            destructor?.Dispose();
            destructor = null;

            fragmenter?.Dispose();
            fragmenter = null;

            base.DisposeAll();
        }

        protected override void DestroyVoxObj()
        {
            destructionActive = false;
            fragmenterActive = false;
            pendingAffectedMaterials = null;
            base.DestroyVoxObj();
        }
    }
}
