using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using VoxelDestructionPro.VoxelObjects;

namespace VoxelDestructionPro.Editor
{
    [CustomEditor(typeof(VoxelDestructionEventTrigger))]
    public class VoxelDestructionEventTriggerEditor : UnityEditor.Editor
    {
        private SerializedProperty targetProp;
        private SerializedProperty voxelEventsProp;
        private SerializedProperty groupEventsProp;
        private SerializedProperty showGizmosProp;
        private SerializedProperty singleVoxelColorProp;
        private SerializedProperty groupVoxelColorProp;
        private ReorderableList voxelEventList;
        private ReorderableList groupEventList;

        private void OnEnable()
        {
            targetProp = serializedObject.FindProperty("target");
            voxelEventsProp = serializedObject.FindProperty("voxelEvents");
            groupEventsProp = serializedObject.FindProperty("groupEvents");
            showGizmosProp = serializedObject.FindProperty("showGizmos");
            singleVoxelColorProp = serializedObject.FindProperty("singleVoxelColor");
            groupVoxelColorProp = serializedObject.FindProperty("groupVoxelColor");

            voxelEventList = CreateList(voxelEventsProp, "Single Voxels");
            groupEventList = CreateList(groupEventsProp, "Voxel Groups");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(targetProp);
            EditorGUILayout.Space();

            voxelEventList.DoLayoutList();
            EditorGUILayout.Space();
            groupEventList.DoLayoutList();
            EditorGUILayout.Space();

            EditorGUILayout.PropertyField(showGizmosProp);
            EditorGUILayout.PropertyField(singleVoxelColorProp);
            EditorGUILayout.PropertyField(groupVoxelColorProp);

            if (GUILayout.Button("Refresh Voxel Indices"))
            {
                foreach (Object targetObject in targets)
                {
                    if (targetObject is VoxelDestructionEventTrigger trigger)
                    {
                        trigger.RefreshCache();
                        EditorUtility.SetDirty(trigger);
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void OnSceneGUI()
        {
            if (targets.Length != 1)
                return;

            VoxelDestructionEventTrigger trigger = (VoxelDestructionEventTrigger)target;
            if (trigger == null || trigger.showGizmos == false)
                return;

            if (trigger.target == null || trigger.target.CurrentVoxelData == null)
                return;

            Transform meshTransform = trigger.target.targetFilter != null
                ? trigger.target.targetFilter.transform
                : trigger.target.transform;

            float voxelSize = Mathf.Max(0.01f, trigger.target.GetSingleVoxelSize());
            Vector3 half = Vector3.one * (voxelSize * 0.5f);

            Handles.matrix = meshTransform.localToWorldMatrix;

            if (groupEventList != null && groupEventList.index >= 0)
            {
                Handles.color = trigger.groupVoxelColor;
                DrawGroupSelection(trigger, groupEventList.index, voxelSize, half);
            }

            if (voxelEventList != null && voxelEventList.index >= 0)
            {
                Handles.color = trigger.singleVoxelColor;
                DrawSingleSelection(trigger, voxelEventList.index, voxelSize, half);
            }
        }

        private static ReorderableList CreateList(SerializedProperty property, string title)
        {
            ReorderableList list = new ReorderableList(property.serializedObject, property, true, true, true, true);
            list.drawHeaderCallback = rect => EditorGUI.LabelField(rect, title);
            list.onSelectCallback = _ => SceneView.RepaintAll();
            list.elementHeightCallback = index =>
            {
                SerializedProperty element = property.GetArrayElementAtIndex(index);
                return EditorGUI.GetPropertyHeight(element, true) + 4f;
            };
            list.drawElementCallback = (rect, index, _, __) =>
            {
                SerializedProperty element = property.GetArrayElementAtIndex(index);
                rect.y += 2f;
                rect.height = EditorGUI.GetPropertyHeight(element, true);
                EditorGUI.PropertyField(rect, element, GUIContent.none, true);
            };
            return list;
        }

        private static void DrawSingleSelection(VoxelDestructionEventTrigger trigger, int index, float voxelSize, Vector3 half)
        {
            if (index < 0 || index >= trigger.voxelEvents.Count)
                return;

            Vector3 localPos = (Vector3)trigger.voxelEvents[index].voxelIndex * voxelSize + half;
            Handles.DrawWireCube(localPos, Vector3.one * voxelSize);
        }

        private static void DrawGroupSelection(VoxelDestructionEventTrigger trigger, int index, float voxelSize, Vector3 half)
        {
            if (index < 0 || index >= trigger.groupEvents.Count)
                return;

            var voxels = trigger.groupEvents[index].voxelIndices;
            for (int i = 0; i < voxels.Count; i++)
            {
                Vector3 localPos = (Vector3)voxels[i] * voxelSize + half;
                Handles.DrawWireCube(localPos, Vector3.one * voxelSize);
            }
        }
    }
}
