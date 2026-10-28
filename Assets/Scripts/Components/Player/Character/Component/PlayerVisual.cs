using UnityEngine;
using UnityEngine.Assertions;

namespace Minimap.FPC
{
    [RequireComponent(typeof(Renderer))]
    public class PlayerVisual : MonoBehaviour, IPlayerVisual
    {
        [SerializeField] private Renderer _meshRenderer;

#if UNITY_EDITOR
        private void OnValidate()
        {
            TryGetComponent(out _meshRenderer);
            Assert.IsNotNull(_meshRenderer, "Renderer is null!");
        }
#endif

        private Transform _target;
        public void Initialize(Transform followTarget)
        {
            _target = followTarget;
            transform.SetPositionAndRotation(_target.position, _target.rotation);
        }
        void LateUpdate()
        {
            VisualMovement();
            VisualRotate();
        }

        public void SetColor(Color newColor)
        {
            _meshRenderer.material.color = newColor;   
        }

        public void VisualMovement() => transform.position = _target.position;
        public void VisualRotate() => transform.rotation = _target.rotation;
    }
}