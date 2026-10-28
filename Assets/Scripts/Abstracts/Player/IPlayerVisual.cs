using UnityEngine;

internal interface IPlayerVisual
{
    void Initialize(Transform followTarget);
    void SetColor(Color newColor);
    void VisualMovement();
    void VisualRotate();
}