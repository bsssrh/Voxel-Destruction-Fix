using UnityEngine;

namespace VoxelDestructionPro.Minimal
{
    public class MinimalFragmentSpawner : MonoBehaviour
    {
        [SerializeField] private GameObject fragmentPrefab;
        [SerializeField] private Transform fragmentParent;

        public MinimalVoxelObject SpawnFragment(MinimalVoxelData data, Transform sourceTransform, Vector3 localOffset)
        {
            if (data == null)
                return null;

            Transform parent = fragmentParent != null ? fragmentParent : sourceTransform.parent;
            GameObject instance = fragmentPrefab != null
                ? Instantiate(fragmentPrefab)
                : new GameObject("MinimalFragment");

            instance.transform.SetParent(parent, worldPositionStays: false);
            instance.transform.position = sourceTransform.TransformPoint(localOffset);
            instance.transform.rotation = sourceTransform.rotation;
            instance.transform.localScale = sourceTransform.localScale;

            MinimalVoxelObject voxelObject = instance.GetComponent<MinimalVoxelObject>();
            if (voxelObject == null)
                voxelObject = instance.AddComponent<MinimalVoxelObject>();

            voxelObject.AssignVoxelData(data);
            return voxelObject;
        }
    }
}
