using System.Collections.Generic;
using UnityEngine;
using VoxelDestructionPro.Data;
using VoxelDestructionPro.VoxelModifications;
using VoxelDestructionPro.VoxelObjects;

namespace VoxelDestructionPro.Demo
{
    public class Explosive : MonoBehaviour
    {
        public float explosionDelay;
        private float explosionTime;

        [Space]
        public float explosionRadius = 10f;

        // В VDP это по факту "range" разрушения (радиус воксельного воздействия).
        public float explosionForce = 20f;

        public DestructionData.DestructionType destructionType = DestructionData.DestructionType.Sphere;

        [Header("Material Filter")]
        [Tooltip("Control which voxel materials can be destroyed by this explosive.")]
        public VoxelMaterialFilter materialFilter = new()
        {
            affectAllMaterials = true,
            materialTypes = new List<VoxelMaterialType>()
        };

        [Header("Impact Type")]
        [Tooltip("Impact type used for voxel color modification.")]
        public ImpactType impactType = ImpactType.Bullet;

        [Header("Paint Settings")]
        [Min(0f)] public float paintRadius;
        [Range(0f, 1f)] public float paintNoise;
        [Min(0.01f)] public float paintFalloff = 1f;
        [Range(0f, 1f)] public float paintIntensity = 1f;

        [Header("Paint (Compound tweak)")]
        [Tooltip("Extra multiplier applied ONLY when CompoundBoxCollider mode is active, to make paint look as 'wide' as Mesh mode.")]
        [Min(1f)] private float compoundPaintRadiusMultiplier = 1.1f;

        // caches to avoid allocations (1 destruction per DynamicVoxelObj)
        private readonly Dictionary<DynamicVoxelObj, Collider> bestColliderByObj = new Dictionary<DynamicVoxelObj, Collider>(64);
        private readonly Dictionary<DynamicVoxelObj, float> bestDistSqByObj = new Dictionary<DynamicVoxelObj, float>(64);

        private void Start()
        {
            explosionTime = Time.time + explosionDelay;
        }

        private void Update()
        {
            if (Time.time <= explosionTime)
                return;

            Vector3 explosionPos = transform.position;

            Collider[] colliders = Physics.OverlapSphere(
                explosionPos,
                explosionRadius,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore
            );

            IEnumerable<VoxelMaterialType> materials = materialFilter.GetFilter();

            // 1) pick ONE "best" collider per DynamicVoxelObj (closest point to explosion)
            bestColliderByObj.Clear();
            bestDistSqByObj.Clear();

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider c = colliders[i];
                if (c == null) continue;

                DynamicVoxelObj vox = c.GetComponentInParent<DynamicVoxelObj>();
                if (vox == null) continue;

                Vector3 cp = c.ClosestPoint(explosionPos);
                float d2 = (cp - explosionPos).sqrMagnitude;

                if (bestDistSqByObj.TryGetValue(vox, out float best))
                {
                    if (d2 < best)
                    {
                        bestDistSqByObj[vox] = d2;
                        bestColliderByObj[vox] = c;
                    }
                }
                else
                {
                    bestDistSqByObj[vox] = d2;
                    bestColliderByObj[vox] = c;
                }
            }

            // 2) destroy once per object + paint in correct mode
            foreach (var kv in bestColliderByObj)
            {
                DynamicVoxelObj vox = kv.Key;
                Collider hitCollider = kv.Value;

                if (vox == null || hitCollider == null)
                    continue;

                bool destructionStarted = destructionType == DestructionData.DestructionType.Sphere
                    ? vox.AddDestruction_Sphere(explosionPos, explosionForce, materials)
                    : vox.AddDestruction_Cube(explosionPos, explosionForce, materials);

                if (!destructionStarted)
                    continue;

                bool compound = vox.IsCompoundColliderModeActive();

                if (compound)
                {
                    // ✅ Compound: красим 1:1 в точку разрушения (центр взрыва)
                    Vector3 paintPoint = explosionPos;

                    // ✅ Boost radius ONLY for compound (to compensate "too tight" look)
                    float boostedRadius = paintRadius * Mathf.Max(1f, compoundPaintRadiusMultiplier);

                    if (vox.TryGetComponent(out VoxelColorModifierCompound compoundModifier))
                    {
                        compoundModifier.ApplyImpactColor(
                            hitCollider,
                            paintPoint,
                            impactType,
                            boostedRadius,
                            paintNoise,
                            paintFalloff,
                            paintIntensity
                        );
                    }
                    else if (vox.TryGetComponent(out VoxelColorModifier fallbackMeshMod))
                    {
                        // fallback (если у тебя где-то всё ещё стоит обычный модификатор)
                        fallbackMeshMod.ApplyImpactColor(
                            paintPoint,
                            impactType,
                            boostedRadius,
                            paintNoise,
                            paintFalloff,
                            paintIntensity
                        );
                    }
                }
                else
                {
                    // ✅ Mesh: красим по поверхности (ClosestPoint)
                    Collider targetCol = vox.targetCollider != null ? vox.targetCollider : hitCollider;
                    Vector3 paintPoint = targetCol != null ? targetCol.ClosestPoint(explosionPos) : explosionPos;

                    if (vox.TryGetComponent(out VoxelColorModifier meshModifier))
                    {
                        // В mesh-скрипте обычно есть перегрузка (Collider, Point,...)
                        meshModifier.ApplyImpactColor(
                            targetCol,
                            paintPoint,
                            impactType,
                            paintRadius,
                            paintNoise,
                            paintFalloff,
                            paintIntensity
                        );
                    }
                    else if (vox.TryGetComponent(out VoxelColorModifierCompound fallbackCompound))
                    {
                        fallbackCompound.ApplyImpactColor(
                            targetCol,
                            paintPoint,
                            impactType,
                            paintRadius,
                            paintNoise,
                            paintFalloff,
                            paintIntensity
                        );
                    }
                }
            }

            Debug.Log("[Explosive] triggered at " + explosionPos);
            Destroy(gameObject);
        }
    }
}
