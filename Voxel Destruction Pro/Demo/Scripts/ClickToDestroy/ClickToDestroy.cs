using UnityEngine;
using UnityEngine.SceneManagement;
using VoxelDestructionPro.Data;
using VoxelDestructionPro.VoxelModifications;
using VoxelDestructionPro.VoxelObjects;

namespace VoxelDestructionPro.Demo
{
    public class ClickToDestroy : MonoBehaviour
    {
        public Camera cam;

        public float destructionRadius = 2;

        public ImpactType impactType = ImpactType.Bullet;

        [Header("Paint Settings")]
        [Min(0f)]
        public float paintRadius;

        [Range(0f, 1f)]
        public float paintNoise;

        [Min(0.01f)]
        public float paintFalloff = 1f;

        [Range(0f, 1f)]
        public float paintIntensity = 1f;

        [Header("Paint (Compound tweak)")]
        [Tooltip("Extra multiplier applied ONLY when CompoundBoxCollider mode is active, to make paint look as 'wide' as Mesh mode.")]
        [Min(1f)] public float compoundPaintRadiusMultiplier = 1.1f;

        private void Update()
        {
            bool oneClick = Input.GetKey(KeyCode.LeftShift);
        
            if ((!oneClick && Input.GetMouseButton(0)) || (oneClick && Input.GetMouseButtonDown(0)))
            {
                Ray r = cam.ScreenPointToRay(Input.mousePosition);
            
                if (!Physics.Raycast(r, out RaycastHit hit, 999))
                    return;
            
                DynamicVoxelObj vo = hit.transform.GetComponentInParent<DynamicVoxelObj>();
            
                if (vo == null)
                    return;
                
                bool destructionStarted = vo.AddDestruction_Sphere(hit.point, destructionRadius);

                if (!destructionStarted)
                    return;

                bool compound = vo.IsCompoundColliderModeActive();

                if (compound)
                {
                    float boostedRadius = paintRadius * Mathf.Max(1f, compoundPaintRadiusMultiplier);
                    Vector3 paintPoint = hit.point;

                    if (vo.TryGetComponent(out VoxelColorModifierCompound compoundModifier))
                    {
                        compoundModifier.ApplyImpactColor(hit.collider, paintPoint, impactType, boostedRadius, paintNoise, paintFalloff, paintIntensity);
                    }
                    else if (vo.TryGetComponent(out VoxelColorModifier fallbackMeshModifier))
                    {
                        fallbackMeshModifier.ApplyImpactColor(paintPoint, impactType, boostedRadius, paintNoise, paintFalloff, paintIntensity);
                    }
                }
                else
                {
                    Collider targetCol = vo.targetCollider != null ? vo.targetCollider : hit.collider;
                    Vector3 paintPoint = targetCol != null ? targetCol.ClosestPoint(hit.point) : hit.point;

                    if (vo.TryGetComponent(out VoxelColorModifier meshModifier))
                    {
                        meshModifier.ApplyImpactColor(targetCol, paintPoint, impactType, paintRadius, paintNoise, paintFalloff, paintIntensity);
                    }
                    else if (vo.TryGetComponent(out VoxelColorModifierCompound fallbackCompound))
                    {
                        fallbackCompound.ApplyImpactColor(targetCol, paintPoint, impactType, paintRadius, paintNoise, paintFalloff, paintIntensity);
                    }
                }
            }
        }

        public void SwitchToMovement()
        {
            SceneManager.LoadScene(0);
        }
    }
}
