using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace EOS.Friends
{
    /// <summary>
    /// Manages the FriendsOverlay panel slide animation, open/closed visibility state,
    /// and navigation tab visual styling.
    /// </summary>
    internal sealed class FriendsOverlayPanelView : IDisposable
    {
        private readonly MonoBehaviour _coroutineHost;
        private readonly RectTransform _rectTransform;
        private readonly CanvasGroup _canvasGroup;
        private readonly Button _openButton;
        private readonly Button _closeButton;
        private readonly Button _allFriendsTab;
        private readonly Button _platformTab;
        private readonly Button _crossPlayTab;
        private readonly Button _notificationsTab;
        private readonly Button _addFriendTab;
        private readonly Button _blockedTab;
        private readonly Color _tabActiveColor;
        private readonly Color _tabInactiveColor;
        private readonly float _slideDuration;

        private Button _activeTabButton;
        private Coroutine _slideCoroutine;

        public bool IsOpen { get; private set; }

        public event Action<FriendsTabType> OnTabClicked;
        public event Action OnOpenClicked;
        public event Action OnCloseClicked;

        public FriendsOverlayPanelView(
            MonoBehaviour coroutineHost,
            RectTransform rectTransform,
            CanvasGroup canvasGroup,
            Button openButton,
            Button closeButton,
            Button allFriendsTab,
            Button platformTab,
            Button crossPlayTab,
            Button notificationsTab,
            Button addFriendTab,
            Button blockedTab,
            Color tabActiveColor,
            Color tabInactiveColor,
            float slideDuration,
            bool startClosed)
        {
            _coroutineHost = coroutineHost;
            _rectTransform = rectTransform;
            _canvasGroup = canvasGroup;
            _openButton = openButton;
            _closeButton = closeButton;
            _allFriendsTab = allFriendsTab;
            _platformTab = platformTab;
            _crossPlayTab = crossPlayTab;
            _notificationsTab = notificationsTab;
            _addFriendTab = addFriendTab;
            _blockedTab = blockedTab;
            _tabActiveColor = tabActiveColor;
            _tabInactiveColor = tabInactiveColor;
            _slideDuration = slideDuration;

            _allFriendsTab?.onClick.AddListener(HandleAllFriendsClicked);
            _platformTab?.onClick.AddListener(HandlePlatformClicked);
            _crossPlayTab?.onClick.AddListener(HandleCrossPlayClicked);
            _notificationsTab?.onClick.AddListener(HandleNotificationsClicked);
            _addFriendTab?.onClick.AddListener(HandleAddFriendClicked);
            _blockedTab?.onClick.AddListener(HandleBlockedClicked);
            _openButton?.onClick.AddListener(HandleOpenClicked);
            _closeButton?.onClick.AddListener(HandleCloseClicked);

            ApplyInitialVisibility(startClosed);
        }

        public void UpdateTabVisuals(FriendsTabType activeTab)
        {
            Button newButton = GetTabButton(activeTab);
            if (_activeTabButton != null && _activeTabButton != newButton)
            {
                SetTabButtonVisual(_activeTabButton, false);
            }
            if (newButton != null)
            {
                SetTabButtonVisual(newButton, true);
            }
            _activeTabButton = newButton;
        }

        public void Open()
        {
            IsOpen = true;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;

            StartSlide(GetOpenPositionX(), true, null);
        }

        public void Close(Action onClosedComplete = null)
        {
            StartSlide(GetClosedPositionX(), false, onClosedComplete);
        }

        private void ApplyInitialVisibility(bool startClosed)
        {
            if (_rectTransform == null || _canvasGroup == null) return;

            if (startClosed)
            {
                IsOpen = false;
                _rectTransform.anchoredPosition = new Vector2(GetClosedPositionX(), _rectTransform.anchoredPosition.y);
                _canvasGroup.alpha = 0f;
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.interactable = false;
            }
            else
            {
                IsOpen = true;
                _rectTransform.anchoredPosition = new Vector2(GetOpenPositionX(), _rectTransform.anchoredPosition.y);
                _canvasGroup.alpha = 1f;
                _canvasGroup.blocksRaycasts = true;
                _canvasGroup.interactable = true;
            }
        }

        private void StartSlide(float targetX, bool opening, Action onClosedComplete)
        {
            if (_coroutineHost == null) return;
            if (_slideCoroutine != null) _coroutineHost.StopCoroutine(_slideCoroutine);
            _slideCoroutine = _coroutineHost.StartCoroutine(SlideRoutine(targetX, opening, onClosedComplete));
        }

        private IEnumerator SlideRoutine(float targetX, bool opening, Action onClosedComplete)
        {
            float startX = _rectTransform.anchoredPosition.x;
            float startAlpha = _canvasGroup.alpha;
            float targetAlpha = opening ? 1f : 0f;
            float elapsed = 0f;
            float duration = Mathf.Max(0.01f, _slideDuration);

            if (opening)
            {
                _canvasGroup.blocksRaycasts = true;
                _canvasGroup.interactable = true;
            }

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float smoothT = Mathf.SmoothStep(0f, 1f, t);
                float currentX = Mathf.Lerp(startX, targetX, smoothT);
                _rectTransform.anchoredPosition = new Vector2(currentX, _rectTransform.anchoredPosition.y);
                _canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, smoothT);

                yield return null;
            }

            _rectTransform.anchoredPosition = new Vector2(targetX, _rectTransform.anchoredPosition.y);
            _canvasGroup.alpha = targetAlpha;
            IsOpen = opening;
            _slideCoroutine = null;

            if (!opening)
            {
                onClosedComplete?.Invoke();
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.interactable = false;
            }
        }

        private float GetOpenPositionX()
        {
            float width = _rectTransform != null ? _rectTransform.rect.width : 600f;
            float pivotX = _rectTransform != null ? _rectTransform.pivot.x : 0.5f;
            return -(1f - pivotX) * width;
        }

        private float GetClosedPositionX()
        {
            float width = _rectTransform != null ? _rectTransform.rect.width : 600f;
            float pivotX = _rectTransform != null ? _rectTransform.pivot.x : 0.5f;
            return (pivotX * width) + 20f;
        }

        private Button GetTabButton(FriendsTabType tab)
        {
            return tab switch
            {
                FriendsTabType.AllFriends => _allFriendsTab,
                FriendsTabType.Platform => _platformTab,
                FriendsTabType.CrossPlay => _crossPlayTab,
                FriendsTabType.Notifications => _notificationsTab,
                FriendsTabType.AddFriend => _addFriendTab,
                FriendsTabType.Blocked => _blockedTab,
                _ => _allFriendsTab
            };
        }

        private void SetTabButtonVisual(Button btn, bool isActive)
        {
            Image img = btn.GetComponent<Image>();
            if (img != null)
            {
                img.color = isActive ? _tabActiveColor : _tabInactiveColor;
            }
        }

        private void HandleAllFriendsClicked() => OnTabClicked?.Invoke(FriendsTabType.AllFriends);
        private void HandlePlatformClicked() => OnTabClicked?.Invoke(FriendsTabType.Platform);
        private void HandleCrossPlayClicked() => OnTabClicked?.Invoke(FriendsTabType.CrossPlay);
        private void HandleNotificationsClicked() => OnTabClicked?.Invoke(FriendsTabType.Notifications);
        private void HandleAddFriendClicked() => OnTabClicked?.Invoke(FriendsTabType.AddFriend);
        private void HandleBlockedClicked() => OnTabClicked?.Invoke(FriendsTabType.Blocked);
        private void HandleOpenClicked() => OnOpenClicked?.Invoke();
        private void HandleCloseClicked() => OnCloseClicked?.Invoke();

        public void Dispose()
        {
            if (_slideCoroutine != null && _coroutineHost != null)
            {
                _coroutineHost.StopCoroutine(_slideCoroutine);
                _slideCoroutine = null;
            }

            _allFriendsTab?.onClick.RemoveListener(HandleAllFriendsClicked);
            _platformTab?.onClick.RemoveListener(HandlePlatformClicked);
            _crossPlayTab?.onClick.RemoveListener(HandleCrossPlayClicked);
            _notificationsTab?.onClick.RemoveListener(HandleNotificationsClicked);
            _addFriendTab?.onClick.RemoveListener(HandleAddFriendClicked);
            _blockedTab?.onClick.RemoveListener(HandleBlockedClicked);
            _openButton?.onClick.RemoveListener(HandleOpenClicked);
            _closeButton?.onClick.RemoveListener(HandleCloseClicked);
        }
    }
}

