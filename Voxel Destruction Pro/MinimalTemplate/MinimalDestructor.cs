using UnityEngine;

namespace VoxelDestructionPro.Minimal
{
    public class MinimalDestructor : MonoBehaviour
    {
        [SerializeField] private MinimalVoxelObject targetObject;

        public void DestroySphere(Vector3 worldPosition, float radius)
        {
            if (!EnsureTarget())
                return;

            Vector3 localCenter = targetObject.transform.InverseTransformPoint(worldPosition);
            float localRadius = radius / Mathf.Max(targetObject.transform.lossyScale.x, 0.0001f);

            Vector3Int min = new Vector3Int(
                Mathf.FloorToInt(localCenter.x - localRadius - 0.5f),
                Mathf.FloorToInt(localCenter.y - localRadius - 0.5f),
                Mathf.FloorToInt(localCenter.z - localRadius - 0.5f));
            Vector3Int max = new Vector3Int(
                Mathf.CeilToInt(localCenter.x + localRadius + 0.5f),
                Mathf.CeilToInt(localCenter.y + localRadius + 0.5f),
                Mathf.CeilToInt(localCenter.z + localRadius + 0.5f));

            MinimalVoxelData data = targetObject.VoxelData;
            float radiusSq = localRadius * localRadius;

            for (int z = min.z; z <= max.z; z++)
            {
                for (int y = min.y; y <= max.y; y++)
                {
                    for (int x = min.x; x <= max.x; x++)
                    {
                        if (!data.InBounds(x, y, z))
                            continue;

                        Vector3 voxelCenter = new Vector3(x, y, z);
                        if ((voxelCenter - localCenter).sqrMagnitude > radiusSq)
                            continue;

                        MinimalVoxel voxel = data.GetVoxel(x, y, z);
                        if (!voxel.active)
                            continue;

                        voxel.active = false;
                        data.SetVoxel(x, y, z, voxel);
                    }
                }
            }

            targetObject.RebuildMesh();
        }

        public void DestroyCube(Vector3 worldCenter, Vector3 worldSize)
        {
            if (!EnsureTarget())
                return;

            Vector3 localCenter = targetObject.transform.InverseTransformPoint(worldCenter);
            Vector3 localHalfSize = worldSize * 0.5f;

            Vector3Int min = new Vector3Int(
                Mathf.FloorToInt(localCenter.x - localHalfSize.x - 0.5f),
                Mathf.FloorToInt(localCenter.y - localHalfSize.y - 0.5f),
                Mathf.FloorToInt(localCenter.z - localHalfSize.z - 0.5f));
            Vector3Int max = new Vector3Int(
                Mathf.CeilToInt(localCenter.x + localHalfSize.x + 0.5f),
                Mathf.CeilToInt(localCenter.y + localHalfSize.y + 0.5f),
                Mathf.CeilToInt(localCenter.z + localHalfSize.z + 0.5f));

            MinimalVoxelData data = targetObject.VoxelData;

            for (int z = min.z; z <= max.z; z++)
            {
                for (int y = min.y; y <= max.y; y++)
                {
                    for (int x = min.x; x <= max.x; x++)
                    {
                        if (!data.InBounds(x, y, z))
                            continue;

                        MinimalVoxel voxel = data.GetVoxel(x, y, z);
                        if (!voxel.active)
                            continue;

                        voxel.active = false;
                        data.SetVoxel(x, y, z, voxel);
                    }
                }
            }

            targetObject.RebuildMesh();
        }

        public void DestroyLine(Vector3 worldStart, Vector3 worldEnd, float radius)
        {
            if (!EnsureTarget())
                return;

            Vector3 localStart = targetObject.transform.InverseTransformPoint(worldStart);
            Vector3 localEnd = targetObject.transform.InverseTransformPoint(worldEnd);
            float localRadius = radius / Mathf.Max(targetObject.transform.lossyScale.x, 0.0001f);

            MinimalVoxelData data = targetObject.VoxelData;

            Vector3Int min = Vector3Int.FloorToInt(Vector3.Min(localStart, localEnd) - Vector3.one * (localRadius + 0.5f));
            Vector3Int max = Vector3Int.CeilToInt(Vector3.Max(localStart, localEnd) + Vector3.one * (localRadius + 0.5f));

            float radiusSq = localRadius * localRadius;

            for (int z = min.z; z <= max.z; z++)
            {
                for (int y = min.y; y <= max.y; y++)
                {
                    for (int x = min.x; x <= max.x; x++)
                    {
                        if (!data.InBounds(x, y, z))
                            continue;

                        Vector3 voxelCenter = new Vector3(x, y, z);
                        float distance = DistancePointToSegmentSquared(voxelCenter, localStart, localEnd);
                        if (distance > radiusSq)
                            continue;

                        MinimalVoxel voxel = data.GetVoxel(x, y, z);
                        if (!voxel.active)
                            continue;

                        voxel.active = false;
                        data.SetVoxel(x, y, z, voxel);
                    }
                }
            }

            targetObject.RebuildMesh();
        }

        private bool EnsureTarget()
        {
            if (targetObject == null)
                targetObject = GetComponent<MinimalVoxelObject>();

            return targetObject != null && targetObject.VoxelData != null;
        }

        private float DistancePointToSegmentSquared(Vector3 point, Vector3 start, Vector3 end)
        {
            Vector3 segment = end - start;
            float lengthSq = segment.sqrMagnitude;
            if (lengthSq <= Mathf.Epsilon)
                return (point - start).sqrMagnitude;

            float t = Vector3.Dot(point - start, segment) / lengthSq;
            t = Mathf.Clamp01(t);
            Vector3 projection = start + segment * t;
            return (point - projection).sqrMagnitude;
        }
    }
}
