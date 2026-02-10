using System.Collections.Generic;
using UnityEngine;
using VoxelDestructionPro.VoxelObjects;

namespace VoxelDestructionPro.Lite
{
    /// <summary>
    /// Lite explosion: single-radius sphere destruction around this object.
    /// This is a simplified version without advanced settings.
    /// </summary>
    public class LiteExplosion : MonoBehaviour
    {
        [Tooltip("Explosion radius used for overlap and destruction.")]
        public float explosionRadius = 6f;

        [Tooltip("Delay before triggering the explosion.")]
        public float delay = 0f;

        [Tooltip("Optional physics force applied to nearby rigidbodies.")]
        public float explosionForce = 0f;

        private readonly HashSet<DynamicVoxelObj> touchedObjects = new HashSet<DynamicVoxelObj>();
        private readonly HashSet<Rigidbody> touchedBodies = new HashSet<Rigidbody>();

        private void Start()
        {
            if (delay <= 0f)
                Explode();
            else
                Invoke(nameof(Explode), delay);
        }

        public void Explode()
        {
            Vector3 position = transform.position;
            Collider[] colliders = Physics.OverlapSphere(
                position,
                explosionRadius,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore
            );

            touchedObjects.Clear();
            touchedBodies.Clear();

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null)
                    continue;

                DynamicVoxelObj vox = collider.GetComponentInParent<DynamicVoxelObj>();
                if (vox == null || touchedObjects.Contains(vox))
                    continue;

                touchedObjects.Add(vox);
                vox.AddDestruction_Sphere(position, explosionRadius);

                if (explosionForce > 0f)
                {
                    Rigidbody body = collider.attachedRigidbody;
                    if (body != null && touchedBodies.Add(body))
                        body.AddExplosionForce(explosionForce, position, explosionRadius);
                }
            }
        }
    }
}
