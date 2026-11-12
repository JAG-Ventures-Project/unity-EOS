using UnityEngine;

namespace EOS.Test.FPC
{
    internal interface IPlayerPhysics
    {
        void SetMoveInput(Vector2 moveCalled);
        void HandleMovement();
        void SetSprinting(bool isSprinting);
        void HandleRotation(Quaternion yaw);
        void HandleJump();
        bool IsGrounded();
        void EnablePhysics(bool isEnable = true);
    }
}