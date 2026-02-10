using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using VoxelDestructionPro;
using VoxelDestructionPro.Data;
using VoxelDestructionPro.Settings;
using VoxelDestructionPro.Tools;
using VoxelDestructionPro.VoxelObjects;

namespace VoxelDestructionPro.VoxelModifications
{
    public class VoxelColorModifierCompound : VoxModification
    {
        public VoxelColorProfile colorProfile;

        private VoxelData lastVoxelData;
        private bool voxelDataCloned;

        private const int MaxImpactWaitFrames = 10;

        private bool impactPending;
        private int impactFrame;

        private ImpactType pendingImpactType;
        private Vector3 pendingPoint;
        private Collider pendingCollider;

        private float pendingRadius;
        private float pendingNoise;
        private float pendingFalloff;
        private float pendingIntensity;

        private int lastRemovedFrame = -1;
        private int lastRemovedCount;

        private void OnEnable()
        {
            if (dyn_targetObj != null)
                dyn_targetObj.onBeforeVoxelsRemoved += HandleBeforeVoxelsRemoved;
            if (dyn_targetObj != null)
                dyn_targetObj.onVoxelsRemoved += HandleVoxelsRemoved;
        }

        private void OnDisable()
        {
            if (dyn_targetObj != null)
                dyn_targetObj.onBeforeVoxelsRemoved -= HandleBeforeVoxelsRemoved;
            if (dyn_targetObj != null)
                dyn_targetObj.onVoxelsRemoved -= HandleVoxelsRemoved;
        }

        private void HandleBeforeVoxelsRemoved(NativeList<int> removedVoxels)
        {
            lastRemovedCount = removedVoxels.Length;
            lastRemovedFrame = Time.frameCount;

            if (!impactPending || lastRemovedCount <= 0)
                return;

            ApplyPendingImpact();
        }

        private void HandleVoxelsRemoved(NativeList<int> removedVoxels)
        {
            lastRemovedCount = removedVoxels.Length;
            lastRemovedFrame = Time.frameCount;

            if (!impactPending || lastRemovedCount <= 0)
                return;

            if (Time.frameCount - impactFrame > MaxImpactWaitFrames)
            {
                impactPending = false;
                return;
            }

            ApplyPendingImpact();
        }

        public void ApplyImpactColor(RaycastHit hit, ImpactType impactType, float paintRadius, float paintNoise, float paintFalloff, float paintIntensity)
        {
            ApplyImpactColor(hit.collider, hit.point, impactType, paintRadius, paintNoise, paintFalloff, paintIntensity);
        }

        public void ApplyImpactColor(Collider hitCollider, Vector3 hitPoint, ImpactType impactType, float paintRadius, float paintNoise, float paintFalloff, float paintIntensity)
        {
            QueueImpact(hitCollider, hitPoint, impactType, paintRadius, paintNoise, paintFalloff, paintIntensity);
        }

        private void QueueImpact(Collider hitCollider, Vector3 hitPoint, ImpactType impactType, float paintRadius, float paintNoise, float paintFalloff, float paintIntensity)
        {
            impactPending = true;
            impactFrame = Time.frameCount;

            pendingCollider = hitCollider;
            pendingPoint = hitPoint;

            pendingImpactType = impactType;
            pendingRadius = paintRadius;
            pendingNoise = paintNoise;
            pendingFalloff = paintFalloff;
            pendingIntensity = paintIntensity;

            if (lastRemovedCount > 0 && lastRemovedFrame == Time.frameCount)
                ApplyPendingImpact();
        }

        private void ApplyPendingImpact()
        {
            if (!impactPending)
                return;

            impactPending = false;

            ApplyImpactColorImmediate(
                pendingCollider,
                pendingPoint,
                pendingImpactType,
                impactFrame,
                pendingRadius,
                pendingNoise,
                pendingFalloff,
                pendingIntensity
            );
        }

        private void ApplyImpactColorImmediate(
            Collider hitCollider,
            Vector3 hitPoint,
            ImpactType impactType,
            int noiseSeed,
            float paintRadius,
            float paintNoise,
            float paintFalloff,
            float paintIntensity)
        {
            if (paintRadius <= 0f)
                return;

            DynamicVoxelObj voxelObj = dyn_targetObj;
            if (voxelObj == null || colorProfile == null || voxelObj.voxelData == null)
                return;

            // ✅ Только Compound-режим
            if (!voxelObj.IsCompoundColliderModeActive())
                return;

            if (hitCollider == null)
                return;

            // ✅ Валидация: если targetColliders уже собраны — collider ОБЯЗАН быть из этого массива
            // Если массив пока пуст/не собран — fallback по родителю (чтобы не ломать на старте)
            if (voxelObj.targetColliders != null && voxelObj.targetColliders.Length > 0)
            {
                if (!IsInTargetColliders(voxelObj, hitCollider))
                    return;
            }
            else
            {
                if (hitCollider.GetComponentInParent<DynamicVoxelObj>() != voxelObj)
                    return;
            }

            // В Compound режиме тег берём с коллайдера
            string meshTag = hitCollider.gameObject.tag;
            if (!colorProfile.TryGetTagEntry(impactType, meshTag, out TagEntry tagEntry))
                return;

            ApplyColorInternal(
                voxelObj,
                hitPoint, // hitPoint НЕ двигаем
                tagEntry,
                noiseSeed,
                paintRadius,
                paintNoise,
                paintFalloff,
                paintIntensity
            );
        }

        private static bool IsInTargetColliders(DynamicVoxelObj obj, Collider c)
        {
            var arr = obj != null ? obj.targetColliders : null;
            if (arr == null || c == null) return false;

            for (int i = 0; i < arr.Length; i++)
                if (arr[i] == c) return true;

            return false;
        }

        private void ApplyColorInternal(
            DynamicVoxelObj voxelObj,
            Vector3 impactPoint,
            TagEntry tagEntry,
            int noiseSeed,
            float paintRadius,
            float paintNoise,
            float paintFalloff,
            float paintIntensity)
        {
            EnsureUniqueVoxelData(voxelObj);
            VoxelData voxelData = voxelObj.voxelData;

            Transform meshTransform = voxelObj.targetFilter != null ? voxelObj.targetFilter.transform : voxelObj.transform;
            float voxelSize = voxelObj.GetSingleVoxelSize();
            if (voxelSize <= 0f)
                return;

            // voxel-space (дробные координаты)
            Vector3 localVox = meshTransform.InverseTransformPoint(impactPoint) / voxelSize;

            int3 length = voxelData.length;

            // Детерминированный выбор центра: при .5 всегда "вниз"
            int centerX = ClampIndex(RoundHalfDown(localVox.x), length.x);
            int centerY = ClampIndex(RoundHalfDown(localVox.y), length.y);
            int centerZ = ClampIndex(RoundHalfDown(localVox.z), length.z);

            float radius = Mathf.Max(0f, paintRadius);
            if (radius <= 0f)
                return;

            int radiusCeil = Mathf.CeilToInt(radius);
            int minX = Mathf.Max(0, centerX - radiusCeil);
            int minY = Mathf.Max(0, centerY - radiusCeil);
            int minZ = Mathf.Max(0, centerZ - radiusCeil);

            int maxX = Mathf.Min(length.x - 1, centerX + radiusCeil);
            int maxY = Mathf.Min(length.y - 1, centerY + radiusCeil);
            int maxZ = Mathf.Min(length.z - 1, centerZ + radiusCeil);

            Dictionary<Color32, byte> paletteLookup = BuildPaletteLookup(voxelData);
            List<Color> paletteColors = new List<Color>(voxelData.palette.ToArray());

            bool paletteChanged = false;
            bool anyVoxelChanged = false;

            float radiusSquared = radius * radius;
            float edgeNoise = Mathf.Clamp01(paintNoise);
            float intensity = Mathf.Clamp01(paintIntensity);
            float falloff = Mathf.Max(0.01f, paintFalloff);

            // Важно: расстояние считаем от дробной точки localVox (стабильнее)
            for (int z = minZ; z <= maxZ; z++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        float dx = x - localVox.x;
                        float dy = y - localVox.y;
                        float dz = z - localVox.z;

                        float distanceSquared = dx * dx + dy * dy + dz * dz;
                        if (distanceSquared > radiusSquared)
                            continue;

                        int voxelIndex = To1D(x, y, z, length);
                        Voxel voxel = voxelData.voxels[voxelIndex];

                        if (voxel.active == 0)
                            continue;

                        if (edgeNoise > 0f)
                        {
                            float distance = Mathf.Sqrt(distanceSquared);
                            float t = Mathf.Clamp01(distance / radius);
                            float skipChance = edgeNoise * t;
                            if (Hash01(voxelIndex, noiseSeed) < skipChance)
                                continue;
                        }

                        Color originalColor = voxelData.palette[voxel.color];
                        Color targetColor = tagEntry.targetColor;

                        if (tagEntry.blendMode == VoxelColorBlendMode.BlendToOriginal && radius > 0f)
                        {
                            float distance = Mathf.Sqrt(distanceSquared);
                            float t = Mathf.Clamp01(distance / radius);
                            t = Mathf.Pow(t, falloff);
                            targetColor = Color.Lerp(tagEntry.targetColor, originalColor, t);
                        }

                        if (intensity < 1f)
                            targetColor = Color.Lerp(originalColor, targetColor, intensity);

                        byte newColorIndex = GetOrAddPaletteIndex(paletteLookup, paletteColors, targetColor, ref paletteChanged);
                        if (voxel.color != newColorIndex)
                        {
                            voxel.color = newColorIndex;
                            voxelData.voxels[voxelIndex] = voxel;
                            anyVoxelChanged = true;
                        }
                    }
                }
            }

            // ✅ если не затронули активные — не регеним
            if (!anyVoxelChanged)
                return;

            if (paletteChanged)
            {
                voxelData.palette.Dispose();
                voxelData.palette = new NativeArray<Color>(paletteColors.ToArray(), Allocator.Persistent);
            }

            voxelObj.RequestMeshRegeneration();
        }

        // --- Helpers ---

        private static int ClampIndex(int idx, int axisLen) => Mathf.Clamp(idx, 0, axisLen - 1);

        // Детерминированный Round: при дробной части ровно .5 всегда вниз
        private static int RoundHalfDown(float v)
        {
            const float eps = 1e-6f;
            float f = Mathf.Floor(v);
            float frac = v - f;

            if (frac > 0.5f + eps) return (int)f + 1;
            return (int)f;
        }

        private static float Hash01(int value, int seed)
        {
            unchecked
            {
                uint hash = (uint)value;
                hash ^= (uint)seed + 0x9e3779b9u + (hash << 6) + (hash >> 2);
                hash ^= hash >> 16;
                hash *= 0x7feb352du;
                hash ^= hash >> 15;
                hash *= 0x846ca68bu;
                hash ^= hash >> 16;
                return (hash & 0x00ffffffu) / 16777215f;
            }
        }

        private static Dictionary<Color32, byte> BuildPaletteLookup(VoxelData voxelData)
        {
            var lookup = new Dictionary<Color32, byte>();
            for (byte i = 0; i < voxelData.palette.Length; i++)
            {
                Color32 c = voxelData.palette[i];
                if (!lookup.ContainsKey(c))
                    lookup.Add(c, i);
            }
            return lookup;
        }

        private static int To1D(int x, int y, int z, int3 length)
        {
            return x + length.x * (y + length.y * z);
        }

        private void EnsureUniqueVoxelData(DynamicVoxelObj voxelObj)
        {
            if (voxelObj == null || voxelObj.voxelData == null)
                return;

            if (voxelObj.voxelData == lastVoxelData && voxelDataCloned)
                return;

            lastVoxelData = voxelObj.voxelData;
            voxelDataCloned = false;

            VoxelData voxelData = voxelObj.voxelData;
            VoxelObjBase[] voxelObjects = Object.FindObjectsOfType<VoxelObjBase>();
            for (int i = 0; i < voxelObjects.Length; i++)
            {
                VoxelObjBase otherObj = voxelObjects[i];
                if (otherObj == null || otherObj == voxelObj)
                    continue;

                if (otherObj.voxelData == voxelData)
                {
                    CachedVoxelData cachedCopy = voxelData.ToCachedVoxelData().GetCopy();
                    voxelObj.voxelData = new VoxelData(cachedCopy);
                    lastVoxelData = voxelObj.voxelData;
                    voxelDataCloned = true;
                    return;
                }
            }
        }

        private static byte GetOrAddPaletteIndex(
            Dictionary<Color32, byte> paletteLookup,
            List<Color> paletteColors,
            Color targetColor,
            ref bool paletteChanged)
        {
            Color32 color32 = targetColor;

            if (paletteLookup.TryGetValue(color32, out byte index))
                return index;

            if (paletteColors.Count >= byte.MaxValue)
                return FindClosestPaletteIndex(paletteColors, targetColor);

            byte newIndex = (byte)paletteColors.Count;
            paletteColors.Add(targetColor);
            paletteLookup[color32] = newIndex;
            paletteChanged = true;
            return newIndex;
        }

        private static byte FindClosestPaletteIndex(List<Color> paletteColors, Color targetColor)
        {
            byte closestIndex = 0;
            float closestDistance = float.MaxValue;

            for (byte i = 0; i < paletteColors.Count; i++)
            {
                Color c = paletteColors[i];
                float d = (c.r - targetColor.r) * (c.r - targetColor.r)
                        + (c.g - targetColor.g) * (c.g - targetColor.g)
                        + (c.b - targetColor.b) * (c.b - targetColor.b);

                if (d < closestDistance)
                {
                    closestDistance = d;
                    closestIndex = i;
                }
            }
            return closestIndex;
        }
    }
}
