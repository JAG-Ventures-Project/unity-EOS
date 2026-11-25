using Unity.Profiling;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;

namespace LazyList
{
    public abstract class LazyListController<TModel, TView> : MonoBehaviour
        where TView : LazyListItemView
    {
        private static readonly ProfilerMarker s_SetDataMarker = new ProfilerMarker("LazyList.SetData");
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private TView itemPrefab;
        [SerializeField] private float spacing = 5f;
        [SerializeField] private bool isLooping = false;
        protected TModel[] models = System.Array.Empty<TModel>();
        private TView[] _viewPool = System.Array.Empty<TView>();
        private float _itemStride = 0f;
        private RectTransform content;
        private int _headDataIndex = -1;
        private float _cycleSize = 0f;
        private int _poolSize = 1;
        private int _activeViewsCount = 0;

        protected ScrollRect ScrollRect => scrollRect;
        protected TView[] ViewPool => _viewPool;
        protected int ActiveViewsCount => _activeViewsCount;

#if UNITY_EDITOR
        private void OnValidate()
        {
            Assert.IsNotNull(scrollRect, "scrollRect is not assigned");
            Assert.IsNotNull(itemPrefab, "itemPrefab is not assigned");
        }
#endif
        protected virtual void Awake()
        {
            _itemStride = RectTransformUtility.CalculateRelativeRectTransformBounds(itemPrefab.RectTransform).size.y + spacing;
            itemPrefab.gameObject.SetActive(true);
            content = scrollRect.content;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            RectTransform rt = itemPrefab.RectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            scrollRect.vertical = true;
            scrollRect.horizontal = false;
            scrollRect.movementType = isLooping ? ScrollRect.MovementType.Unrestricted : ScrollRect.MovementType.Elastic;
            scrollRect.onValueChanged.AddListener(OnScrollChanged);
        }

        private void Start()
        {
            RectTransform rect = scrollRect.transform as RectTransform;
            _poolSize = Mathf.Max(1, Mathf.CeilToInt(rect.rect.height / _itemStride) + 1);
            _activeViewsCount = _poolSize;
            _viewPool = new TView[_poolSize];
            InitializePool();
        }

        protected virtual void OnDestroy()
        {
            scrollRect.onValueChanged.RemoveListener(OnScrollChanged);
            for (int i = 0; i < _viewPool.Length; i++)
            {
                if (_viewPool[i] != null)
                {
                    OnViewDestroyed(_viewPool[i]);
                }
            }
        }

        private void OnScrollChanged(Vector2 scrollPos)
        {
            Vector2 pos = content.anchoredPosition;
            if (isLooping)
            {
                HandleLoopWrapping(pos);
            }
            RefreshVisibleViews(pos.y);
        }

        public virtual void SetData(TModel[] items)
        {
            using (s_SetDataMarker.Auto())
            {
                models = items;
                if (!isLooping)
                {
                    UpdateActivePoolViews();
                }
                _headDataIndex = -_poolSize;
                scrollRect.StopMovement();
                content.anchoredPosition = new Vector2(content.anchoredPosition.x, 0f);
                CalculateLayout();
                RefreshVisibleViews(0f);
            }
        }

        private void InitializePool()
        {
            for (int i = 0; i < _viewPool.Length; i++)
            {
                var view = Instantiate(itemPrefab, content);
                view.ModelIndex = i;
                OnViewCreated(view);
                _viewPool[i] = view;
            }
        }

        protected void CalculateLayout()
        {
            _cycleSize = models.Length * _itemStride;
            float totalHeight = isLooping
                ? _cycleSize + scrollRect.viewport.rect.height
                : Mathf.Max(0f, _cycleSize - spacing);
            content.sizeDelta = new Vector2(content.sizeDelta.x, totalHeight);
        }
        protected void UpdateActivePoolViews()
        {
            int targetCount = Mathf.Min(_viewPool.Length, models.Length);
            if (targetCount == _activeViewsCount) return;
            _activeViewsCount = targetCount;
            for (int i = 0; i < _viewPool.Length; i++)
            {
                _viewPool[i].gameObject.SetActive(i < _activeViewsCount);
            }
        }

        private void RefreshVisibleViews(float scrollPos)
        {
            if (models.Length == 0)
            {
                return;
            }
            float scrollOffset = Mathf.Max(0f, scrollPos);
            int newHeadIndex = Mathf.FloorToInt(scrollOffset / _itemStride);
            if (!isLooping)
            {
                int maxHead = Mathf.Max(0, models.Length - _activeViewsCount);
                newHeadIndex = Mathf.Min(newHeadIndex, maxHead);
            }
            if (newHeadIndex == _headDataIndex)
            {
                return;
            }
            int start, end;
            if (newHeadIndex > _headDataIndex)
            {
                start = Mathf.Max(newHeadIndex, _headDataIndex + _activeViewsCount);
                end = newHeadIndex + _activeViewsCount;
            }
            else
            {
                start = newHeadIndex;
                end = Mathf.Min(_headDataIndex, newHeadIndex + _activeViewsCount);
            }
            _headDataIndex = newHeadIndex;
            for (int r = start; r < end; r++)
            {
                int modelIndex = isLooping ? (r % models.Length) : r;
                TView view = _viewPool[r % _activeViewsCount];
                view.ModelIndex = modelIndex;
                view.RectTransform.anchoredPosition = new Vector2(0f, -r * _itemStride);
                BindView(view, models[modelIndex]);
            }
        }
        private void HandleLoopWrapping(Vector2 pos)
        {
            if (pos.y >= _cycleSize)
            {
                pos.y -= _cycleSize;
                content.anchoredPosition = pos;
            }
            else if (pos.y < 0f)
            {
                pos.y += _cycleSize;
                content.anchoredPosition = pos;
            }
        }
        protected TModel GetDataForView(TView view) => models[view.ModelIndex];
        protected abstract void BindView(TView view, TModel data);
        protected virtual void OnViewCreated(TView view) { }
        protected virtual void OnViewDestroyed(TView view) { }
    }
}