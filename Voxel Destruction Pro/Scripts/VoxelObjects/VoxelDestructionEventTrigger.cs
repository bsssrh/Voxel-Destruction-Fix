using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Events;
using Unity.Mathematics;

namespace VoxelDestructionPro.VoxelObjects
{
    /// <summary>
    /// Triggers UnityEvents when specific voxels (or voxel groups) are destroyed.
    /// </summary>
    public class VoxelDestructionEventTrigger : MonoBehaviour
    {
        public enum GroupTriggerMode
        {
            Any,
            All
        }

        [Serializable]
        public class VoxelEvent
        {
            public string label;
            public Vector3Int voxelIndex;
            public UnityEvent onDestroyed;

            [NonSerialized]
            public bool triggered;
            [NonSerialized]
            public int cachedIndex = -1;
        }

        [Serializable]
        public class VoxelGroupEvent
        {
            public string label;
            public GroupTriggerMode triggerMode = GroupTriggerMode.All;
            public List<Vector3Int> voxelIndices = new List<Vector3Int>();
            public UnityEvent onGroupDestroyed;

            [NonSerialized]
            public bool triggered;
            [NonSerialized]
            public HashSet<int> remainingIndices;
        }

        [Header("Target")]
        public DynamicVoxelObj target;

        [Header("Single Voxels")]
        public List<VoxelEvent> voxelEvents = new List<VoxelEvent>();

        [Header("Voxel Groups")]
        public List<VoxelGroupEvent> groupEvents = new List<VoxelGroupEvent>();

        [Header("Gizmos")]
        public bool showGizmos = true;
        public Color singleVoxelColor = new Color(0.2f, 0.9f, 0.3f, 0.8f);
        public Color groupVoxelColor = new Color(1f, 0.7f, 0.1f, 0.6f);

        private readonly Dictionary<int, List<VoxelEvent>> voxelEventLookup = new Dictionary<int, List<VoxelEvent>>();
        private readonly Dictionary<int, bool> trackedIndexStates = new Dictionary<int, bool>();
        private readonly List<int> trackedIndices = new List<int>();

        private void Reset()
        {
            target = GetComponent<DynamicVoxelObj>();
        }

        private void OnEnable()
        {
            EnsureTarget();
            Subscribe();
            RefreshCache();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnValidate()
        {
            RefreshCache();
        }

        private void EnsureTarget()
        {
            if (target == null)
                target = GetComponent<DynamicVoxelObj>();
        }

        private void Subscribe()
        {
            if (target == null)
                return;

            target.onBeforeVoxelsRemoved += HandleVoxelsRemoved;
            target.onVoxelsRemoved += HandleVoxelsRemoved;
            target.onVoxeldataChanged += HandleVoxelDataChanged;
        }

        private void Unsubscribe()
        {
            if (target == null)
                return;

            target.onBeforeVoxelsRemoved -= HandleVoxelsRemoved;
            target.onVoxelsRemoved -= HandleVoxelsRemoved;
            target.onVoxeldataChanged -= HandleVoxelDataChanged;
        }

        private void HandleVoxelDataChanged(VoxelDestructionPro.Data.VoxelData data)
        {
            TriggerDeactivatedVoxelEvents(data);
            RefreshCache(false);
        }

        public void RefreshCache(bool resetTriggers = true)
        {
            EnsureTarget();
            voxelEventLookup.Clear();
            trackedIndexStates.Clear();
            trackedIndices.Clear();

            if (target == null || target.CurrentVoxelData == null)
                return;

            int3 length = target.VoxelDataLength;
            var voxels = target.CurrentVoxelData.voxels;

            for (int i = 0; i < voxelEvents.Count; i++)
            {
                VoxelEvent entry = voxelEvents[i];
                if (resetTriggers)
                    entry.triggered = false;
                entry.cachedIndex = -1;

                if (!IsValidIndex(entry.voxelIndex, length))
                    continue;

                int index = To1D(entry.voxelIndex, length);
                entry.cachedIndex = index;

                TrackIndexState(index, voxels);

                if (!voxelEventLookup.TryGetValue(index, out List<VoxelEvent> list))
                {
                    list = new List<VoxelEvent>();
                    voxelEventLookup.Add(index, list);
                }

                list.Add(entry);
            }

            for (int i = 0; i < groupEvents.Count; i++)
            {
                VoxelGroupEvent group = groupEvents[i];
                if (resetTriggers)
                    group.triggered = false;
                group.remainingIndices = new HashSet<int>();

                for (int v = 0; v < group.voxelIndices.Count; v++)
                {
                    Vector3Int voxel = group.voxelIndices[v];
                    if (!IsValidIndex(voxel, length))
                        continue;

                    int index = To1D(voxel, length);
                    group.remainingIndices.Add(index);
                    TrackIndexState(index, voxels);
                }
            }
        }

        private void HandleVoxelsRemoved(NativeList<int> removedVoxels)
        {
            if (removedVoxels.IsCreated == false)
                return;

            for (int i = 0; i < removedVoxels.Length; i++)
            {
                int removedIndex = removedVoxels[i];
                TriggerSingleVoxelEvents(removedIndex);
                TriggerGroupEvents(removedIndex);
                MarkIndexInactive(removedIndex);
            }
        }

        private void TriggerSingleVoxelEvents(int removedIndex)
        {
            if (!voxelEventLookup.TryGetValue(removedIndex, out List<VoxelEvent> events))
                return;

            for (int i = 0; i < events.Count; i++)
            {
                VoxelEvent entry = events[i];
                if (entry.triggered)
                    continue;

                entry.triggered = true;
                entry.onDestroyed?.Invoke();
            }
        }

        private void TriggerDeactivatedVoxelEvents(VoxelDestructionPro.Data.VoxelData data)
        {
            if (data == null || trackedIndices.Count == 0)
                return;

            int3 length = data.length;

            for (int i = 0; i < trackedIndices.Count; i++)
            {
                int index = trackedIndices[i];
                if (!trackedIndexStates.TryGetValue(index, out bool wasActive))
                    continue;

                bool isActive = IsIndexActive(index, length, data);
                if (wasActive && !isActive)
                {
                    TriggerSingleVoxelEvents(index);
                    TriggerGroupEvents(index);
                }

                trackedIndexStates[index] = isActive;
            }
        }

        private void TrackIndexState(int index, NativeArray<VoxelDestructionPro.Data.Voxel> voxels)
        {
            if (trackedIndexStates.ContainsKey(index))
                return;

            bool isActive = index >= 0 && index < voxels.Length && voxels[index].active != 0;
            trackedIndexStates.Add(index, isActive);
            trackedIndices.Add(index);
        }

        private void MarkIndexInactive(int index)
        {
            if (trackedIndexStates.ContainsKey(index))
                trackedIndexStates[index] = false;
        }

        private static bool IsIndexActive(int index, int3 length, VoxelDestructionPro.Data.VoxelData data)
        {
            int volume = length.x * length.y * length.z;
            if (index < 0 || index >= volume || index >= data.voxels.Length)
                return false;

            return data.voxels[index].active != 0;
        }

        private void TriggerGroupEvents(int removedIndex)
        {
            for (int i = 0; i < groupEvents.Count; i++)
            {
                VoxelGroupEvent group = groupEvents[i];
                if (group.triggered || group.remainingIndices == null)
                    continue;

                if (group.triggerMode == GroupTriggerMode.Any)
                {
                    if (group.remainingIndices.Contains(removedIndex))
                    {
                        group.triggered = true;
                        group.onGroupDestroyed?.Invoke();
                    }

                    continue;
                }

                if (group.remainingIndices.Remove(removedIndex) && group.remainingIndices.Count == 0)
                {
                    group.triggered = true;
                    group.onGroupDestroyed?.Invoke();
                }
            }
        }

        private static int To1D(Vector3Int index, int3 length)
        {
            return index.x + length.x * (index.y + length.y * index.z);
        }

        private static bool IsValidIndex(Vector3Int index, int3 length)
        {
            return index.x >= 0 && index.y >= 0 && index.z >= 0 &&
                   index.x < length.x && index.y < length.y && index.z < length.z;
        }

        private void OnDrawGizmosSelected()
        {
            if (!showGizmos)
                return;

            EnsureTarget();

            if (target == null || target.CurrentVoxelData == null)
                return;

            Transform meshTransform = target.targetFilter != null ? target.targetFilter.transform : target.transform;
            float voxelSize = Mathf.Max(0.01f, target.GetSingleVoxelSize());
            Vector3 half = Vector3.one * (voxelSize * 0.5f);

            Gizmos.matrix = meshTransform.localToWorldMatrix;

            Gizmos.color = groupVoxelColor;
            DrawVoxelList(groupEvents, voxelSize, half);

            Gizmos.color = singleVoxelColor;
            for (int i = 0; i < voxelEvents.Count; i++)
            {
                Vector3 localPos = (Vector3)voxelEvents[i].voxelIndex * voxelSize + half;
                Gizmos.DrawWireCube(localPos, Vector3.one * voxelSize);
            }
        }

        private void DrawVoxelList(List<VoxelGroupEvent> groups, float voxelSize, Vector3 half)
        {
            for (int i = 0; i < groups.Count; i++)
            {
                List<Vector3Int> voxels = groups[i].voxelIndices;
                for (int v = 0; v < voxels.Count; v++)
                {
                    Vector3 localPos = (Vector3)voxels[v] * voxelSize + half;
                    Gizmos.DrawWireCube(localPos, Vector3.one * voxelSize);
                }
            }
        }
    }
}
