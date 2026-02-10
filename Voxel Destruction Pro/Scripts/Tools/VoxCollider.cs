using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VoxelDestructionPro.Data;
using VoxelDestructionPro.VoxelModifications;
using VoxelDestructionPro.VoxelObjects;

namespace VoxelDestructionPro.Tools
{
    public class VoxCollider : MonoBehaviour
    {
        [Tooltip("The minimum collision relative velocity, increase this if a stationary object still triggers destruction")]
        public float minCollisionRelative = 1;
        
        [Space]
        
        [Tooltip("The radius of the destruction, for sphere destructiontype this is the radius of the sphere")]
        public float destructionRadius = 10;
        [Tooltip("If enabled the destruction radius will be effected by the relative velocity of the collision")]
        public bool useRelativeVelocity;
        [Tooltip("If enabled the destruction radius will be effect by the targets object voxel size")]
        public bool useObjScale;

        [Space] 
        
        [Tooltip("The destruction type that should be used")]
        public DestructionData.DestructionType destructionType = DestructionData.DestructionType.Sphere;

        [Tooltip("The impact type used for voxel color modification.")]
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
        
        private void OnCollisionEnter(Collision other)
        {
            DynamicVoxelObj vox = other.transform.GetComponentInParent<DynamicVoxelObj>();
            
            if (vox == null)
                return;
            
            float mag = other.relativeVelocity.magnitude;

            if (mag < minCollisionRelative)
                return;
            
            float rad = destructionRadius;

            if (useRelativeVelocity)
                rad *= mag * 0.1f;

            if (useObjScale)
                rad /= vox.GetSingleVoxelSize();

            ContactPoint contact = other.GetContact(0);
            Collider voxelCollider = ResolveVoxelCollider(vox, contact);

            DestructionData destructionData = new DestructionData(
                destructionType,
                other.contacts[0].point,
                other.contacts[0].point - other.contacts[0].normal * rad,
                rad
            );

            bool destructionStarted = vox.AddDestruction(destructionData);

            if (!destructionStarted)
                return;

            bool compound = vox.IsCompoundColliderModeActive();

            if (compound)
            {
                float boostedRadius = paintRadius * Mathf.Max(1f, compoundPaintRadiusMultiplier);
                Vector3 paintPoint = contact.point;

                if (vox.TryGetComponent(out VoxelColorModifierCompound compoundModifier))
                {
                    compoundModifier.ApplyImpactColor(voxelCollider, paintPoint, impactType, boostedRadius, paintNoise, paintFalloff, paintIntensity);
                }
                else if (vox.TryGetComponent(out VoxelColorModifier fallbackMeshModifier))
                {
                    fallbackMeshModifier.ApplyImpactColor(paintPoint, impactType, boostedRadius, paintNoise, paintFalloff, paintIntensity);
                }
            }
            else
            {
                Collider targetCol = vox.targetCollider != null ? vox.targetCollider : voxelCollider;
                Vector3 paintPoint = targetCol != null ? targetCol.ClosestPoint(contact.point) : contact.point;

                if (vox.TryGetComponent(out VoxelColorModifier meshModifier))
                {
                    meshModifier.ApplyImpactColor(targetCol, paintPoint, impactType, paintRadius, paintNoise, paintFalloff, paintIntensity);
                }
                else if (vox.TryGetComponent(out VoxelColorModifierCompound fallbackCompound))
                {
                    fallbackCompound.ApplyImpactColor(targetCol, paintPoint, impactType, paintRadius, paintNoise, paintFalloff, paintIntensity);
                }
            }
        }

        private static Collider ResolveVoxelCollider(DynamicVoxelObj voxelObj, ContactPoint contact)
        {
            if (voxelObj == null)
                return null;

            if (contact.thisCollider != null && contact.thisCollider.GetComponentInParent<DynamicVoxelObj>() == voxelObj)
                return contact.thisCollider;

            if (contact.otherCollider != null && contact.otherCollider.GetComponentInParent<DynamicVoxelObj>() == voxelObj)
                return contact.otherCollider;

            if (voxelObj.targetCollider != null)
                return voxelObj.targetCollider;

            return contact.otherCollider != null ? contact.otherCollider : contact.thisCollider;
        }
    }
}

