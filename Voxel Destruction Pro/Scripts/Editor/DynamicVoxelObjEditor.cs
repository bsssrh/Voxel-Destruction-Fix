#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using VoxelDestructionPro.Tools;
using VoxelDestructionPro.VoxelObjects;

namespace VoxelDestructionPro.Editor
{
    [CustomEditor(typeof(DynamicVoxelObj))]
    public class DynamicVoxelObjEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            DynamicVoxelObj voxelObj = (DynamicVoxelObj)target;

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (voxelObj.GetComponent<CompoundBoxColliderManager>() == null)
            {
                if (GUILayout.Button("Add Compound Box Collider Manager"))
                {
                    Undo.AddComponent<CompoundBoxColliderManager>(voxelObj.gameObject);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("CompoundBoxColliderManager is already attached.", MessageType.Info);
            }

            EditorGUILayout.EndHorizontal();
        }
    }
}
#endif
