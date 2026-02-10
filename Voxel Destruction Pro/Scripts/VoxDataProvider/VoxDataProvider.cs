using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VoxelDestructionPro.VoxelObjects;

namespace VoxelDestructionPro.VoxDataProviders
{
    [ExecuteAlways]
    [RequireComponent(typeof(VoxelObjBase))]
    public class VoxDataProvider : MonoBehaviour
    {
        protected VoxelObjBase targetObj;

        private void Start()
        {
            targetObj = GetComponent<VoxelObjBase>();
            if (Application.isPlaying)
            {
                Load(false);
                return;
            }

#if UNITY_EDITOR
            Load(true);
#endif
        }

        public virtual void Load(bool editorMode)
        {
            targetObj = GetComponent<VoxelObjBase>();
            if (editorMode)
                targetObj.Clear();
        }

        public virtual void Clear()
        {
            targetObj = GetComponent<VoxelObjBase>();
            targetObj.Clear();
        }

        public virtual void ResetVoxelObject()
        {
            Clear();
            Load(false);
        }
    }
}
