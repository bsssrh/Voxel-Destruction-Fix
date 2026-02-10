using System;
using System.Collections.Generic;
using Better.StreamingAssets;
using UnityEngine;
using VoxelDestructionPro.Data;
using VoxelDestructionPro.Data.Serializable;
using VoxelDestructionPro.Settings;
using VoxelDestructionPro.Tools;

namespace VoxelDestructionPro
{
    /// <summary>
    /// Manages stuff that is independent of a single voxel object
    ///
    /// - Quick setup
    /// - Voxeldata caching
    /// - Fragment pooling
    /// </summary>
    public class VoxelManager : MonoBehaviour
    {
        private static VoxelManager _Instance;

        public static VoxelManager Instance
        {
            get
            {
                if (_Instance == null)
                    Debug.LogError("There is no Voxel Manager in the scene!");

                return _Instance;
            }
        }
        
        public enum ColliderType
        {
            None, Standard, Convex
        }
        
        [Header("Quick setup")]
        
        public Material standardMaterial;
        public ColliderType standardCollider;
        public MeshSettingsObj standardMeshSettings;
        public IsoSettings standardIsolationSettings;
        public DynSettings standardDynamicSettings;
        public Transform fragmentParent;

        [Header("Fragment pooling")] 
        
        public PooledFragments[] fragmentPools;
        [Min(0f)]
        public float poolingUpdateInterval = 0.2f;

        //Main object pool
        private bool poolChanged;
        private Dictionary<GameObject, Queue<GameObject>> objectPool;
        private Dictionary<GameObject, int> poolTargetCounts;
        private float poolingUpdateTimer;
        
        //Vox obj caching
        [Header("Voxel cache")]
        [Min(0)]
        public int maxVoxelCacheEntries = 128;
        private Dictionary<Tuple<string, int>, LinkedListNode<VoxelCacheEntry>> voxelCache;
        private LinkedList<VoxelCacheEntry> voxelCacheLru;

        private bool betterStreamingAssetsLoaded;
        
        private void Awake()
        {
            if (_Instance == null)
            {
                _Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else if (_Instance != this)
            {
                Destroy(gameObject);
            }
            
            betterStreamingAssetsLoaded = false;

            InitPooling();
        }

        #region VoxCaching
        
        public void LoadBSA()
        {
            //I think it is better if we dont load it multiple times
            if (betterStreamingAssetsLoaded)
                return;

            betterStreamingAssetsLoaded = true;
            BetterStreamingAssets.Initialize();
        }
        
        /// <summary>
        /// Loads Voxeldata and caches it, this allows
        /// the reuse of Voxeldata
        /// </summary>
        /// <param name="modelpath"></param>
        /// <param name="modelIndex"></param>
        /// <returns></returns>
        public VoxelData LoadAndCacheVoxFile(string modelpath, int modelIndex)
        {
            voxelCache ??= new Dictionary<Tuple<string, int>, LinkedListNode<VoxelCacheEntry>>();
            voxelCacheLru ??= new LinkedList<VoxelCacheEntry>();
            
            Tuple<string, int> key = new Tuple<string, int>(modelpath, modelIndex);
            if (voxelCache.TryGetValue(key, out LinkedListNode<VoxelCacheEntry> cachedNode))
            {
                //Aleady cached, we dont need to load
                voxelCacheLru.Remove(cachedNode);
                voxelCacheLru.AddFirst(cachedNode);
                return new VoxelData(cachedNode.Value.Data.GetCopy());
            }

            VoxelParser parser = new VoxelParser(modelpath, modelIndex);
            VoxelData file = parser.ParseToVoxelData();
            CacheVoxelData(key, file.ToCachedVoxelData().GetCopy());

            return file;
        }
        
        #endregion

        #region FragmentPool

        private void InitPooling()
        {
            objectPool = new Dictionary<GameObject, Queue<GameObject>>();
            poolTargetCounts = new Dictionary<GameObject, int>();

            foreach (var pool in fragmentPools)
            {
                if (pool.instanceCount <= 0)
                    continue;

                var instances = new Queue<GameObject>();

                for (int i = 0; i < pool.instanceCount; i++)
                {
                    GameObject n = Instantiate(pool.prefab, transform, true);
                    n.SetActive(false);
                    instances.Enqueue(n);
                }
                
                objectPool.Add(pool.prefab, instances);
                poolTargetCounts[pool.prefab] = pool.instanceCount;
            }

            poolChanged = false;
            poolingUpdateTimer = poolingUpdateInterval;
        }
        
        /// <inheritdoc cref="InstantiatePooled(UnityEngine.GameObject,UnityEngine.Vector3,UnityEngine.Quaternion)"/>
        public GameObject InstantiatePooled(GameObject prefab, Vector3 pos, Quaternion rot)
        {
            GameObject newObj = InstantiatePooled(prefab);
            newObj.transform.SetPositionAndRotation(pos, rot);

            return newObj;
        }

        /// <summary>
        /// Checks if the requested object is pooled, else it Instantiates it
        /// </summary>
        /// <param name="prefab"></param>
        /// <returns></returns>
        public GameObject InstantiatePooled(GameObject prefab)
        {
            if (objectPool.TryGetValue(prefab, out var pooled))
            {
                if (pooled.TryDequeue(out GameObject res))
                {
                    if (res == null)
                        return Instantiate(prefab);
                    
                    poolChanged = true;
                    res.SetActive(true);
                    res.transform.parent = null;
                    return res;
                }
                else 
                    return Instantiate(prefab);
            }
            else
                return Instantiate(prefab);
        }

        /// <summary>
        /// Updates the object pool by adding new elements when they got removed
        /// </summary>
        private void UpdatePooling()
        {
            if (!poolChanged)
                return;
            
            poolChanged = false;
            
            foreach (var kvp in objectPool)
            {
                if (!poolTargetCounts.TryGetValue(kvp.Key, out int targetCount))
                    continue;

                if (kvp.Value.Count < targetCount)
                {
                    GameObject n = Instantiate(kvp.Key, transform, true);
                    n.SetActive(false);
                    kvp.Value.Enqueue(n);
                    poolChanged = true;
                }
            }
        }
        
        #endregion

        #region Events
        
        private void Update()
        {
            poolingUpdateTimer -= Time.unscaledDeltaTime;
            if (poolingUpdateTimer <= 0f)
            {
                poolingUpdateTimer = Mathf.Max(0.01f, poolingUpdateInterval);
                UpdatePooling();
            }
        }

        #endregion

        private void CacheVoxelData(Tuple<string, int> key, CachedVoxelData data)
        {
            if (maxVoxelCacheEntries <= 0)
                return;

            if (voxelCache.TryGetValue(key, out LinkedListNode<VoxelCacheEntry> cachedNode))
            {
                cachedNode.Value.Data = data;
                voxelCacheLru.Remove(cachedNode);
                voxelCacheLru.AddFirst(cachedNode);
                return;
            }

            var entry = new VoxelCacheEntry(key, data);
            var node = voxelCacheLru.AddFirst(entry);
            voxelCache[key] = node;

            if (voxelCache.Count <= maxVoxelCacheEntries)
                return;

            LinkedListNode<VoxelCacheEntry> last = voxelCacheLru.Last;
            if (last == null)
                return;

            voxelCacheLru.RemoveLast();
            voxelCache.Remove(last.Value.Key);
        }

        private sealed class VoxelCacheEntry
        {
            public VoxelCacheEntry(Tuple<string, int> key, CachedVoxelData data)
            {
                Key = key;
                Data = data;
            }

            public Tuple<string, int> Key { get; }
            public CachedVoxelData Data { get; set; }
        }
    }
}
