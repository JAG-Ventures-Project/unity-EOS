using UnityEngine;

namespace EOS.Test.FPC
{
    internal interface IPlayerVisual
    {
        void Initialize(Transform followTarget);
        void SetColor(Color newColor);
        void VisualMovement();
        void VisualRotate();
    }
}