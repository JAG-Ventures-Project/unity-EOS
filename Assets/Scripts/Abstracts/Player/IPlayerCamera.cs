using UnityEngine;
internal interface IPlayerCamera
{
    void InputsListener(Vector2 lookInput);
    void SetCameraTarget(Transform target);
    void FollowTarget();
    void HandleCameraRotation();
}