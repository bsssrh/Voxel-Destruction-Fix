using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VoxReader.Interfaces;

namespace VoxelDestructionPro.Minimal
{
    [Serializable]
    public class MinimalVoxelData
    {
        public MinimalVoxel[] voxels;
        public Color[] palette;
        public Vector3Int length;

        public int Volume => length.x * length.y * length.z;

        public MinimalVoxelData(MinimalVoxel[] voxels, Color[] palette, Vector3Int length)
        {
            this.voxels = voxels;
            this.palette = palette;
            this.length = length;
        }

        public bool InBounds(int x, int y, int z)
        {
            return x >= 0 && y >= 0 && z >= 0 && x < length.x && y < length.y && z < length.z;
        }

        public int ToIndex(int x, int y, int z)
        {
            return x + length.x * (y + length.y * z);
        }

        public void ToCoords(int index, out int x, out int y, out int z)
        {
            x = index % length.x;
            int yIndex = index / length.x;
            y = yIndex % length.y;
            z = yIndex / length.y;
        }

        public MinimalVoxel GetVoxel(int x, int y, int z)
        {
            if (!InBounds(x, y, z))
                return default;

            return voxels[ToIndex(x, y, z)];
        }

        public void SetVoxel(int x, int y, int z, MinimalVoxel voxel)
        {
            if (!InBounds(x, y, z))
                return;

            voxels[ToIndex(x, y, z)] = voxel;
        }

        public bool IsEmpty()
        {
            for (int i = 0; i < voxels.Length; i++)
            {
                if (voxels[i].active)
                    return false;
            }

            return true;
        }

        public static MinimalVoxelData FromModel(IModel model)
        {
            if (model == null)
                return null;

            Vector3Int size = Vector3Int.FloorToInt(model.Size);
            List<Color> colors = new List<Color>();

            for (int i = 0; i < model.Voxels.Length; i++)
            {
                if (!colors.Contains(model.Voxels[i].Color))
                    colors.Add(model.Voxels[i].Color);
            }

            Color[] palette = colors.Select(t => new Color(t.r / 255f, t.g / 255f, t.b / 255f, 1f)).ToArray();
            MinimalVoxel[] voxels = new MinimalVoxel[size.x * size.y * size.z];

            for (int i = 0; i < model.Voxels.Length; i++)
            {
                Vector3 pos = model.Voxels[i].Position;
                int x = Mathf.FloorToInt(pos.x);
                int y = Mathf.FloorToInt(pos.y);
                int z = Mathf.FloorToInt(pos.z);

                if (x < 0 || y < 0 || z < 0 || x >= size.x || y >= size.y || z >= size.z)
                    continue;

                int colorIndex = colors.IndexOf(model.Voxels[i].Color);
                voxels[x + size.x * (y + size.y * z)] = new MinimalVoxel(true, colorIndex);
            }

            return new MinimalVoxelData(voxels, palette, size);
        }
    }
}
