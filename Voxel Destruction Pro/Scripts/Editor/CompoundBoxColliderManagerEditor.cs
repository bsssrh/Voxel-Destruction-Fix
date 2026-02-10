#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using VoxelDestructionPro.Tools;

namespace VoxelDestructionPro.Editor
{
    [CustomEditor(typeof(CompoundBoxColliderManager))]
    [CanEditMultipleObjects]
    public class CompoundBoxColliderManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Bake Colliders"))
            {
                foreach (Object targetObject in targets)
                {
                    CompoundBoxColliderManager manager = (CompoundBoxColliderManager)targetObject;
                    Undo.RecordObject(manager, "Bake Compound Colliders");
                    manager.BakeInEditor();
                    EditorUtility.SetDirty(manager);
                }
            }

            if (GUILayout.Button("Clear Colliders"))
            {
                foreach (Object targetObject in targets)
                {
                    CompoundBoxColliderManager manager = (CompoundBoxColliderManager)targetObject;
                    Undo.RecordObject(manager, "Clear Compound Colliders");
                    manager.ClearBakedColliders();
                    EditorUtility.SetDirty(manager);
                }
            }

            if (GUILayout.Button("Rebuild Now"))
            {
                foreach (Object targetObject in targets)
                {
                    CompoundBoxColliderManager manager = (CompoundBoxColliderManager)targetObject;
                    Undo.RecordObject(manager, "Rebuild Compound Colliders");
                    manager.RebuildNow(true);
                    EditorUtility.SetDirty(manager);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
