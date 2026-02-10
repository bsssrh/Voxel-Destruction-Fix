#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using VoxelDestructionPro.Tools;

namespace VoxelDestructionPro.Editor
{
    [CustomEditor(typeof(VoxSceneMeshLoader))]
    public class VoxSceneMeshLoaderEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(8);

            if (GUILayout.Button("Load Unloaded Meshes"))
            {
                var loader = (VoxSceneMeshLoader)target;
                loader.LoadUnloadedMeshes();
                EditorUtility.SetDirty(loader);
            }
        }
    }
}
#endif
