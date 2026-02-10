using System;

namespace VoxelDestructionPro.Minimal
{
    [Serializable]
    public struct MinimalVoxel : IEquatable<MinimalVoxel>
    {
        public bool active;
        public int colorIndex;
        public int normal;

        public MinimalVoxel(bool active, int colorIndex)
        {
            this.active = active;
            this.colorIndex = colorIndex;
            normal = 0;
        }

        public bool Equals(MinimalVoxel other)
        {
            return active == other.active && colorIndex == other.colorIndex && normal == other.normal;
        }

        public override bool Equals(object obj)
        {
            return obj is MinimalVoxel other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + active.GetHashCode();
                hash = hash * 23 + colorIndex.GetHashCode();
                hash = hash * 23 + normal.GetHashCode();
                return hash;
            }
        }
    }
}
