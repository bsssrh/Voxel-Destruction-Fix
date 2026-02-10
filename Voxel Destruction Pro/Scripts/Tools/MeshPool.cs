using System.Collections.Generic;
using UnityEngine;

namespace VoxelDestructionPro.Tools
{
    public static class MeshPool
    {
        private static readonly Stack<Mesh> Pool = new Stack<Mesh>();
        public static int MaxPoolSize = 64;

        public static Mesh Acquire()
        {
            while (Pool.Count > 0)
            {
                Mesh mesh = Pool.Pop();
                if (mesh == null)
                    continue;

                mesh.Clear(false);
                mesh.hideFlags = HideFlags.None;
                return mesh;
            }

            Mesh newMesh = new Mesh();
            newMesh.hideFlags = HideFlags.None;
            return newMesh;
        }

        public static void Release(Mesh mesh)
        {
            if (mesh == null)
                return;

            if (Pool.Count >= MaxPoolSize)
            {
                Object.Destroy(mesh);
                return;
            }

            mesh.Clear(false);
            Pool.Push(mesh);
        }
    }
}
