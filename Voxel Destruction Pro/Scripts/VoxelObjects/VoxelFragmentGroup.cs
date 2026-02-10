using System.Collections.Generic;
using UnityEngine;

namespace VoxelDestructionPro.VoxelObjects
{
    /// <summary>
    /// Defines child voxel objects that should be copied onto spawned fragments.
    /// Useful when a voxel object contains other voxel objects that should persist after fragmentation.
    /// </summary>
    public class VoxelFragmentGroup : MonoBehaviour
    {
        [Header("Collection")]
        [Tooltip("Automatically collect child objects based on the filters below.")]
        public bool autoCollectChildren = true;

        [Tooltip("Only include direct children (skip nested grandchildren).")]
        public bool onlyDirectChildren = true;

        [Tooltip("Include inactive child objects when auto-collecting.")]
        public bool includeInactive = true;

        [Tooltip("If disabled, only child objects that have a VoxelObjBase component are collected.")]
        public bool includeNonVoxelObjects = false;

        [Tooltip("List of child objects to copy onto fragments.")]
        public List<Transform> attachedObjects = new List<Transform>();

        public readonly struct AttachmentData
        {
            public AttachmentData(GameObject prefab, Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
            {
                Prefab = prefab;
                LocalPosition = localPosition;
                LocalRotation = localRotation;
                LocalScale = localScale;
            }

            public GameObject Prefab { get; }
            public Vector3 LocalPosition { get; }
            public Quaternion LocalRotation { get; }
            public Vector3 LocalScale { get; }
        }

        private void Reset()
        {
            RefreshAttachedObjects();
        }

        private void OnValidate()
        {
            if (autoCollectChildren)
                RefreshAttachedObjects();
        }

        public bool HasAttachments()
        {
            for (int i = 0; i < attachedObjects.Count; i++)
            {
                if (attachedObjects[i] != null)
                    return true;
            }

            return false;
        }

        public void RefreshAttachedObjects()
        {
            attachedObjects.Clear();

            Transform[] children = GetComponentsInChildren<Transform>(includeInactive);
            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (child == null || child == transform)
                    continue;

                if (onlyDirectChildren && child.parent != transform)
                    continue;

                if (!includeNonVoxelObjects && child.GetComponent<VoxelObjBase>() == null)
                    continue;

                attachedObjects.Add(child);
            }
        }

        public Dictionary<int, List<AttachmentData>> BuildAttachmentMap(Transform sourceRoot, Vector3[] fragmentWorldPositions)
        {
            if (sourceRoot == null || fragmentWorldPositions == null || fragmentWorldPositions.Length == 0)
                return null;

            Dictionary<int, List<AttachmentData>> map = null;

            for (int i = 0; i < attachedObjects.Count; i++)
            {
                Transform attachment = attachedObjects[i];
                if (attachment == null)
                    continue;

                int index = FindClosestFragmentIndex(fragmentWorldPositions, attachment.position);
                AttachmentData data = new AttachmentData(
                    attachment.gameObject,
                    sourceRoot.InverseTransformPoint(attachment.position),
                    Quaternion.Inverse(sourceRoot.rotation) * attachment.rotation,
                    CalculateRelativeScale(sourceRoot, attachment)
                );

                map ??= new Dictionary<int, List<AttachmentData>>(fragmentWorldPositions.Length);
                if (!map.TryGetValue(index, out List<AttachmentData> list))
                {
                    list = new List<AttachmentData>();
                    map[index] = list;
                }

                list.Add(data);
            }

            return map;
        }

        public void SpawnAttachmentsForFragment(
            int fragmentIndex,
            Transform fragmentRoot,
            Dictionary<int, List<AttachmentData>> attachmentMap)
        {
            if (fragmentRoot == null || attachmentMap == null)
                return;

            if (!attachmentMap.TryGetValue(fragmentIndex, out List<AttachmentData> attachments))
                return;

            for (int i = 0; i < attachments.Count; i++)
            {
                AttachmentData data = attachments[i];
                if (data.Prefab == null)
                    continue;

                GameObject clone = Instantiate(data.Prefab, fragmentRoot);
                Transform cloneTransform = clone.transform;
                cloneTransform.localPosition = data.LocalPosition;
                cloneTransform.localRotation = data.LocalRotation;
                cloneTransform.localScale = data.LocalScale;
            }
        }

        private static int FindClosestFragmentIndex(Vector3[] fragmentWorldPositions, Vector3 targetPosition)
        {
            int bestIndex = 0;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < fragmentWorldPositions.Length; i++)
            {
                float distance = (fragmentWorldPositions[i] - targetPosition).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static Vector3 CalculateRelativeScale(Transform sourceRoot, Transform child)
        {
            Vector3 sourceScale = sourceRoot.lossyScale;
            Vector3 childScale = child.lossyScale;

            return new Vector3(
                SafeDivide(childScale.x, sourceScale.x),
                SafeDivide(childScale.y, sourceScale.y),
                SafeDivide(childScale.z, sourceScale.z)
            );
        }

        private static float SafeDivide(float numerator, float denominator)
        {
            if (Mathf.Approximately(denominator, 0f))
                return numerator;

            return numerator / denominator;
        }
    }
}
