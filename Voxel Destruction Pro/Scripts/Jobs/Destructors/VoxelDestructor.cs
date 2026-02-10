using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VoxelDestructionPro.Data;
using VoxelDestructionPro.Interfaces;
using VoxelDestructionPro.Settings;

namespace VoxelDestructionPro.Jobs.Destruction
{
    public class VoxelDestructor : VoxelJob, IDestructor
    {
        private SphereDestructorJob sphereJob;
        private CubeDestructorJob cubeJob;
        private LineDestructorJob lineJob;
        
        private JobHandle handle;
        private int length;
        private int3 gridSize;
        private int maxCapacitySeen;

        private NativeList<int> outputIndex;
        private NativeArray<byte> destructionFlags;
    
        public VoxelDestructor(int3 length)
        {
            gridSize = length;
            this.length = length.x * length.y * length.z;
            
            outputIndex = new NativeList<int>(Allocator.Persistent);
            destructionFlags = new NativeArray<byte>(this.length, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            sphereJob = new SphereDestructorJob()
            {
                indexLength = length.xy,
            };
            cubeJob = new CubeDestructorJob()
            {
                indexLength = length.xy,
            };
            lineJob = new LineDestructorJob()
            {
                indexLength = length.xy,
            };
        }
    
        public void Prepare(DestructionData data)
        {        
            outputIndex.Clear();
            int estimate = GetCapacityEstimate(data);
            int desiredCapacity = math.max(maxCapacitySeen, estimate);
            if (desiredCapacity > outputIndex.Capacity)
            {
                outputIndex.Capacity = desiredCapacity;
            }
            maxCapacitySeen = desiredCapacity;

            var clearFlagsJob = new ClearDestructionFlagsJob
            {
                flags = destructionFlags,
            };
            JobHandle clearHandle = clearFlagsJob.ScheduleParallel(length, 64, default);

            var gatherJob = new GatherDestructionFlagsJob
            {
                flags = destructionFlags,
                outputIndex = outputIndex,
            };

            if (data.destructionType == DestructionData.DestructionType.Sphere)
            {
                sphereJob.getRadiusSqr = data.range * data.range;
                sphereJob.targetPoint = data.start;
                GetSphereBounds(data.start, data.range, out int3 minBound, out int3 maxBound);
                sphereJob.minBound = minBound;
                sphereJob.maxBound = maxBound;
                sphereJob.flags = destructionFlags;

                int zCount = GetZCount(minBound, maxBound);
                JobHandle destructionHandle = zCount > 0
                    ? sphereJob.ScheduleParallel(zCount, 32, clearHandle)
                    : clearHandle;
                handle = gatherJob.Schedule(destructionHandle);
            }
            else if (data.destructionType == DestructionData.DestructionType.Cube)
            {
                cubeJob.cubeHalfExtend = data.range;
                cubeJob.targetPoint = data.start;
                GetCubeBounds(data.start, data.range, out int3 minBound, out int3 maxBound);
                cubeJob.minBound = minBound;
                cubeJob.maxBound = maxBound;
                cubeJob.flags = destructionFlags;

                int zCount = GetZCount(minBound, maxBound);
                JobHandle destructionHandle = zCount > 0
                    ? cubeJob.ScheduleParallel(zCount, 32, clearHandle)
                    : clearHandle;
                handle = gatherJob.Schedule(destructionHandle);
            }
            else if (data.destructionType == DestructionData.DestructionType.Line)
            {
                lineJob.radiusSqr = data.range * data.range;
                lineJob.startPoint = data.start;
                lineJob.endPoint = data.end;
                lineJob.lineVec = data.end - data.start;
                float lineLenSqr = math.dot(lineJob.lineVec, lineJob.lineVec);
                lineJob.invLenSqr = lineLenSqr > 0f ? 1f / lineLenSqr : 0f;

                GetLineBounds(data.start, data.end, data.range, out int3 minBound, out int3 maxBound);
                lineJob.minBound = minBound;
                lineJob.maxBound = maxBound;
                lineJob.flags = destructionFlags;

                int zCount = GetZCount(minBound, maxBound);
                JobHandle destructionHandle = zCount > 0
                    ? lineJob.ScheduleParallel(zCount, 32, clearHandle)
                    : clearHandle;
                handle = gatherJob.Schedule(destructionHandle);
            }
        }

        public NativeList<int> GetData()
        {
            handle.Complete();
        
            return outputIndex;
        }

        public bool isFinished()
        {
            return handle.IsCompleted;
        }

        protected override void DisposeAll()
        {
            handle.Complete();
            outputIndex.Dispose();
            destructionFlags.Dispose();
        }

        private int GetZCount(int3 minBound, int3 maxBound)
        {
            int count = maxBound.z - minBound.z + 1;
            return count > 0 ? count : 0;
        }

        private void GetSphereBounds(float3 center, float range, out int3 minBound, out int3 maxBound)
        {
            float3 minFloat = center - range;
            float3 maxFloat = center + range;
            minBound = new int3((int)math.floor(minFloat.x), (int)math.floor(minFloat.y), (int)math.floor(minFloat.z));
            maxBound = new int3((int)math.ceil(maxFloat.x), (int)math.ceil(maxFloat.y), (int)math.ceil(maxFloat.z));
            ClampBounds(ref minBound, ref maxBound);
        }

        private void GetCubeBounds(float3 center, float halfExtend, out int3 minBound, out int3 maxBound)
        {
            float3 minFloat = center - halfExtend;
            float3 maxFloat = center + halfExtend;
            minBound = new int3((int)math.floor(minFloat.x), (int)math.floor(minFloat.y), (int)math.floor(minFloat.z));
            maxBound = new int3((int)math.ceil(maxFloat.x), (int)math.ceil(maxFloat.y), (int)math.ceil(maxFloat.z));
            ClampBounds(ref minBound, ref maxBound);
        }

        private void GetLineBounds(float3 start, float3 end, float range, out int3 minBound, out int3 maxBound)
        {
            float3 minPoint = math.min(start, end) - range;
            float3 maxPoint = math.max(start, end) + range;
            minBound = new int3((int)math.floor(minPoint.x), (int)math.floor(minPoint.y), (int)math.floor(minPoint.z));
            maxBound = new int3((int)math.ceil(maxPoint.x), (int)math.ceil(maxPoint.y), (int)math.ceil(maxPoint.z));
            ClampBounds(ref minBound, ref maxBound);
        }

        private void ClampBounds(ref int3 minBound, ref int3 maxBound)
        {
            int3 minClamp = int3.zero;
            int3 maxClamp = gridSize - 1;
            minBound = math.clamp(minBound, minClamp, maxClamp);
            maxBound = math.clamp(maxBound, minClamp, maxClamp);
        }

        private int GetCapacityEstimate(DestructionData data)
        {
            double range = data.range;
            double estimate;

            switch (data.destructionType)
            {
                case DestructionData.DestructionType.Sphere:
                    estimate = (4d / 3d) * math.PI * range * range * range;
                    break;
                case DestructionData.DestructionType.Cube:
                    estimate = 2d * range;
                    estimate = estimate * estimate * estimate;
                    break;
                case DestructionData.DestructionType.Line:
                    double lineLength = math.distance(data.start, data.end);
                    estimate = lineLength * math.PI * range * range;
                    break;
                default:
                    estimate = 0d;
                    break;
            }

            if (estimate <= 0d)
            {
                return 0;
            }

            double capped = math.min(estimate, length);
            return capped > int.MaxValue ? int.MaxValue : (int)math.ceil(capped);
        }

    }
}
