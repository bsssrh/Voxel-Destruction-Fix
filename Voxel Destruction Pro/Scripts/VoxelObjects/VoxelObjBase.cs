using System;
using System.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using VoxelDestructionPro.Data;
using VoxelDestructionPro.Interfaces;
using VoxelDestructionPro.Tools;
using VoxelDestructionPro.Jobs.ColliderBaker;
using VoxelDestructionPro.Jobs.Mesher;
using VoxelDestructionPro.Settings;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;

namespace VoxelDestructionPro.VoxelObjects
{
    /// <summary>
    /// This is the base class for all voxel objects
    ///
    /// It handles most important operation, like mesh generation, pivot setting,
    /// collider baking, ...
    ///
    /// You can override it to create custom voxel objects
    /// </summary>
    public class VoxelObjBase : MonoBehaviour
    {
        #region Enums

        public enum ScaleType
        {
            Voxel, Units
        }

        #endregion
        
        #region Parameter

        [Header("Pivot")] 
        
        [Tooltip("If enabled the mesh filter object will be moved to match the pivotPlacement vector")]
        public bool setPivot = true;
        [Tooltip("Defines where the pivot should be placed, (0,0,0) = bottom left, (0.5,0.5,0.5) = center")]
        public Vector3 pivotPlacement = new Vector3(0.5f, 0.5f, 0.5f);
        [SerializeField] [HideInInspector]
        private bool pivotInit;

        [Header("Mesh")] 
        
        [Tooltip("Defines how the objectScale should be used.\nVoxel: The objectscale describes the size of a single voxel in units" +
                 "\nUnits: The objectscale describes the size of a single voxel relative to the length of the voxeldata. Useful for uniform scaling independent of voxel count")]
        public ScaleType scaleType = ScaleType.Voxel;
        [Tooltip("The size of the voxel object, size behaviour is defined by the scaletype")]
        public float objectScale = 1;
        public MeshFilter targetFilter;
        [Tooltip("Assign either a MeshCollider or a BoxCollider to this")]
        public Collider targetCollider;
        [Tooltip("The mesh setting object holds information on how the mesh should be constructed, collider settings and more")]
        public MeshSettingsObj meshSettings;

        [Header("Other")]
        
        [Tooltip("If set to true you can access the public int fields 'startVoxelCount' and 'currentVoxelCount' " +
                 "to calculate how much of the object got destroyed")]
        public bool calculateVoxelCount;

        [Header("Cleanup")]
        [Min(0)]
        public int destructionDelayFrames = 1;
        [Min(1)]
        public int maxDeferredDestructionsPerFrame = 4;
        [Min(1)]
        public int maxMeshRegenerationsPerFrame = 2;

        [HideInInspector]
        public int startVoxelCount;
        [HideInInspector]
        public int currentVoxelCount;
        
        //Private
        public VoxelData voxelData;
        protected IVoxMesher mesher;
        protected ColliderBaker colliderBaker;
        protected CompoundBoxColliderManager compoundColliderManager;
        private Mesh cMesh;
        [SerializeField] [HideInInspector]
        protected bool isCreated;

        private bool destructionQueued;
        private bool meshRegenerationQueued;
        
        //Active
        protected bool meshRegenerationActive;
        protected bool colliderBakeActive;
        //Requested
        protected bool meshRegenerationRequested;
        protected bool colliderBakeRequested;
        public bool objectDestructionRequested;
        
        protected bool isValidObject;
        
        //Events
        public Action<Mesh> onMeshGenerated;
        public Action<VoxelData> onVoxeldataChanged;
        public Action onVoxelDestroy;
        
        //Properties
        /// <summary>
        /// The voxeldatas active voxel count, note this expensive to calculate for larger voxel objects
        /// </summary>
        public int ActiveVoxelCount => voxelData.GetActiveVoxelCount();
        public int3 VoxelDataLength => voxelData.length;
        public int VoxelDataVolume => voxelData.Volume;
        
        /// <summary>
        /// The current voxeldata, can be null
        /// </summary>
        public VoxelData CurrentVoxelData
        {
            get => voxelData;
        }

        private static readonly System.Collections.Generic.List<DestructionRequest> DestructionQueue = new System.Collections.Generic.List<DestructionRequest>();
        private static int lastDestructionProcessFrame = -1;
        private static int globalMaxDeferredDestructionsPerFrame = 4;
        private static readonly System.Collections.Generic.List<VoxelObjBase> MeshRegenerationQueue = new System.Collections.Generic.List<VoxelObjBase>();
        private static int lastMeshProcessFrame = -1;
        private static int globalMaxMeshRegenerationsPerFrame = 2;
        
        #endregion

        #region Creation

        protected virtual void Awake()
        {
            compoundColliderManager = GetComponent<CompoundBoxColliderManager>();
            globalMaxDeferredDestructionsPerFrame = Mathf.Max(globalMaxDeferredDestructionsPerFrame, maxDeferredDestructionsPerFrame);
            globalMaxMeshRegenerationsPerFrame = Mathf.Max(globalMaxMeshRegenerationsPerFrame, maxMeshRegenerationsPerFrame);
        }
        
        protected virtual void Start()
        {
            if (voxelData == null)
                isValidObject = false;

            if (compoundColliderManager == null &&
                meshSettings.freezeRbWhileBaking && 
                meshSettings.useThreadedCollisionBaking &&
                targetCollider is MeshCollider mc && 
                mc.sharedMesh == null)
            {
                Rigidbody rb = GetComponent<Rigidbody>();

                if (rb != null)
                    rb.isKinematic = true;
            }
        }

        protected virtual void CreateJobs()
        {
            //Create the mesher
            if (meshSettings.mesherType == MeshSettingsObj.MeshCalculationType.Simple)
                mesher ??= new GreedyMesher(GetSingleVoxelSize(), voxelData);
            else
                mesher ??= new TripleGreedyMesher(GetSingleVoxelSize(), voxelData);

            if (compoundColliderManager == null && meshSettings.useThreadedCollisionBaking && targetCollider is MeshCollider)
                colliderBaker ??= new ColliderBaker();
        }
        
        public virtual void Create(bool editorMode = false)
        {
            if (!isActiveAndEnabled)
                return;
            
            if (!AssertVoxelObject())
            {
                isValidObject = false;
                return;
            }
            
            pivotInit = false;

            if (voxelData == null)
            {
                Debug.Log("Trying to create a voxel object without a voxelData");
                return;
            }
            
            CreateJobs();
           
            if (!meshRegenerationActive)
                StartCoroutine(GenerateMesh(editorMode));

            if (editorMode)
                DisposeAll();
            
            isCreated = true;
        }
        
        /// <summary>
        /// Assigns the voxeldata and creates the object if not created yet
        /// Editormode should only be true if this function is ran outside of
        /// play mode
        /// </summary>
        /// <param name="data"></param>
        /// <param name="editorMode"></param>
        public virtual void AssignVoxelData(VoxelData data, bool editorMode = false)
        {
            if (meshRegenerationActive)
                return; //We cant assign it while it is using it
            
            /*
             * We dispose all jobs and create them again because the old voxeldata
             * needs to get Disposed. If a jobs us currently using it it will throw an error,
             * thus we need to also dispose that on first
            */
            
            DisposeAll();

            if (data == null)
            {
                Debug.LogError("AssignVoxelData without valid voxeldata called!", this);
                isValidObject = false;
                return;
            }
            
            voxelData = data;
            if (calculateVoxelCount)
            {
                startVoxelCount = data.GetActiveVoxelCount();
                currentVoxelCount = startVoxelCount;
            }
            isValidObject = true;
            RequestMeshRegeneration();
            
            if (!isCreated)
                Create(editorMode);
            else if (!editorMode)
                CreateJobs();
            
            if (onVoxeldataChanged != null) 
                onVoxeldataChanged.Invoke(voxelData);

            NotifyVoxelDataAssigned(voxelData);
            RequestCompoundColliderRebuild();
        }
        
        /// Do not call directly, use meshRegenerationRequested instead!
        /// <summary>
        /// </summary>
        /// <param name="editorMode"></param>
        /// <returns></returns>
        protected virtual IEnumerator GenerateMesh(bool editorMode = false)
        {
            meshRegenerationActive = true;
            if (calculateVoxelCount)
                currentVoxelCount = voxelData.GetActiveVoxelCount();
            
            if (voxelData.IsEmpty())
            {
                SetMesh(null, editorMode);
                yield break;
            }
            
            Profiler.BeginSample("Starting Mesher");
            //Let it schedule the greedy job
            mesher.Prepare(voxelData);
            Profiler.EndSample();

            //Let him cook
            while (true)
            {
                if (mesher == null)
                    yield break;
                
                if (mesher.IsGreedyJobFinished() || editorMode)
                    break;
                
                yield return null;
            }
            
            Mesh m;

            if (meshSettings.meshConstructionJob && !editorMode)
            {
                //For complex meshes every operation will take some time
                //we want to minimize the lag by adding some delay
                mesher.StartBuildMesh();
                
                while (!mesher.IsMeshJobFinished()) //This should only take few frames
                    yield return null;
                
                m = mesher.CompleteMeshBuilding();
                yield return null;
            }
            else
            {
                m = mesher.CreateMeshInstantly();
            }
            
            SetMesh(m, editorMode);
            meshRegenerationActive = false;
        }

        /// <summary>
        /// Sets the MeshFilter and MeshCollider Mesh and calculates
        /// Pivot
        /// </summary>
        /// <param name="m"></param>
        /// <param name="editorMode"></param>
        protected void SetMesh(Mesh m, bool editorMode)
        {
            //Destroy the mesh to remove it from memory, 10 seconds delay because the collision could still be
            //baking in background
            if (cMesh != m && !editorMode)
                StartCoroutine(ReleaseMeshAfterDelay(cMesh, 10f));
            
            cMesh = m;
            if (onMeshGenerated != null)
                onMeshGenerated.Invoke(m);
            NotifyMeshGenerated(m);
            
            if (m == null || m.vertexCount == 0)
            {
                if (!editorMode)
                    objectDestructionRequested = true;

                m = null;
            }
            
            //Assign Mesh
            targetFilter.mesh = m;
            
            //Pivot Stuff: pivotInit marks if the pivot is already set to avoid reseting it
            //every time a destruction occurs
            if (!pivotInit)
            {
                pivotInit = true;
                
                if (setPivot && m != null)
                {
                    Vector3 length = m.bounds.max - m.bounds.min;
                    length.x *= pivotPlacement.x;
                    length.y *= pivotPlacement.y;
                    length.z *= pivotPlacement.z;
                    
                    //Get the target localPosition from Mesh.bounds
                    Vector3 localTargetPoint = length + m.bounds.min;
                    
                    targetFilter.transform.localPosition = -localTargetPoint;
                }
            }
            
            Profiler.BeginSample("Setting Mesh Collider");

            if (compoundColliderManager != null && compoundColliderManager.enabledCompound)
            {
                ClearTargetCollider();
            }
            else
            {
                if (targetCollider != null && m != null)
                {
                    if (targetCollider is MeshCollider meshCollider)
                    {
                        //No cookingOptions = Better performance, without a job we use none
                        if (!meshSettings.useThreadedCollisionBaking)
                            meshCollider.cookingOptions = MeshColliderCookingOptions.None;
                        else
                            meshCollider.cookingOptions = meshSettings.cookingOptions;

                        //ThreadedColliderBaking is only used in playmode
                        if (editorMode || !meshSettings.useThreadedCollisionBaking)
                            meshCollider.sharedMesh = m;
                        else
                            colliderBakeRequested = true;
                    }
                    else if (targetCollider is BoxCollider boxCollider)
                    {
                        boxCollider.center = m.bounds.center;
                        boxCollider.size = m.bounds.size;
                    }
                }
                else if (targetCollider != null)
                {
                    ClearTargetCollider();
                }
            }
            
            Profiler.EndSample();
        }

        /// <summary>
        /// Clears the Mesh, Collider, voxelData and created
        /// </summary>
        public virtual void Clear()
        {
            objectDestructionRequested = false;
            meshRegenerationActive = false;
            meshRegenerationRequested = false;
            colliderBakeActive = false;
            colliderBakeRequested = false;
            isValidObject = false;
            
            if (cMesh != null && Application.isPlaying)
                StartCoroutine(ReleaseMeshAfterDelay(cMesh, 10f));
            
            if (targetFilter != null)
                targetFilter.mesh = null;
            if (targetCollider != null)
                ClearTargetCollider();
            
            isCreated = false;
            DisposeAll();
        }
        
        /// <summary>
        /// Gets called from the editor quick setup button,
        /// creates some stuff based on the settings defined
        /// on the VoxelManager
        /// </summary>
        public virtual void QuickSetup(VoxelManager manager)
        {
            if (manager == null)
            {
                Debug.LogWarning("Add a Voxel Manager to the scene, if you want to use quick setup");
                return;
            }
            if (targetFilter != null)
            {
                Debug.Log("Can not create a new target Filter, already exists!");
                return;
            }
            
            GameObject nFilter = new GameObject();
            
            nFilter.transform.parent = transform;
            nFilter.gameObject.name = "Voxel Mesh";
            nFilter.transform.localPosition = Vector3.zero;
            
            targetFilter = nFilter.AddComponent<MeshFilter>();
            
            //Collider setup
            if (manager.standardCollider != VoxelManager.ColliderType.None)
            {
                MeshCollider mc = nFilter.AddComponent<MeshCollider>();
                targetCollider = mc;
                mc.cookingOptions = MeshColliderCookingOptions.None;
                
                if (manager.standardCollider == VoxelManager.ColliderType.Convex)
                    mc.convex = true;
            }
            else
                targetCollider = null;
            
            MeshRenderer mr = nFilter.AddComponent<MeshRenderer>();

            mr.material = manager.standardMaterial;
            mr.shadowCastingMode = ShadowCastingMode.TwoSided;
            meshSettings = manager.standardMeshSettings;
        }

        #endregion

        #region Events
        
        /// <summary>
        /// Destroys the mesh object
        /// </summary>
        protected virtual void DestroyVoxObj()
        {
            if (onVoxelDestroy != null)
                onVoxelDestroy.Invoke();
            
            Clear();
            
            if (meshSettings.emptyAction == MeshSettingsObj.EmptyAction.Destroy)
                Destroy(gameObject);
            else if (meshSettings.emptyAction == MeshSettingsObj.EmptyAction.Deactive)
                gameObject.SetActive(false);
        }

        /// <summary>
        /// Allows you to block the destruction of the object,
        /// some jobs need to complete before the voxelobject can be
        /// destroyed
        /// </summary>
        /// <returns></returns>
        protected virtual bool CanDestroyObject()
        {
            return true;
        }
        
        private void OnApplicationQuit()
        {
            DisposeAll();
        }

        private void OnDestroy()
        {
            if (cMesh != null)
                MeshPool.Release(cMesh);
            DisposeAll();
        }

        /// <summary>
        /// You can call this multiple times, but once called
        /// all jobs get disposed and you cant use them anymore
        /// </summary>
        protected virtual void DisposeAll()
        {
            if (mesher != null)
                mesher.Dispose();
            mesher = null;
            
            if (voxelData != null)
                voxelData.Dispose();
            voxelData = null;
        }
        
        #endregion

        #region Update
        
        protected virtual void Update()
        {
            ProcessDestructionQueue();
            ProcessMeshRegenerationQueue();

            if (objectDestructionRequested && CanDestroyObject())
                QueueDestruction();
            
            if (!isValidObject)
                return;
            
            if (compoundColliderManager == null && colliderBakeRequested && !colliderBakeActive && targetCollider is MeshCollider mc)
            {
                colliderBakeRequested = false;
                colliderBakeActive = true;
                colliderBaker.BakeCollider(mc, cMesh);
            }

            if (compoundColliderManager == null && colliderBakeActive && colliderBaker.isCompleted())
            {
                colliderBakeActive = false;
                colliderBaker.FinishBaking();
                
                if (meshSettings.freezeRbWhileBaking)
                {
                    Rigidbody rb = GetComponent<Rigidbody>();

                    if (rb != null)
                        rb.isKinematic = false;
                }
            }
        }

        #endregion

        private void QueueDestruction()
        {
            if (destructionQueued)
                return;

            destructionQueued = true;
            int delayFrames = Mathf.Max(0, destructionDelayFrames);
            DestructionQueue.Add(new DestructionRequest(this, Time.frameCount + delayFrames));
        }

        private void ExecuteDeferredDestruction()
        {
            objectDestructionRequested = false;
            destructionQueued = false;
            DestroyVoxObj();
        }

        private static void ProcessDestructionQueue()
        {
            if (Time.frameCount == lastDestructionProcessFrame)
                return;

            lastDestructionProcessFrame = Time.frameCount;

            for (int i = DestructionQueue.Count - 1; i >= 0; i--)
            {
                if (DestructionQueue[i].Target == null)
                    DestructionQueue.RemoveAt(i);
            }

            int budget = Mathf.Max(1, globalMaxDeferredDestructionsPerFrame);
            int processed = 0;

            for (int i = 0; i < DestructionQueue.Count && processed < budget;)
            {
                DestructionRequest request = DestructionQueue[i];
                if (request.Target == null)
                {
                    DestructionQueue.RemoveAt(i);
                    continue;
                }

                if (Time.frameCount < request.ReadyFrame)
                {
                    i++;
                    continue;
                }

                if (!request.Target.CanDestroyObject())
                {
                    i++;
                    continue;
                }

                request.Target.ExecuteDeferredDestruction();
                DestructionQueue.RemoveAt(i);
                processed++;
            }
        }

        private readonly struct DestructionRequest
        {
            public DestructionRequest(VoxelObjBase target, int readyFrame)
            {
                Target = target;
                ReadyFrame = readyFrame;
            }

            public VoxelObjBase Target { get; }
            public int ReadyFrame { get; }
        }
        
        #region Other
        
        /// <summary>
        /// This method checks if the settings are valid,
        /// will abort the mesh creation in case there are invalid settings
        /// </summary>
        /// <returns></returns>
        protected virtual bool AssertVoxelObject()
        {
            if (GetSingleVoxelSize() == 0)
            {
                Debug.LogError("Object scale can not be zero!");
                return false;
            }
            else if (targetFilter == null)
            {
                Debug.LogError("Target filter is null!");
                return false;
            }
            else if (meshSettings == null)
            {
                Debug.LogError("Mesh settings not assigned!");
                return false;
            }
            else if (setPivot && targetFilter.gameObject == gameObject)
            {
                Debug.LogError("If setPivot is enabled the targetFilter needs to be on a child of the object!");
                return false;
            }
            
            return true;
        }

        //Index Stuff
        protected Vector3 To3D(int index, int xMax, int yMax)
        {
            int z = index / (xMax * yMax);
            int idx = index - (z * xMax * yMax);
            int y = idx / xMax;
            int x = idx % xMax;
            return new Vector3(x, y, z);
        }
        
        protected Vector3 To3D(int index)
        {
            int z = index / (voxelData.length.x * voxelData.length.y);
            int idx = index - (z * voxelData.length.x * voxelData.length.y);
            int y = idx / voxelData.length.x;
            int x = idx % voxelData.length.x;
            return new Vector3(x, y, z);
        }
        
        protected int To1D(Vector3 index, int xMax, int yMax)
        {
            return (int)(index.x + xMax * (index.y + yMax * index.z));
        }
        
        protected Vector3 GetMinV3(VoxReader.Voxel[] array)
        {
            Vector3 min = array[0].Position;
            
            for (int i = 1; i < array.Length; i++)
            {
                if (array[i].Position.x < min.x)
                    min = new Vector3(array[i].Position.x, min.y, min.z);
                
                if (array[i].Position.y < min.y)
                    min = new Vector3(min.x, array[i].Position.y, min.z);
                
                if (array[i].Position.z < min.z)
                    min = new Vector3(min.x, min.y, array[i].Position.z);
            }
            
            return min;
        }

        public Color GetColorFromVoxelData()
        {
            return voxelData.palette[Random.Range(0, voxelData.palette.Length)];
        }

        public float GetSingleVoxelSize()
        {
            return scaleType switch
            {
                ScaleType.Voxel => objectScale,
                ScaleType.Units => objectScale / math.length(voxelData.length),
                _ => throw new InvalidOperationException()
            };
        }

        public void RequestMeshRegeneration()
        {
            meshRegenerationRequested = true;
            QueueMeshRegeneration();
        }

        public void RequestCompoundColliderRebuild()
        {
            if (compoundColliderManager != null && compoundColliderManager.enabledCompound)
                compoundColliderManager.RequestRebuild();
        }

        public void SetCompoundColliderManager(CompoundBoxColliderManager manager)
        {
            compoundColliderManager = manager;
        }

        private void ClearTargetCollider()
        {
            if (targetCollider is MeshCollider meshCollider)
                meshCollider.sharedMesh = null;
            else if (targetCollider is BoxCollider boxCollider)
                boxCollider.size = Vector3.zero;
        }

        private void NotifyVoxelDataAssigned(VoxelData data)
        {
            IVoxelDataConsumer[] consumers = GetComponents<IVoxelDataConsumer>();
            for (int i = 0; i < consumers.Length; i++)
                consumers[i].OnVoxelDataAssigned(data);
        }

        private void NotifyMeshGenerated(Mesh mesh)
        {
            IVoxelMeshReady[] listeners = GetComponents<IVoxelMeshReady>();
            for (int i = 0; i < listeners.Length; i++)
                listeners[i].OnMeshGenerated(mesh);
        }

        private void QueueMeshRegeneration()
        {
            if (meshRegenerationQueued)
                return;

            meshRegenerationQueued = true;
            MeshRegenerationQueue.Add(this);
        }

        private static void ProcessMeshRegenerationQueue()
        {
            if (Time.frameCount == lastMeshProcessFrame)
                return;

            lastMeshProcessFrame = Time.frameCount;

            for (int i = MeshRegenerationQueue.Count - 1; i >= 0; i--)
            {
                if (MeshRegenerationQueue[i] == null)
                    MeshRegenerationQueue.RemoveAt(i);
            }

            int budget = Mathf.Max(1, globalMaxMeshRegenerationsPerFrame);
            int processed = 0;

            for (int i = 0; i < MeshRegenerationQueue.Count && processed < budget;)
            {
                VoxelObjBase target = MeshRegenerationQueue[i];
                if (target == null)
                {
                    MeshRegenerationQueue.RemoveAt(i);
                    continue;
                }

                if (!target.meshRegenerationRequested || target.meshRegenerationActive || !target.isActiveAndEnabled)
                {
                    i++;
                    continue;
                }

                target.meshRegenerationRequested = false;
                target.meshRegenerationQueued = false;
                target.StartCoroutine(target.GenerateMesh());
                MeshRegenerationQueue.RemoveAt(i);
                processed++;
            }
        }

        private IEnumerator ReleaseMeshAfterDelay(Mesh mesh, float delaySeconds)
        {
            if (mesh == null)
                yield break;

            yield return new WaitForSeconds(delaySeconds);
            MeshPool.Release(mesh);
        }

        protected GameObject InstantiateVox(GameObject prefab, Vector3 pos, Quaternion rot)
        {
            if (VoxelManager.Instance == null)
                return Instantiate(prefab, pos, rot);
            else
                return VoxelManager.Instance.InstantiatePooled(prefab, pos, rot);
        }
        
        protected GameObject InstantiateVox(GameObject prefab)
        {
            if (VoxelManager.Instance == null)
                return Instantiate(prefab);
            else
                return VoxelManager.Instance.InstantiatePooled(prefab);
        }
        
        #endregion

        #region Debug

        #if UNITY_EDITOR
        
        [ContextMenu("Show debug info")]
        public void ShowDebugInfo()
        {
            Debug.Log("-- Voxel object " + name + " --");
            Debug.Log("IsValidObject: " + isValidObject);
            Debug.Log("Start voxel count: " + startVoxelCount);
            Debug.Log("Is Created: " + isCreated);
            Debug.Log("Active voxel count: " + ActiveVoxelCount);
            Debug.Log("Voxel data volume: " + VoxelDataVolume);
        }

        #endif
        
        #endregion
    }      
}
