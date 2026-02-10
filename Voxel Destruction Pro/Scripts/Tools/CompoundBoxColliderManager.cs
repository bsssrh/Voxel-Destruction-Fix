using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VoxelDestructionPro.Data;
using VoxelDestructionPro.Jobs.CompoundColliders;
using VoxelDestructionPro.VoxelObjects;
using VoxelDestructionPro.VoxDataProviders;

namespace VoxelDestructionPro.Tools
{
    [ExecuteAlways]
    public class CompoundBoxColliderManager : MonoBehaviour
    {
        [Header("Compound Colliders")]
        public bool enabledCompound = true;
        public Transform targetFilterOverride;
        public bool editorAutoBake = true;
        public bool runtimeAutoRebuild = true;
        [Min(1)]
        public int collisionLOD = 1;
        [Min(0f)]
        public float rebuildCooldown = 0.15f;
        [Min(1)]
        public int maxColliders = 256;
        [Min(1)]
        public int greedyMinBoxVolume = 1;
        public LayerMask colliderLayer;
        public bool collidersAreTriggers;
        public bool freezeRigidbodyDuringApply = true;
        public bool logProfiling;
        [Min(0)]
        public int minVoxelCountForRuntimeRebuild = 0;

        [Header("Runtime Info")]
        [SerializeField] private int activeColliderCount;
        [SerializeField] private int lastLodUsed = 1;
        [SerializeField] private float lastBuildMs;
        [SerializeField] private float lastApplyMs;

        // ✅ VERSION: меняется каждый раз когда мы реально применили изменения коллайдеров
        [Header("Internal")]
        [SerializeField] private int buildVersion;
        public int BuildVersion => buildVersion;

        private readonly HashSet<ColliderBoxKey> prevKeys = new HashSet<ColliderBoxKey>();
        private readonly Dictionary<ColliderBoxKey, BoxCollider> activeByKey = new Dictionary<ColliderBoxKey, BoxCollider>();
        private readonly Stack<BoxCollider> pool = new Stack<BoxCollider>();
        private readonly List<BoxCollider> allCreated = new List<BoxCollider>();

        private VoxelObjBase voxelObj;
        private Transform collidersRoot;
        private bool dirty;
        private Bounds? dirtyBoundsWorld;
        private float lastRebuildTime;
        private bool editorRebuildScheduled;

        private void Awake()
        {
            voxelObj = GetComponent<VoxelObjBase>();
        }

        private void OnEnable()
        {
            voxelObj ??= GetComponent<VoxelObjBase>();
            EnsureTargetFilterOverride();
            EnsureRoots();

            if (!Application.isPlaying && editorAutoBake)
                ScheduleEditorBake();

            TriggerAutoBakeIfReady();
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= EditorUpdate;
#endif
        }

        private void Update()
        {
            if (!Application.isPlaying || !enabledCompound || !runtimeAutoRebuild)
                return;

            if (dirty && Time.unscaledTime - lastRebuildTime >= rebuildCooldown)
                RebuildNow(false);
        }

#if UNITY_EDITOR
        private void EditorUpdate()
        {
            if (Application.isPlaying || !enabledCompound || !editorAutoBake)
                return;

            if (dirty && UnityEditor.EditorApplication.timeSinceStartup - lastRebuildTime >= rebuildCooldown)
                RebuildNow(false);
        }
#endif

        private void OnValidate()
        {
            collisionLOD = Mathf.Max(1, collisionLOD);
            maxColliders = Mathf.Max(1, maxColliders);
            greedyMinBoxVolume = Mathf.Max(1, greedyMinBoxVolume);

#if UNITY_EDITOR
            if (!Application.isPlaying && editorAutoBake)
                ScheduleEditorBake();
#endif
        }

        public void RequestRebuild(Bounds? dirtyWorldBounds = null)
        {
            if (!enabledCompound)
                return;

            if (ShouldSkipRuntimeRebuild())
                return;

            dirty = true;
            if (dirtyWorldBounds.HasValue)
                dirtyBoundsWorld = dirtyBoundsWorld.HasValue
                    ? Encapsulate(dirtyBoundsWorld.Value, dirtyWorldBounds.Value)
                    : dirtyWorldBounds.Value;

#if UNITY_EDITOR
            if (!Application.isPlaying && editorAutoBake)
                ScheduleEditorBake();
#endif
        }

        public void RebuildNow(bool forceFull = false)
        {
            if (!enabledCompound)
                return;

            if (!forceFull && ShouldSkipRuntimeRebuild())
                return;

            VoxelData data = voxelObj != null ? voxelObj.voxelData : null;
            if (data == null || data.voxels.Length == 0)
            {
                ClearBakedColliders();
                return;
            }

            EnsureRoots();
            if (Application.isPlaying)
                lastRebuildTime = Time.unscaledTime;
#if UNITY_EDITOR
            else
                lastRebuildTime = (float)UnityEditor.EditorApplication.timeSinceStartup;
#endif

            int desiredLod = Mathf.Max(1, collisionLOD);
            List<ColliderBoxKey> keys = BuildColliderKeys(data, desiredLod);
            if (keys.Count > maxColliders)
            {
                int boostedLod = desiredLod * 2;
                if (boostedLod != desiredLod)
                {
                    desiredLod = boostedLod;
                    keys = BuildColliderKeys(data, desiredLod);
                }
            }

            ApplyDiff(keys, desiredLod);

            dirty = false;
            dirtyBoundsWorld = null;
        }

        public void ClearBakedColliders()
        {
            for (int i = allCreated.Count - 1; i >= 0; i--)
            {
                BoxCollider collider = allCreated[i];
                if (collider == null)
                    continue;

                if (Application.isPlaying)
                    Destroy(collider.gameObject);
                else
                    DestroyImmediate(collider.gameObject);
            }

            allCreated.Clear();
            pool.Clear();
            prevKeys.Clear();
            activeByKey.Clear();
            activeColliderCount = 0;

            // ✅ version bump
            buildVersion++;
        }

#if UNITY_EDITOR
        public void BakeInEditor()
        {
            if (Application.isPlaying)
                return;

            ScheduleEditorBake();
        }
#endif

        public void EnsureRoots()
        {
            Transform target = GetTargetFilter();
            if (target == null)
                return;

            if (collidersRoot != null && collidersRoot.parent == target)
                return;

            collidersRoot = FindOrCreateChild(target, "CollidersRoot");
            collidersRoot.localPosition = Vector3.zero;
            collidersRoot.localRotation = Quaternion.identity;
            collidersRoot.localScale = Vector3.one;

            CleanupLegacyPoolRoot();
        }

        private Transform GetTargetFilter()
        {
            if (targetFilterOverride != null)
                return targetFilterOverride;

            if (voxelObj != null && voxelObj.targetFilter != null)
                return voxelObj.targetFilter.transform;

            return transform;
        }

        private void EnsureTargetFilterOverride()
        {
            if (targetFilterOverride != null)
                return;

            if (voxelObj != null && voxelObj.targetFilter != null)
                targetFilterOverride = voxelObj.targetFilter.transform;
        }

        private void ApplyDiff(List<ColliderBoxKey> newKeys, int usedLod)
        {
            float applyStart = Time.realtimeSinceStartup;
            HashSet<ColliderBoxKey> newSet = new HashSet<ColliderBoxKey>(newKeys);

            RigidbodyState rbState = default;
            bool frozeRigidbody = false;
            if (freezeRigidbodyDuringApply)
                frozeRigidbody = TryFreezeRigidbody(out rbState);

            int removed = 0;
            foreach (var key in prevKeys)
            {
                if (!newSet.Contains(key))
                {
                    if (activeByKey.TryGetValue(key, out BoxCollider collider))
                        ReturnColliderToPool(collider);
                    activeByKey.Remove(key);
                    removed++;
                }
            }

            int added = 0;
            for (int i = 0; i < newKeys.Count; i++)
            {
                ColliderBoxKey key = newKeys[i];
                if (activeByKey.ContainsKey(key))
                    continue;

                BoxCollider collider = GetColliderFromPool();
                ApplyColliderBox(collider, key);
                activeByKey[key] = collider;
                added++;
            }

            prevKeys.Clear();
            for (int i = 0; i < newKeys.Count; i++)
                prevKeys.Add(newKeys[i]);

            activeColliderCount = activeByKey.Count;
            lastLodUsed = usedLod;

            if (frozeRigidbody)
                RestoreRigidbody(rbState);

            lastApplyMs = (Time.realtimeSinceStartup - applyStart) * 1000f;

            if (logProfiling)
                Debug.Log($"Compound colliders applied. Added {added}, removed {removed}, lod {usedLod}", this);

#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.EditorUtility.SetDirty(this);
#endif

            // ✅ version bump (ВАЖНО для DynamicVoxelObj)
            buildVersion++;
        }

        private bool TryFreezeRigidbody(out RigidbodyState state)
        {
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null)
            {
                state = default;
                return false;
            }

            state = new RigidbodyState(rb.velocity, rb.angularVelocity, rb.isKinematic);
            rb.isKinematic = true;
            return true;
        }

        private void RestoreRigidbody(RigidbodyState state)
        {
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null)
                return;

            rb.isKinematic = state.WasKinematic;
            rb.velocity = state.Velocity;
            rb.angularVelocity = state.AngularVelocity;
        }

        private List<ColliderBoxKey> BuildColliderKeys(VoxelData data, int lod)
        {
            float buildStart = Time.realtimeSinceStartup;
            int3 length = data.length;
            int3 gridLength = new int3(
                Mathf.CeilToInt(length.x / (float)lod),
                Mathf.CeilToInt(length.y / (float)lod),
                Mathf.CeilToInt(length.z / (float)lod));

            int gridSize = gridLength.x * gridLength.y * gridLength.z;
            NativeArray<byte> grid = new NativeArray<byte>(gridSize, Allocator.TempJob);
            CollisionGridBuilderJob job = new CollisionGridBuilderJob
            {
                voxels = data.voxels,
                sourceLength = length,
                lod = lod,
                gridLength = gridLength,
                grid = grid
            };

            JobHandle handle = job.Schedule();
            handle.Complete();

            List<ColliderBoxKey> keys = BuildGreedyBoxes(grid, gridLength, lod);
            grid.Dispose();

            lastBuildMs = (Time.realtimeSinceStartup - buildStart) * 1000f;
            return keys;
        }

        private List<ColliderBoxKey> BuildGreedyBoxes(NativeArray<byte> grid, int3 gridLength, int lod)
        {
            int size = gridLength.x * gridLength.y * gridLength.z;
            bool[] visited = new bool[size];
            List<ColliderBoxKey> keys = new List<ColliderBoxKey>();

            for (int z = 0; z < gridLength.z; z++)
            {
                for (int y = 0; y < gridLength.y; y++)
                {
                    for (int x = 0; x < gridLength.x; x++)
                    {
                        int index = To1D(x, y, z, gridLength);
                        if (grid[index] == 0 || visited[index])
                            continue;

                        int maxX = 1;
                        while (x + maxX < gridLength.x && IsSolidUnvisited(grid, visited, x + maxX, y, z, gridLength))
                            maxX++;

                        int maxY = 1;
                        while (y + maxY < gridLength.y && CanExpandY(grid, visited, x, y + maxY, z, maxX, gridLength))
                            maxY++;

                        int maxZ = 1;
                        while (z + maxZ < gridLength.z && CanExpandZ(grid, visited, x, y, z + maxZ, maxX, maxY, gridLength))
                            maxZ++;

                        MarkVisited(visited, x, y, z, maxX, maxY, maxZ, gridLength);

                        int volume = maxX * maxY * maxZ;
                        if (volume < greedyMinBoxVolume)
                            continue;

                        keys.Add(new ColliderBoxKey(new int3(x, y, z), new int3(maxX, maxY, maxZ), lod));
                    }
                }
            }

            return keys;
        }

        private bool IsSolidUnvisited(NativeArray<byte> grid, bool[] visited, int x, int y, int z, int3 gridLength)
        {
            int index = To1D(x, y, z, gridLength);
            return grid[index] != 0 && !visited[index];
        }

        private bool CanExpandY(NativeArray<byte> grid, bool[] visited, int startX, int y, int z, int sizeX, int3 gridLength)
        {
            for (int x = 0; x < sizeX; x++)
            {
                if (!IsSolidUnvisited(grid, visited, startX + x, y, z, gridLength))
                    return false;
            }
            return true;
        }

        private bool CanExpandZ(NativeArray<byte> grid, bool[] visited, int startX, int startY, int z, int sizeX, int sizeY, int3 gridLength)
        {
            for (int y = 0; y < sizeY; y++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    if (!IsSolidUnvisited(grid, visited, startX + x, startY + y, z, gridLength))
                        return false;
                }
            }
            return true;
        }

        private void MarkVisited(bool[] visited, int startX, int startY, int startZ, int sizeX, int sizeY, int sizeZ, int3 gridLength)
        {
            for (int z = 0; z < sizeZ; z++)
            {
                for (int y = 0; y < sizeY; y++)
                {
                    for (int x = 0; x < sizeX; x++)
                    {
                        int index = To1D(startX + x, startY + y, startZ + z, gridLength);
                        visited[index] = true;
                    }
                }
            }
        }

        private BoxCollider GetColliderFromPool()
        {
            BoxCollider collider = pool.Count > 0 ? pool.Pop() : CreateCollider();
            collider.enabled = true;
            collider.gameObject.SetActive(true);
            return collider;
        }

        private void ReturnColliderToPool(BoxCollider collider)
        {
            if (collider == null)
                return;

            collider.enabled = false;
            if (collidersRoot != null)
                collider.transform.SetParent(collidersRoot, false);
            pool.Push(collider);
        }

        private BoxCollider CreateCollider()
        {
            GameObject colliderObject = new GameObject("VoxelBoxCollider");
            colliderObject.transform.SetParent(collidersRoot != null ? collidersRoot : transform, false);
            colliderObject.transform.localPosition = Vector3.zero;
            colliderObject.transform.localRotation = Quaternion.identity;
            colliderObject.transform.localScale = Vector3.one;
            colliderObject.tag = GetColliderTag();

            BoxCollider collider = colliderObject.AddComponent<BoxCollider>();
            allCreated.Add(collider);

            return collider;
        }

        private void ApplyColliderBox(BoxCollider collider, ColliderBoxKey key)
        {
            float voxelSize = voxelObj != null ? voxelObj.GetSingleVoxelSize() : 1f;
            float cellSize = voxelSize * key.lod;
            Vector3 voxelOriginOffset = Vector3.one * (voxelSize * 0.5f);

            Vector3 localSize = new Vector3(key.size.x, key.size.y, key.size.z) * cellSize;
            Vector3 localCenter = (new Vector3(key.min.x, key.min.y, key.min.z) + new Vector3(key.size.x, key.size.y, key.size.z) * 0.5f) * cellSize - voxelOriginOffset;

            collider.center = localCenter;
            collider.size = localSize;
            collider.isTrigger = collidersAreTriggers;

            collider.transform.SetParent(collidersRoot, false);
            collider.transform.localPosition = Vector3.zero;
            collider.transform.localRotation = Quaternion.identity;
            collider.transform.localScale = Vector3.one;

            int layer = GetLayerFromMask(colliderLayer);
            if (layer >= 0)
                collider.gameObject.layer = layer;

            collider.gameObject.tag = GetColliderTag();
        }

        private static int GetLayerFromMask(LayerMask mask)
        {
            int value = mask.value;
            if (value == 0)
                return 0;

            for (int i = 0; i < 32; i++)
            {
                if ((value & (1 << i)) != 0)
                    return i;
            }

            return 0;
        }

        private string GetColliderTag()
        {
            Transform target = GetTargetFilter();
            return target != null ? target.tag : "Untagged";
        }

        private static int To1D(int x, int y, int z, int3 length)
        {
            return x + length.x * (y + length.y * z);
        }

        private static Bounds Encapsulate(Bounds a, Bounds b)
        {
            a.Encapsulate(b);
            return a;
        }

        private static Transform FindOrCreateChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null)
                return child;

            GameObject childObject = new GameObject(name);
            childObject.transform.SetParent(parent, false);
            return childObject.transform;
        }

        private void CleanupLegacyPoolRoot()
        {
            if (collidersRoot == null)
                return;

            Transform legacyPool = collidersRoot.Find("PoolRoot");
            if (legacyPool == null)
                return;

            List<Transform> children = new List<Transform>();
            for (int i = 0; i < legacyPool.childCount; i++)
                children.Add(legacyPool.GetChild(i));

            for (int i = 0; i < children.Count; i++)
                children[i].SetParent(collidersRoot, false);

            if (Application.isPlaying)
                Destroy(legacyPool.gameObject);
            else
                DestroyImmediate(legacyPool.gameObject);
        }

        private void TriggerAutoBakeIfReady()
        {
            if (Application.isPlaying)
                return;

            if (!editorAutoBake || !enabledCompound)
                return;

            if (voxelObj == null || voxelObj.voxelData == null)
                return;

            if (GetComponent<VoxFileDataProvider>() == null)
                return;

            RequestRebuild();
            Debug.Log("[CompoundBoxColliderManager] Auto-bake triggered (VoxelFileDataProvider + voxel data).", this);
        }

        private bool ShouldSkipRuntimeRebuild()
        {
            if (!Application.isPlaying)
                return false;

            if (minVoxelCountForRuntimeRebuild <= 0)
                return false;

            if (voxelObj == null)
                return false;

            if (voxelObj.ActiveVoxelCount > minVoxelCountForRuntimeRebuild)
                return false;

            runtimeAutoRebuild = false;
            return true;
        }

#if UNITY_EDITOR
        private void ScheduleEditorBake()
        {
            if (editorRebuildScheduled)
                return;

            editorRebuildScheduled = true;
            UnityEditor.EditorApplication.update -= EditorUpdate;
            UnityEditor.EditorApplication.update += EditorUpdate;

            UnityEditor.EditorApplication.delayCall += () =>
            {
                editorRebuildScheduled = false;
                if (this == null)
                    return;

                RequestRebuild();
            };
        }
#endif
    }

    [Serializable]
    public readonly struct ColliderBoxKey : IEquatable<ColliderBoxKey>
    {
        public readonly int3 min;
        public readonly int3 size;
        public readonly int lod;

        public ColliderBoxKey(int3 min, int3 size, int lod)
        {
            this.min = min;
            this.size = size;
            this.lod = lod;
        }

        public bool Equals(ColliderBoxKey other)
        {
            return min.Equals(other.min) && size.Equals(other.size) && lod == other.lod;
        }

        public override bool Equals(object obj)
        {
            return obj is ColliderBoxKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + min.GetHashCode();
                hash = hash * 31 + size.GetHashCode();
                hash = hash * 31 + lod;
                return hash;
            }
        }
    }

    public readonly struct RigidbodyState
    {
        public readonly Vector3 Velocity;
        public readonly Vector3 AngularVelocity;
        public readonly bool WasKinematic;

        public RigidbodyState(Vector3 velocity, Vector3 angularVelocity, bool wasKinematic)
        {
            Velocity = velocity;
            AngularVelocity = angularVelocity;
            WasKinematic = wasKinematic;
        }
    }
}
