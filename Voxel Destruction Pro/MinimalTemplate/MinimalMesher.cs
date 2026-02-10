using System.Collections.Generic;
using UnityEngine;

namespace VoxelDestructionPro.Minimal
{
    public static class MinimalMesher
    {
        public static Mesh BuildMesh(MinimalVoxelData data, float voxelSize)
        {
            Mesh mesh = new Mesh
            {
                name = "MinimalVoxelMesh"
            };

            if (data == null || data.voxels == null || data.voxels.Length == 0)
                return mesh;

            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector3> normals = new List<Vector3>();
            List<Color> colors = new List<Color>();

            int[] dims = { data.length.x, data.length.y, data.length.z };
            int maxMaskSize = Mathf.Max(dims[0] * dims[1], dims[1] * dims[2], dims[0] * dims[2]);
            MinimalVoxel[] mask = new MinimalVoxel[maxMaskSize];
            MinimalVoxel empty = new MinimalVoxel(false, 0);
            Vector3 blockMinVertex = new Vector3(-0.5f, -0.5f, -0.5f);

            for (int d = 0; d < 3; d++)
            {
                int u = (d + 1) % 3;
                int v = (d + 2) % 3;
                int[] x = new int[3];
                int[] q = new int[3];
                q[d] = 1;

                for (x[d] = -1; x[d] < dims[d];)
                {
                    int n = 0;
                    for (x[v] = 0; x[v] < dims[v]; ++x[v])
                    {
                        for (x[u] = 0; x[u] < dims[u]; ++x[u])
                        {
                            MinimalVoxel blockCurrent = x[d] >= 0
                                ? GetVoxel(data, x[0], x[1], x[2])
                                : empty;
                            MinimalVoxel blockCompare = x[d] < dims[d] - 1
                                ? GetVoxel(data, x[0] + q[0], x[1] + q[1], x[2] + q[2])
                                : empty;

                            if ((blockCurrent.active == blockCompare.active)
                                || (blockCurrent.active && blockCompare.active))
                            {
                                mask[n++] = empty;
                            }
                            else if (!blockCurrent.active)
                            {
                                blockCompare.normal = 1;
                                mask[n++] = blockCompare;
                            }
                            else
                            {
                                blockCurrent.normal = 2;
                                mask[n++] = blockCurrent;
                            }
                        }
                    }

                    x[d]++;
                    n = 0;

                    for (int j = 0; j < dims[v]; j++)
                    {
                        for (int i = 0; i < dims[u];)
                        {
                            if (mask[n].active || mask[n].normal != 0)
                            {
                                int w = 1;
                                int h = 1;

                                for (; i + w < dims[u] && mask[n + w].Equals(mask[n]); w++)
                                {
                                }

                                bool done = false;
                                for (; j + h < dims[v]; h++)
                                {
                                    for (int k = 0; k < w; ++k)
                                    {
                                        if (mask[n + k + h * dims[u]].active || !mask[n + k + h * dims[u]].Equals(mask[n]))
                                        {
                                            done = true;
                                            break;
                                        }
                                    }

                                    if (done)
                                        break;
                                }

                                x[u] = i;
                                x[v] = j;

                                int[] du = new int[3];
                                du[u] = w;

                                int[] dv = new int[3];
                                dv[v] = h;

                                AddToMesh(
                                    vertices,
                                    triangles,
                                    normals,
                                    colors,
                                    blockMinVertex + new Vector3(x[0], x[1], x[2]),
                                    blockMinVertex + new Vector3(x[0] + du[0], x[1] + du[1], x[2] + du[2]),
                                    blockMinVertex + new Vector3(x[0] + du[0] + dv[0], x[1] + du[1] + dv[1], x[2] + du[2] + dv[2]),
                                    blockMinVertex + new Vector3(x[0] + dv[0], x[1] + dv[1], x[2] + dv[2]),
                                    mask[n],
                                    AxisMask(d),
                                    data.palette,
                                    voxelSize
                                );

                                for (int l = 0; l < h; ++l)
                                    for (int k = 0; k < w; ++k)
                                        mask[n + k + l * dims[u]] = empty;

                                i += w;
                                n += w;
                            }
                            else
                            {
                                i++;
                                n++;
                            }
                        }
                    }
                }
            }

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.RecalculateBounds();

            return mesh;
        }

        private static MinimalVoxel GetVoxel(MinimalVoxelData data, int x, int y, int z)
        {
            return data.voxels[x + data.length.x * (y + data.length.y * z)];
        }

        private static Vector3 AxisMask(int d)
        {
            switch (d)
            {
                case 0:
                    return new Vector3(1f, 0f, 0f);
                case 1:
                    return new Vector3(0f, 1f, 0f);
                default:
                    return new Vector3(0f, 0f, 1f);
            }
        }

        private static void AddToMesh(
            List<Vector3> vertices,
            List<int> triangles,
            List<Vector3> normals,
            List<Color> colors,
            Vector3 bottomLeft,
            Vector3 topLeft,
            Vector3 topRight,
            Vector3 bottomRight,
            MinimalVoxel voxel,
            Vector3 axisMask,
            Color[] palette,
            float voxelSize)
        {
            if (palette == null || palette.Length == 0)
                return;

            int normal = voxel.normal;
            if (normal == 2)
                normal = -1;

            int vertIndex = vertices.Count;

            triangles.Add(vertIndex);
            triangles.Add(vertIndex + 2 - normal);
            triangles.Add(vertIndex + 2 + normal);
            triangles.Add(vertIndex + 3);
            triangles.Add(vertIndex + 1 + normal);
            triangles.Add(vertIndex + 1 - normal);

            vertices.Add(bottomLeft * voxelSize);
            vertices.Add(bottomRight * voxelSize);
            vertices.Add(topLeft * voxelSize);
            vertices.Add(topRight * voxelSize);

            int paletteIndex = Mathf.Clamp(voxel.colorIndex, 0, palette.Length - 1);
            Color paletteColor = palette[paletteIndex];

            colors.Add(paletteColor);
            colors.Add(paletteColor);
            colors.Add(paletteColor);
            colors.Add(paletteColor);

            Vector3 normalDir = axisMask * -normal;
            normals.Add(normalDir);
            normals.Add(normalDir);
            normals.Add(normalDir);
            normals.Add(normalDir);
        }
    }
}
