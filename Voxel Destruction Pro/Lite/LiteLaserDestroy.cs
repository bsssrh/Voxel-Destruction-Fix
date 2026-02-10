using UnityEngine;
using VoxelDestructionPro.VoxelObjects;

namespace VoxelDestructionPro.Lite
{
    /// <summary>
    /// Lite laser: shoots a ray from a camera/transform and destroys voxels on hit.
    /// This is a simplified version without advanced settings.
    /// </summary>
    public class LiteLaserDestroy : MonoBehaviour
    {
        [Tooltip("Ray origin; if null, Camera.main is used.")]
        public Transform rayOrigin;

        [Tooltip("Optional destruction radius override. Leave <= 0 to use the default radius.")]
        public float radius = 0f;

        [Tooltip("Input key or mouse button (Mouse0/Mouse1/Mouse2).")]
        public KeyCode activationKey = KeyCode.Mouse0;

        private const float DefaultRadius = 2f;
        private const float MaxDistance = 200f;

        private void Update()
        {
            if (!Input.GetKey(activationKey))
                return;

            Transform origin = rayOrigin != null
                ? rayOrigin
                : (Camera.main != null ? Camera.main.transform : null);

            if (origin == null)
                return;

            Ray ray = new Ray(origin.position, origin.forward);

            if (!Physics.Raycast(ray, out RaycastHit hit, MaxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return;

            DynamicVoxelObj voxelObj = hit.collider.GetComponentInParent<DynamicVoxelObj>();
            if (voxelObj == null)
                return;

            float destructionRadius = radius > 0f ? radius : DefaultRadius;
            voxelObj.AddDestruction_Sphere(hit.point, destructionRadius);
        }
    }
}
