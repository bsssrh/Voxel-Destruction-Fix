using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Profiling;
using VoxelDestructionPro.Data;
using VoxelDestructionPro.Jobs.Isolator;
using VoxelDestructionPro.Jobs.Simple;
using VoxelDestructionPro.Settings;
using VoxelDestructionPro.VoxDataProviders;

namespace VoxelDestructionPro.VoxelObjects
{
     /// <summary>
     /// Finds isolated pieces in the voxelobject and removes them
     /// 
     /// ✅ FIX: Сохраняет матрицу трансформации для корректного позиционирования изолированных фрагментов
     /// </summary>
    public class IsolatedVoxelObj : VoxelObjBase
    {
        [Header("Isolation")] 
        
        public IsoSettings isoSettings;
        [Tooltip("The isolation origin, defines the axis that is connected to a solid object. " +
                 "If you object has physics you probably want to set this to none so that a new fragment is created whenever split.")]
        public IsoSettings.IsolationOrigin isolationOrigin = IsoSettings.IsolationOrigin.YNeg;
        [Tooltip("You can assign a parent for all the fragments that will get created, " +
                 "this helps keeping the scene organized and keeping track of the fragments. " +
                 "Make sure that this object has a scale of (1, 1, 1)")]
        public Transform fragmentParent;
        
        //Active states
        protected bool isolatorActive;
        protected bool isolationProcessorActive;

        //Requires
        protected bool isolatorRequested;
        
        //Locks
        /// <summary>
        /// Blocks the Isolator from running
        /// </summary>
        protected bool lockIsolatorRun;
        /// <summary>
        /// Blocks the Isolator from starting a mesh reload once it is completed
        /// </summary>
        protected bool lockIsolatorRebuild;
        private bool pendingIsolationRebuild;

        // ✅ FIX: Сохранённая матрица трансформации для корректного позиционирования изолированных фрагментов
        private Matrix4x4 savedIsolationTransformMatrix;
        private bool hasSavedIsolationMatrix;
        
        private CCL_Isolator isolator;
        private IsolationProcessor isolationProcessor;

        //Events
        public Action<GameObject> onIsolationFragmentCreated;
        public Action<NativeArray<ushort>> onIsolationDataReturned;
        
        protected override void Start()
        {
            base.Start();

            if (isValidObject && isoSettings.runIsolationOnStart && isoSettings.isolationMode != IsoSettings.IsolationMode.None)
                isolatorRequested = true;
        }

        protected override bool AssertVoxelObject()
        {
            if (isoSettings == null)
            {
                Debug.LogError("No isolation settings assigned!");
                return false;
            }
            else
                return base.AssertVoxelObject();
        }

        protected override void CreateJobs()
        {
            base.CreateJobs();

            if (isoSettings.isolationMode != IsoSettings.IsolationMode.None)
            {
                isolator ??= new CCL_Isolator(isolationOrigin, voxelData);
                if (isoSettings.isolationMode == IsoSettings.IsolationMode.Fragment)
                    isolationProcessor ??= new IsolationProcessor(voxelData);
            }
        }

        protected override void Update()
        {
            base.Update();

            if (!isValidObject)
                return;
            
            if (isolatorRequested && !isolatorActive && !lockIsolatorRun && !isolationProcessorActive)
            {
                if (isoSettings.isolationMode == IsoSettings.IsolationMode.None)
                    return;
                
                isolatorRequested = false;
                isolatorActive = true;

                // ✅ FIX: Сохраняем матрицу трансформации перед началом изоляции
                Transform meshTransform = targetFilter != null ? targetFilter.transform : transform;
                savedIsolationTransformMatrix = meshTransform.localToWorldMatrix;
                hasSavedIsolationMatrix = true;
                
                isolator.Begin(voxelData);
            }

            if (isolatorActive && isolator.IsFinished())
                FinishIsolation();
            
            if (isolationProcessorActive && isolationProcessor.ProcessorCompleted())
                FinishFragmentProcessing();

            if (pendingIsolationRebuild && !lockIsolatorRebuild)
            {
                pendingIsolationRebuild = false;
                RequestMeshRegeneration();
            }
        }

        
        /// <summary>
        /// Finishes the Isolation Job
        /// </summary>
        private void FinishIsolation()
        {
            NativeArray<ushort> data = isolator.GetResults();
            Profiler.BeginSample("Setting Isolation data");
            
            if (isoSettings.isolationMode == IsoSettings.IsolationMode.Fragment)
            {
                isolationProcessor.ProcessIsolationData(data, voxelData);
                isolationProcessorActive = true;
            }

            VoxelOverride overrideJob = new VoxelOverride()
            {
                voxels = voxelData.voxels,
                data = data
            };
            
            overrideJob.Run();

            if (onIsolationDataReturned != null) 
                onIsolationDataReturned.Invoke(data);

            onVoxeldataChanged?.Invoke(voxelData);

            if (lockIsolatorRebuild)
                pendingIsolationRebuild = true;
            else
                RequestMeshRegeneration();

            RequestCompoundColliderRebuild();
            
            Profiler.EndSample();
            
            isolatorActive = false;
        }

        private void FinishFragmentProcessing()
        {
            isolationProcessorActive = false;
            VoxelData[] fragments = isolationProcessor.CreateFragments(voxelData, out Vector3[] positions);

            if (fragments == null)
            {
                hasSavedIsolationMatrix = false;
                return;
            }

            if (targetFilter == null)
            {
                for (int i = 0; i < fragments.Length; i++)
                    fragments[i]?.Dispose();
                hasSavedIsolationMatrix = false;
                return;
            }

            // ✅ OPTIMIZATION: Cache voxel size and rotation - used repeatedly in loops
            float voxelSize = GetSingleVoxelSize();
            Quaternion worldRot = GetRotationFromSavedIsolationMatrix();
            
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
                    // ✅ FIX: Use saved matrix for consistent positioning
                    fragmentWorldPositions[p] = TransformPointUsingSavedIsolationMatrix(positions[p] * voxelSize);
                }

                attachmentMap = fragmentGroup.BuildAttachmentMap(transform, fragmentWorldPositions);
            }

            for (int i = 0; i < fragments.Length; i++)
            {
                // Temporarily disable the min voxel filter to validate fragment creation.
                int minVoxelCount = 0;
                if (minVoxelCount > 0)
                    if (!fragments[i].ActiveCountLarger(minVoxelCount))
                    {
                        fragments[i].Dispose();
                        continue; 
                    }

                // ✅ FIX: Validate position to prevent coordinate errors
                if (!IsValidIsolationPosition(positions[i], voxelData.length))
                {
                    fragments[i].Dispose();
                    continue;
                }

                // ✅ FIX: Use saved matrix for consistent positioning
                Vector3 worldPos = TransformPointUsingSavedIsolationMatrix(positions[i] * voxelSize);
                
                GameObject nObj = InstantiateVox(
                    isoSettings.isolationFragmentPrefab, 
                    worldPos, 
                    worldRot
                );
                DisableDataProviders(nObj);
                nObj.transform.parent = fragmentParent;
                
                VoxelObjBase vox = nObj.GetComponent<VoxelObjBase>();
                if (vox != null)
                {
                    vox.scaleType = ScaleType.Voxel;
                    vox.objectScale = voxelSize;
                    if (vox is IsolatedVoxelObj iso)
                        iso.fragmentParent = fragmentParent;
                    
                    vox.AssignVoxelData(fragments[i]);   
                }
                else
                    fragments[i].Dispose();
                
                if (onIsolationFragmentCreated != null)
                    onIsolationFragmentCreated.Invoke(nObj);

                fragmentGroup?.SpawnAttachmentsForFragment(i, nObj.transform, attachmentMap);
            }

            hasSavedIsolationMatrix = false;
        }
        
        /// <summary>
        /// ✅ FIX: Validates position to prevent coordinate errors from isolation processor
        /// </summary>
        private bool IsValidIsolationPosition(Vector3 position, Unity.Mathematics.int3 length)
        {
            // Check for NaN or Infinity
            if (float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z) ||
                float.IsInfinity(position.x) || float.IsInfinity(position.y) || float.IsInfinity(position.z))
                return false;
            
            // Check if position is within reasonable bounds
            if (position.x < -0.5f || position.x > length.x + 0.5f ||
                position.y < -0.5f || position.y > length.y + 0.5f ||
                position.z < -0.5f || position.z > length.z + 0.5f)
                return false;
            
            return true;
        }

        /// <summary>
        /// ✅ FIX: Трансформирует локальную позицию в мировую используя сохранённую матрицу изоляции
        /// </summary>
        private Vector3 TransformPointUsingSavedIsolationMatrix(Vector3 localPosition)
        {
            if (!hasSavedIsolationMatrix)
            {
                // Fallback на текущий transform если матрица не сохранена
                Transform meshTransform = targetFilter != null ? targetFilter.transform : transform;
                return meshTransform.TransformPoint(localPosition);
            }

            return savedIsolationTransformMatrix.MultiplyPoint3x4(localPosition);
        }

        /// <summary>
        /// ✅ FIX: Извлекает rotation из сохранённой матрицы изоляции
        /// </summary>
        private Quaternion GetRotationFromSavedIsolationMatrix()
        {
            if (!hasSavedIsolationMatrix)
            {
                // Fallback на текущий transform если матрица не сохранена
                Transform meshTransform = targetFilter != null ? targetFilter.transform : transform;
                return meshTransform.rotation;
            }

            return savedIsolationTransformMatrix.rotation;
        }

        private static void DisableDataProviders(GameObject obj)
        {
            if (obj == null)
                return;

            VoxDataProvider[] providers = obj.GetComponents<VoxDataProvider>();
            for (int i = 0; i < providers.Length; i++)
                providers[i].enabled = false;
        }

        public override void QuickSetup(VoxelManager manager)
        {
            base.QuickSetup(manager);

            isoSettings = manager.standardIsolationSettings;
            fragmentParent = manager.fragmentParent;
        }

        protected override bool CanDestroyObject()
        {
            if (isolatorActive || isolationProcessorActive)
                return false;
            
            return base.CanDestroyObject();
        }

        protected override void DisposeAll()
        {
            if (isolationProcessor != null)
                isolationProcessor.Dispose();
            isolationProcessor = null;
            if (isolator != null)
                isolator.Dispose();
            isolator = null;

            hasSavedIsolationMatrix = false;
            
            base.DisposeAll();
        }

        protected override void DestroyVoxObj()
        {
            isolatorActive = false;
            isolationProcessorActive = false;
            isolatorRequested = false;
            hasSavedIsolationMatrix = false;
            base.DestroyVoxObj();
        }
    }   
}
