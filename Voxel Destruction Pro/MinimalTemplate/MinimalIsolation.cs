using System.Collections.Generic;
using UnityEngine;

namespace VoxelDestructionPro.Minimal
{
    public class MinimalIsolation : MonoBehaviour
    {
        [SerializeField] private MinimalVoxelObject targetObject;
        [SerializeField] private MinimalFragmentSpawner fragmentSpawner;

        public void Isolate()
        {
            if (targetObject == null)
                targetObject = GetComponent<MinimalVoxelObject>();

            if (fragmentSpawner == null)
                fragmentSpawner = GetComponent<MinimalFragmentSpawner>();

            if (targetObject == null || fragmentSpawner == null)
                return;

            MinimalVoxelData data = targetObject.VoxelData;
            if (data == null || data.IsEmpty())
                return;

            bool[] visited = new bool[data.Volume];
            List<List<int>> clusters = new List<List<int>>();
            int maxClusterIndex = -1;
            int maxClusterSize = 0;

            for (int i = 0; i < data.voxels.Length; i++)
            {
                if (visited[i] || !data.voxels[i].active)
                    continue;

                List<int> cluster = FloodFill(data, i, visited);
                clusters.Add(cluster);

                if (cluster.Count > maxClusterSize)
                {
                    maxClusterSize = cluster.Count;
                    maxClusterIndex = clusters.Count - 1;
                }
            }

            if (clusters.Count <= 1)
                return;

            for (int c = 0; c < clusters.Count; c++)
            {
                if (c == maxClusterIndex)
                    continue;

                List<int> cluster = clusters[c];
                MinimalVoxelData fragmentData = ExtractCluster(data, cluster, out Vector3 localOffset);
                if (fragmentData == null)
                    continue;

                fragmentSpawner.SpawnFragment(fragmentData, targetObject.transform, localOffset);
            }

            targetObject.RebuildMesh();
        }

        private List<int> FloodFill(MinimalVoxelData data, int startIndex, bool[] visited)
        {
            List<int> cluster = new List<int>();
            Queue<int> queue = new Queue<int>();
            queue.Enqueue(startIndex);
            visited[startIndex] = true;

            while (queue.Count > 0)
            {
                int index = queue.Dequeue();
                cluster.Add(index);

                data.ToCoords(index, out int x, out int y, out int z);
                EnqueueIfActive(data, visited, queue, x + 1, y, z);
                EnqueueIfActive(data, visited, queue, x - 1, y, z);
                EnqueueIfActive(data, visited, queue, x, y + 1, z);
                EnqueueIfActive(data, visited, queue, x, y - 1, z);
                EnqueueIfActive(data, visited, queue, x, y, z + 1);
                EnqueueIfActive(data, visited, queue, x, y, z - 1);
            }

            return cluster;
        }

        private void EnqueueIfActive(MinimalVoxelData data, bool[] visited, Queue<int> queue, int x, int y, int z)
        {
            if (!data.InBounds(x, y, z))
                return;

            int index = data.ToIndex(x, y, z);
            if (visited[index])
                return;

            if (!data.voxels[index].active)
                return;

            visited[index] = true;
            queue.Enqueue(index);
        }

        private MinimalVoxelData ExtractCluster(MinimalVoxelData source, List<int> cluster, out Vector3 localOffset)
        {
            localOffset = Vector3.zero;

            if (cluster == null || cluster.Count == 0)
                return null;

            Vector3Int min = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
            Vector3Int max = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);

            for (int i = 0; i < cluster.Count; i++)
            {
                source.ToCoords(cluster[i], out int x, out int y, out int z);
                min = Vector3Int.Min(min, new Vector3Int(x, y, z));
                max = Vector3Int.Max(max, new Vector3Int(x, y, z));
            }

            Vector3Int size = max - min + Vector3Int.one;
            MinimalVoxel[] voxels = new MinimalVoxel[size.x * size.y * size.z];

            for (int i = 0; i < cluster.Count; i++)
            {
                source.ToCoords(cluster[i], out int x, out int y, out int z);
                MinimalVoxel voxel = source.GetVoxel(x, y, z);
                voxel.active = true;

                int localX = x - min.x;
                int localY = y - min.y;
                int localZ = z - min.z;
                voxels[localX + size.x * (localY + size.y * localZ)] = voxel;

                MinimalVoxel empty = voxel;
                empty.active = false;
                source.SetVoxel(x, y, z, empty);
            }

            localOffset = new Vector3(min.x, min.y, min.z);
            return new MinimalVoxelData(voxels, source.palette, size);
        }
    }
}
