using System.Collections.Generic;
using System.Linq;
using VoxReader.Exceptions;
using VoxReader.Interfaces;
using UnityEngine;

namespace VoxReader
{
    internal static class Helper
    {
        internal static char[] GetCharArray(byte[] data, int startIndex, int length)
        {
            var array = new char[length];

            for (int i = 0; i < array.Length; i++)
            {
                array[i] = (char)data[i + startIndex];
            }

            return array;
        }

        public static IEnumerable<IModel> ExtractModels(IChunk mainChunk, IPalette palette, bool applyTransformRotation)
        {
            var sizeChunks = mainChunk.GetChildren<ISizeChunk>();
            var voxelChunks = mainChunk.GetChildren<IVoxelChunk>();

            if (sizeChunks.Length != voxelChunks.Length)
                throw new InvalidDataException("Can not extract models, because the number of SIZE chunks does not match the number of XYZI chunks!");

            var shapeNodeChunks = mainChunk.GetChildren<IShapeNodeChunk>();
            var transformNodeChunks = mainChunk.GetChildren<ITransformNodeChunk>();
            var groupNodeChunks = mainChunk.GetChildren<IGroupNodeChunk>();

            var allNodes = new Dictionary<int, INodeChunk>();
            foreach (ITransformNodeChunk t in transformNodeChunks)
                allNodes.Add(t.NodeId, t);
            foreach (IGroupNodeChunk g in groupNodeChunks)
                allNodes.Add(g.NodeId, g);
            foreach (IShapeNodeChunk s in shapeNodeChunks)
                allNodes.Add(s.NodeId, s);

            var transformNodesThatHaveAShapeNode = new Dictionary<ITransformNodeChunk, IShapeNodeChunk>();
            foreach (ITransformNodeChunk transformNodeChunk in transformNodeChunks)
            {
                foreach (IShapeNodeChunk shapeNodeChunk in shapeNodeChunks)
                {
                    if (transformNodeChunk.ChildNodeId != shapeNodeChunk.NodeId)
                        continue;

                    transformNodesThatHaveAShapeNode.Add(transformNodeChunk, shapeNodeChunk);
                    break;
                }
            }

            var processedModelIds = new HashSet<int>();

            foreach (var keyValuePair in transformNodesThatHaveAShapeNode)
            {
                ITransformNodeChunk transformNodeChunk = keyValuePair.Key;
                IShapeNodeChunk shapeNodeChunk = keyValuePair.Value;

                int[] ids = shapeNodeChunk.Models;

                foreach (int id in ids)
                {
                    string name = transformNodeChunk.Name;
                    Vector3 size = sizeChunks[id].Size;
                    Vector3 position = GetGlobalTranslation(transformNodeChunk);
                    byte rotation = transformNodeChunk.Frames[0].Rotation;
                    bool hasRotation = applyTransformRotation && rotation != 0;
                    RotationAxes rotationAxes = hasRotation ? DecodeRotationAxes(rotation) : RotationAxes.Identity;
                    Vector3 rotatedSize = hasRotation ? GetRotatedSize(size, rotationAxes) : size;
                    Vector3 pivot = hasRotation ? (size - Vector3.one) / 2f : Vector3.zero;
                    Vector3 rotatedPivot = hasRotation ? (rotatedSize - Vector3.one) / 2f : Vector3.zero;

                    var voxels = voxelChunks[id].Voxels.Select(voxel =>
                    {
                        Vector3 localPosition = voxel.Position;

                        if (hasRotation)
                        {
                            Vector3 centered = localPosition - pivot;
                            Vector3 rotated = ApplyRotation(centered, rotationAxes);
                            localPosition = rotated + rotatedPivot;
                        }

                        Vector3 globalPosition = position + localPosition - rotatedSize / 2f;
                        return new Voxel(localPosition, globalPosition, palette.Colors[voxel.ColorIndex - 1]);
                    }).ToArray();

                    // Create new model
                    var model = new Model(id, name, position, rotatedSize, voxels, !processedModelIds.Add(id));
                    yield return model;
                }
            }

            Vector3 GetGlobalTranslation(ITransformNodeChunk target)
            {
                Vector3 position = target.Frames[0].Translation;

                while (TryGetParentTransformNodeChunk(target, out ITransformNodeChunk parent))
                {
                    position += parent.Frames[0].Translation;

                    target = parent;
                }

                return position;
            }

            bool TryGetParentTransformNodeChunk(ITransformNodeChunk target, out ITransformNodeChunk parent)
            {
                //TODO: performance here is questionable; might need an additional scene structure to query the parent efficiently
                foreach (IGroupNodeChunk groupNodeChunk in groupNodeChunks)
                {
                    foreach (int parentGroupNodeChunkChildId in groupNodeChunk.ChildrenNodes)
                    {
                        if (parentGroupNodeChunkChildId != target.NodeId)
                            continue;

                        foreach (ITransformNodeChunk transformNodeChunk in transformNodeChunks)
                        {
                            if (transformNodeChunk.ChildNodeId != groupNodeChunk.NodeId)
                                continue;

                            parent = transformNodeChunk;
                            return true;
                        }
                    }
                }

                parent = null;
                return false;
            }
        }

        private static Vector3 ApplyRotation(Vector3 vector, RotationAxes axes)
        {
            return new Vector3(
                axes.XAxis.x * vector.x + axes.YAxis.x * vector.y + axes.ZAxis.x * vector.z,
                axes.XAxis.y * vector.x + axes.YAxis.y * vector.y + axes.ZAxis.y * vector.z,
                axes.XAxis.z * vector.x + axes.YAxis.z * vector.y + axes.ZAxis.z * vector.z);
        }

        private static Vector3 GetRotatedSize(Vector3 size, RotationAxes axes)
        {
            return new Vector3(
                Mathf.Abs(axes.XAxis.x) * size.x + Mathf.Abs(axes.YAxis.x) * size.y + Mathf.Abs(axes.ZAxis.x) * size.z,
                Mathf.Abs(axes.XAxis.y) * size.x + Mathf.Abs(axes.YAxis.y) * size.y + Mathf.Abs(axes.ZAxis.y) * size.z,
                Mathf.Abs(axes.XAxis.z) * size.x + Mathf.Abs(axes.YAxis.z) * size.y + Mathf.Abs(axes.ZAxis.z) * size.z);
        }

        private static RotationAxes DecodeRotationAxes(byte rotation)
        {
            int xAxisIndex = rotation & 0x03;
            int zAxisIndex = (rotation >> 2) & 0x03;
            int yAxisIndex = 3 - xAxisIndex - zAxisIndex;

            if (xAxisIndex == zAxisIndex || xAxisIndex > 2 || zAxisIndex > 2 || yAxisIndex < 0 || yAxisIndex > 2)
                return RotationAxes.Identity;

            Vector3Int[] axisVectors =
            {
                new(1, 0, 0),
                new(0, 1, 0),
                new(0, 0, 1)
            };

            Vector3Int xAxis = axisVectors[xAxisIndex];
            Vector3Int yAxis = axisVectors[yAxisIndex];
            Vector3Int zAxis = new(
                xAxis.y * yAxis.z - xAxis.z * yAxis.y,
                xAxis.z * yAxis.x - xAxis.x * yAxis.z,
                xAxis.x * yAxis.y - xAxis.y * yAxis.x);

            int xSign = ((rotation >> 4) & 0x01) == 1 ? -1 : 1;
            int ySign = ((rotation >> 5) & 0x01) == 1 ? -1 : 1;
            int zSign = ((rotation >> 6) & 0x01) == 1 ? -1 : 1;

            xAxis *= xSign;
            yAxis *= ySign;
            zAxis *= zSign;

            return new RotationAxes(xAxis, yAxis, zAxis);
        }

        private readonly struct RotationAxes
        {
            public static readonly RotationAxes Identity = new(
                new Vector3Int(1, 0, 0),
                new Vector3Int(0, 1, 0),
                new Vector3Int(0, 0, 1));

            public RotationAxes(Vector3Int xAxis, Vector3Int yAxis, Vector3Int zAxis)
            {
                XAxis = xAxis;
                YAxis = yAxis;
                ZAxis = zAxis;
            }

            public Vector3Int XAxis { get; }
            public Vector3Int YAxis { get; }
            public Vector3Int ZAxis { get; }
        }
    }
}
