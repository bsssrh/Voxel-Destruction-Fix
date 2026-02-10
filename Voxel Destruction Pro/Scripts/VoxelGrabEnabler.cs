using UnityEngine;

using VoxelDestructionPro.VoxelObjects;

namespace VoxelDestructionPro.Demo
{
    [RequireComponent(typeof(DynamicVoxelObj))]
    public class VoxelGrabEnabler : MonoBehaviour
    {
        [Min(0)]
        public int minVoxelCount = 20;

        private DynamicVoxelObj voxelObj;
        private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grabInteractable;
        private bool grabEnabled;

        private void Awake()
        {
            voxelObj = GetComponent<DynamicVoxelObj>();
            grabInteractable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            if (grabInteractable != null)
                grabInteractable.enabled = false;
        }

        private void Update()
        {
            if (grabEnabled || voxelObj == null || voxelObj.voxelData == null)
                return;

            int activeCount = voxelObj.voxelData.GetActiveVoxelCount();
            if (activeCount > minVoxelCount)
                return;

            if (grabInteractable == null)
                grabInteractable = gameObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();

            grabInteractable.enabled = true;
            grabEnabled = true;
        }
    }
}