using UnityEngine;

namespace EOS.Test.FPC
{
    [CreateAssetMenu(fileName = "PlayerControllerProperties", menuName = "EOS/Player/Controller Properties")]
    public class SOPlayerPhysicsSettings : ScriptableObject
    {
        public float moveSpeed = 5f;
        public float sprintMultiplier = 1.5f;
        public float jumpForce = 10f;
        public float groundDistance = 0.1f;
        public float groundRadius = 0.2f;
        public LayerMask groundLayer;
    }
}