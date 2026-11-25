using UnityEngine;
namespace LazyList
{
    [RequireComponent(typeof(RectTransform))]
    public abstract class LazyListItemView : MonoBehaviour
    {
        public RectTransform RectTransform => (RectTransform)transform;
        public int ModelIndex { get; set; }
    }
}