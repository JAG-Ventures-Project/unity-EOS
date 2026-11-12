using UnityEngine;

namespace EOS.Test.FPC
{
    [CreateAssetMenu(fileName = "FPCameraSettings", menuName = "Player/Camera Settings")]
    public class FPCameraSettings : ScriptableObject
    {
        public float rotationSpeed = 2f;
        public float heightOffset = 2f;
        public float minPitch = -85f;
        public float maxPitch = 85f;
    }
}