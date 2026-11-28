using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EOS.Friends
{
    // Manages overlay slide animation, tab visuals, notification badge, and VerticalLayoutGroup toggling.
    internal sealed class FriendsOverlayPanelView : IDisposable
    {
        private readonly MonoBehaviour _coroutineHost;
        private readonly RectTransform _overlayMenu;
        private readonly CanvasGroup _canvasGroup;
        private readonly Button _openButton;
        private readonly Button _closeButton;
        private readonly Button _backGroundButton;

        private readonly Toggle _friendsTab;
        private readonly Toggle _notificationsTab;
        private readonly Toggle _addFriendTab;
        private readonly Toggle _blockedTab;

        private readonly GameObject _friendsSubMenuContainer;
        private readonly Toggle _platformSubTab;
        private readonly Toggle _crossPlaySubTab;

        private readonly GameObject _searchContainer;

        private readonly GameObject _notificationBadge;
        private readonly TMP_Text _notificationBadgeText;

        private readonly VerticalLayoutGroup _bottomSectionLayoutGroup;
        private readonly RectTransform _bottomSectionRect;
        private readonly float _slideDuration;
        private readonly bool _slideLeft;
        private readonly float _openAnchoredX;
        private float _closedAnchoredX;

        private struct ActiveToggleState
        {
            public Toggle Toggle;
            public Sprite NormalSprite;
        }

        private ActiveToggleState _activeMainTab;
        private ActiveToggleState _activeSubTab;
        private Coroutine _slideCoroutine;

        public bool IsOpen { get; private set; }

        public event Action<FriendsTabType> OnTabClicked;
        public event Action<FriendsFilterType> OnFilterClicked;
        public event Action OnOpenClicked;
        public event Action OnCloseClicked;

        public FriendsOverlayPanelView(
            MonoBehaviour coroutineHost,
            RectTransform overlayMenu,
            CanvasGroup canvasGroup,
            Button openButton,
            Button closeButton,
            Button backGround,
            Toggle friendsTab,
            Toggle notificationsTab,
            Toggle addFriendTab,
            Toggle blockedTab,
            GameObject friendsSubMenuContainer,
            Toggle platformSubTab,
            Toggle crossPlaySubTab,
            GameObject searchContainer,
            GameObject notificationBadge,
            TMP_Text notificationBadgeText,
            VerticalLayoutGroup bottomSectionLayoutGroup,
            float slideDuration,
            bool slideLeft,
            bool startClosed)
        {
            _coroutineHost = coroutineHost;
            _overlayMenu = overlayMenu;
            _canvasGroup = canvasGroup;
            _openButton = openButton;
            _closeButton = closeButton;
            _backGroundButton = backGround;
            _friendsTab = friendsTab;
            _notificationsTab = notificationsTab;
            _addFriendTab = addFriendTab;
            _blockedTab = blockedTab;
            _friendsSubMenuContainer = friendsSubMenuContainer;
            _platformSubTab = platformSubTab;
            _crossPlaySubTab = crossPlaySubTab;
            _searchContainer = searchContainer;
            _notificationBadge = notificationBadge;
            _notificationBadgeText = notificationBadgeText;
            _bottomSectionLayoutGroup = bottomSectionLayoutGroup;
            _bottomSectionRect = (RectTransform)bottomSectionLayoutGroup.transform;
            _slideDuration = slideDuration;
            _slideLeft = slideLeft;

            _friendsTab.onValueChanged.AddListener(HandleFriendsToggled);
            _notificationsTab.onValueChanged.AddListener(HandleNotificationsToggled);
            _addFriendTab.onValueChanged.AddListener(HandleAddFriendToggled);
            _blockedTab.onValueChanged.AddListener(HandleBlockedToggled);
            _platformSubTab.onValueChanged.AddListener(HandlePlatformSubTabToggled);
            _crossPlaySubTab.onValueChanged.AddListener(HandleCrossPlaySubTabToggled);
            _openButton.onClick.AddListener(HandleOpenClicked);
            _closeButton.onClick.AddListener(HandleCloseClicked);
            _backGroundButton.onClick.AddListener(HandleCloseClicked);

            Canvas.ForceUpdateCanvases();
            _openAnchoredX = _overlayMenu.anchoredPosition.x;
            _closedAnchoredX = ComputeClosedPositionX();

            ApplyInitialVisibility(startClosed);
        }

        public void UpdateTabVisuals(FriendsTabType activeTab, FriendsFilterType activeFilter)
        {
            SwitchActiveTabToggle(ref _activeMainTab, GetMainTabToggle(activeTab));

            Toggle newSubToggle = activeTab == FriendsTabType.Friends ? GetSubTabToggle(activeFilter) : null;
            SwitchActiveTabToggle(ref _activeSubTab, newSubToggle);
        }

        private static void SwitchActiveTabToggle(ref ActiveToggleState active, Toggle next)
        {
            if (active.Toggle == next) return;

            if (active.Toggle != null)
            {
                SetToggleSprite(active.Toggle, active.NormalSprite);
            }

            if (next != null)
            {
                Image img = next.image;
                active.NormalSprite = img != null ? img.sprite : null;
                SetToggleSprite(next, next.spriteState.selectedSprite);
                next.SetIsOnWithoutNotify(true);
            }

            active.Toggle = next;
        }

        private static void SetToggleSprite(Toggle toggle, Sprite sprite)
        {
            Image img = toggle.image;
            if (img == null) return;

            img.overrideSprite = null;
            img.sprite = sprite;
        }

        public void UpdateTabContentLayout(FriendsTabType activeTab)
        {
            _friendsSubMenuContainer.SetActive(activeTab == FriendsTabType.Friends);
            _searchContainer.SetActive(activeTab == FriendsTabType.AddFriend);

            _bottomSectionLayoutGroup.enabled = true;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_bottomSectionRect);
            _bottomSectionLayoutGroup.enabled = false;
        }

        public void UpdateNotificationBadge(int count)
        {
            _notificationBadge.SetActive(count > 0);
            if (count > 99)
            {
                _notificationBadgeText.SetText("99+");
            }
            else if (count > 0)
            {
                _notificationBadgeText.SetText("{0}", count);
            }
        }

        public void Open()
        {
            IsOpen = true;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;

            StartSlide(true, null);
        }

        public void Close(Action onClosedComplete = null)
        {
            StartSlide(false, onClosedComplete);
        }

        public void RefreshOverlayAnchors()
        {
            Canvas.ForceUpdateCanvases();
            _closedAnchoredX = ComputeClosedPositionX();

            if (_slideCoroutine == null)
            {
                float targetX = IsOpen ? _openAnchoredX : _closedAnchoredX;
                _overlayMenu.anchoredPosition = new Vector2(targetX, _overlayMenu.anchoredPosition.y);
            }
        }

        private void ApplyInitialVisibility(bool startClosed)
        {
            if (startClosed)
            {
                IsOpen = false;
                _overlayMenu.anchoredPosition = new Vector2(_closedAnchoredX, _overlayMenu.anchoredPosition.y);
                _canvasGroup.alpha = 0f;
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.interactable = false;
            }
            else
            {
                IsOpen = true;
                _overlayMenu.anchoredPosition = new Vector2(_openAnchoredX, _overlayMenu.anchoredPosition.y);
                _canvasGroup.alpha = 1f;
                _canvasGroup.blocksRaycasts = true;
                _canvasGroup.interactable = true;
            }
        }

        private void StartSlide(bool opening, Action onClosedComplete)
        {
            if (_slideCoroutine != null) _coroutineHost.StopCoroutine(_slideCoroutine);
            _slideCoroutine = _coroutineHost.StartCoroutine(SlideRoutine(opening, onClosedComplete));
        }

        private IEnumerator SlideRoutine(bool opening, Action onClosedComplete)
        {
            float startX = _overlayMenu.anchoredPosition.x;
            float startAlpha = _canvasGroup.alpha;
            float targetAlpha = opening ? 1f : 0f;
            float elapsed = 0f;
            float duration = Mathf.Max(0.01f, _slideDuration);

            _canvasGroup.blocksRaycasts = opening;
            _canvasGroup.interactable = opening;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float smoothT = Mathf.SmoothStep(0f, 1f, t);
                float targetX = opening ? _openAnchoredX : _closedAnchoredX;
                float currentX = Mathf.Lerp(startX, targetX, smoothT);
                _overlayMenu.anchoredPosition = new Vector2(currentX, _overlayMenu.anchoredPosition.y);
                _canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, smoothT);

                yield return null;
            }

            float finalX = opening ? _openAnchoredX : _closedAnchoredX;
            _overlayMenu.anchoredPosition = new Vector2(finalX, _overlayMenu.anchoredPosition.y);
            _canvasGroup.alpha = targetAlpha;
            IsOpen = opening;
            _slideCoroutine = null;

            if (!opening)
            {
                onClosedComplete?.Invoke();
            }
        }

        private float ComputeClosedPositionX()
        {
            RectTransform parentRect = _overlayMenu.parent as RectTransform;
            float scaleX = Mathf.Abs(_overlayMenu.localScale.x);

            float openLocalX = _overlayMenu.localPosition.x + (_openAnchoredX - _overlayMenu.anchoredPosition.x);
            float openLeft = openLocalX + (_overlayMenu.rect.xMin * scaleX);
            float openRight = openLocalX + (_overlayMenu.rect.xMax * scaleX);

            if (_slideLeft)
            {
                float distanceToLeftEdge = openRight - parentRect.rect.xMin;
                return _openAnchoredX - distanceToLeftEdge;
            }

            float distanceToRightEdge = parentRect.rect.xMax - openLeft;
            return _openAnchoredX + distanceToRightEdge;
        }

        private Toggle GetMainTabToggle(FriendsTabType tab)
        {
            return tab switch
            {
                FriendsTabType.Friends => _friendsTab,
                FriendsTabType.Notifications => _notificationsTab,
                FriendsTabType.AddFriend => _addFriendTab,
                FriendsTabType.Blocked => _blockedTab,
                _ => _friendsTab
            };
        }

        private Toggle GetSubTabToggle(FriendsFilterType filter)
        {
            return filter switch
            {
                FriendsFilterType.Platform => _platformSubTab,
                FriendsFilterType.CrossPlay => _crossPlaySubTab,
                _ => _platformSubTab
            };
        }

        private void HandleFriendsToggled(bool isOn) { if (isOn) OnTabClicked?.Invoke(FriendsTabType.Friends); }
        private void HandleNotificationsToggled(bool isOn) { if (isOn) OnTabClicked?.Invoke(FriendsTabType.Notifications); }
        private void HandleAddFriendToggled(bool isOn) { if (isOn) OnTabClicked?.Invoke(FriendsTabType.AddFriend); }
        private void HandleBlockedToggled(bool isOn) { if (isOn) OnTabClicked?.Invoke(FriendsTabType.Blocked); }
        private void HandlePlatformSubTabToggled(bool isOn) { if (isOn) OnFilterClicked?.Invoke(FriendsFilterType.Platform); }
        private void HandleCrossPlaySubTabToggled(bool isOn) { if (isOn) OnFilterClicked?.Invoke(FriendsFilterType.CrossPlay); }
        private void HandleOpenClicked() => OnOpenClicked?.Invoke();
        private void HandleCloseClicked() => OnCloseClicked?.Invoke();

        public void Dispose()
        {
            if (_slideCoroutine != null)
            {
                _coroutineHost.StopCoroutine(_slideCoroutine);
                _slideCoroutine = null;
            }

            _friendsTab.onValueChanged.RemoveListener(HandleFriendsToggled);
            _notificationsTab.onValueChanged.RemoveListener(HandleNotificationsToggled);
            _addFriendTab.onValueChanged.RemoveListener(HandleAddFriendToggled);
            _blockedTab.onValueChanged.RemoveListener(HandleBlockedToggled);
            _platformSubTab.onValueChanged.RemoveListener(HandlePlatformSubTabToggled);
            _crossPlaySubTab.onValueChanged.RemoveListener(HandleCrossPlaySubTabToggled);
            _openButton.onClick.RemoveListener(HandleOpenClicked);
            _closeButton.onClick.RemoveListener(HandleCloseClicked);
            _backGroundButton.onClick.RemoveListener(HandleCloseClicked);
        }
    }
}
