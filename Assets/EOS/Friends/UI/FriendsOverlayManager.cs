using System;
using EOS.Test.Cursor;
using EOS.Lobby;
using EOS.Auth;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EOS.Friends
{
    // AI implementation: Master UI coordinator for the FriendsOverlay social hierarchy.
    // Coordinates header identity, tab filtering, search lookup, virtualized lazy-pooling list, and contextual actions menu.
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasGroup))]
    public class FriendsOverlayManager : MonoBehaviour, IPointerDownHandler
    {
        [Header("Header Elements")]
        [SerializeField] private Image localAvatarImage;
        [SerializeField] private Image localAvatarFrame;
        [SerializeField] private TMP_Text localUserNameText;
        [SerializeField] private Button copyUserCodeButton;
        [SerializeField] private TMP_Text copyUserCodeText;

        [Header("Navigation Tabs")]
        [SerializeField] private Button allFriendsTab;
        [SerializeField] private Button platformTab;
        [SerializeField] private Button crossPlayTab;
        [SerializeField] private Button notificationsTab;
        [SerializeField] private Button addFriendTab;
        [SerializeField] private Button blockedTab;

        [Header("Tab Styling")]
        [SerializeField] private Color tabActiveColor = new(0.2f, 0.6f, 1f, 1f);
        [SerializeField] private Color tabInactiveColor = new(0.25f, 0.25f, 0.25f, 1f);

        [Header("Search Elements")]
        [SerializeField] private TMP_InputField searchInputField;
        [SerializeField] private Button searchButton;

        [Header("Player List & Actions Menu")]
        [SerializeField] private LazyFriendsListView lazyListView;
        [SerializeField] private FriendActionsMenuUI actionsMenu;

        [Header("Overlay Visibility & Slide Animation")]
        [SerializeField] private Button openButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private float slideDuration = 0.25f;
        [SerializeField] private bool startClosed = true;

        [Header("Networking Mode")]
        [SerializeField] private bool useLobbySystem = true;

        public static FriendsOverlayManager Instance { get; private set; }

        private Button _activeTabButton;
        private RectTransform _rectTransform;
        private CanvasGroup _canvasGroup;
        private Coroutine _slideCoroutine;
        private bool _isOpen;

        private IFriendsService _friendsService;
        private ILobbyInviteService _lobbyInviteService;
        private ILocalPlayerIdentity _identity;

        private FriendsTabType _currentTab = FriendsTabType.AllFriends;
        private Sprite _createdLocalAvatarSprite;
        private Coroutine _copyFeedbackCoroutine;
        private Coroutine _pingCoroutine;
        private readonly List<FriendModel> _filteredFriends = new();

#if UNITY_EDITOR
        private void OnValidate()
        {
            FriendsManager.UseLobbySystem = useLobbySystem;
            TryGetComponent(out _rectTransform);
            TryGetComponent(out _canvasGroup);
            Assert.IsNotNull(_rectTransform, "[FriendsOverlayManager] RectTransform missing.");
            Assert.IsNotNull(_canvasGroup, "[FriendsOverlayManager] CanvasGroup missing.");
            Assert.IsNotNull(localUserNameText, "[FriendsOverlayManager] localUserNameText reference is missing.");
            Assert.IsNotNull(copyUserCodeButton, "[FriendsOverlayManager] copyUserCodeButton reference is missing.");
            Assert.IsNotNull(copyUserCodeText, "[FriendsOverlayManager] copyUserCodeText reference is missing.");
            Assert.IsNotNull(allFriendsTab, "[FriendsOverlayManager] allFriendsTab reference is missing.");
            Assert.IsNotNull(platformTab, "[FriendsOverlayManager] platformTab reference is missing.");
            Assert.IsNotNull(crossPlayTab, "[FriendsOverlayManager] crossPlayTab reference is missing.");
            Assert.IsNotNull(notificationsTab, "[FriendsOverlayManager] notificationsTab reference is missing.");
            Assert.IsNotNull(addFriendTab, "[FriendsOverlayManager] addFriendTab reference is missing.");
            Assert.IsNotNull(blockedTab, "[FriendsOverlayManager] blockedTab reference is missing.");
            Assert.IsNotNull(searchInputField, "[FriendsOverlayManager] searchInputField reference is missing.");
            Assert.IsNotNull(searchButton, "[FriendsOverlayManager] searchButton reference is missing.");
            Assert.IsNotNull(lazyListView, "[FriendsOverlayManager] lazyListView reference is missing.");
            Assert.IsNotNull(actionsMenu, "[FriendsOverlayManager] actionsMenu reference is missing.");
        }
#endif

        private void Awake()
        {
            FriendsManager.UseLobbySystem = useLobbySystem;
            Canvas rootCanvas = GetComponentInParent<Canvas>();
            GameObject rootObject = rootCanvas != null ? rootCanvas.gameObject : gameObject;

            if (Instance != null && Instance != this)
            {
                DestroyImmediate(rootObject);
                return;
            }
            Instance = this;
            rootObject.transform.SetParent(null);
            DontDestroyOnLoad(rootObject);

            LogoutManager.OnLogoutInitiated += HandleLoggedOut;
            AuthManager.OnLoggedOut += HandleLoggedOut;

            _rectTransform = GetComponent<RectTransform>();
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null) _canvasGroup = gameObject.AddComponent<CanvasGroup>();

            copyUserCodeButton.onClick.AddListener(HandleCopyUserCodeClicked);
            allFriendsTab.onClick.AddListener(HandleAllFriendsTabClicked);
            platformTab.onClick.AddListener(HandlePlatformTabClicked);
            crossPlayTab.onClick.AddListener(HandleCrossPlayTabClicked);
            notificationsTab.onClick.AddListener(HandleNotificationsTabClicked);
            addFriendTab.onClick.AddListener(HandleAddFriendTabClicked);
            blockedTab.onClick.AddListener(HandleBlockedTabClicked);

            searchButton.onClick.AddListener(HandleSearchButtonClicked);
            searchInputField.onValueChanged.AddListener(HandleSearchInputChanged);
            searchInputField.onSubmit.AddListener(HandleSearchInputSubmitted);

            openButton.onClick.AddListener(HandleOpenButtonClicked);
            closeButton.onClick.AddListener(HandleCloseButtonClicked);

            if (startClosed)
            {
                _isOpen = false;
                float closedX = GetClosedPositionX();
                _rectTransform.anchoredPosition = new Vector2(closedX, _rectTransform.anchoredPosition.y);
                _canvasGroup.alpha = 0f;
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.interactable = false;
            }
            else
            {
                _isOpen = true;
                float openX = GetOpenPositionX();
                _rectTransform.anchoredPosition = new Vector2(openX, _rectTransform.anchoredPosition.y);
                _canvasGroup.alpha = 1f;
                _canvasGroup.blocksRaycasts = true;
                _canvasGroup.interactable = true;
            }

            lazyListView.OnFriendClicked += HandleCardClicked;
            lazyListView.OnInviteClicked += HandleInviteCardClicked;
            lazyListView.OnScrolled += HandleListScrolled;
            lazyListView.OnBackgroundClicked += HandleBackgroundClicked;

            EnsureServices();
        }

        private void EnsureServices()
        {
            _identity ??= new LocalPlayerIdentity();
            _friendsService = FriendsManager.Instance;
            _lobbyInviteService = FriendsManager.Instance?.LobbyInvites;
            actionsMenu.Initialize(_friendsService, _lobbyInviteService);
        }

        private void Start()
        {
            actionsMenu.OnActionExecuted += HandleActionExecuted;

            UpdateLocalPlayerHeader();
            SelectTab(FriendsTabType.AllFriends);
        }

        private void OnEnable()
        {
            SubscribeServices();
            RefreshSocialData();
        }

        private void OnDisable()
        {
            if (_pingCoroutine != null)
            {
                StopCoroutine(_pingCoroutine);
                _pingCoroutine = null;
            }

            UnsubscribeServices();
            actionsMenu.Hide();
        }

        private void OnDestroy()
        {
            LogoutManager.OnLogoutInitiated -= HandleLoggedOut;
            AuthManager.OnLoggedOut -= HandleLoggedOut;

            Instance = null;

            UnsubscribeServices();
            lazyListView.OnFriendClicked -= HandleCardClicked;
            lazyListView.OnInviteClicked -= HandleInviteCardClicked;
            lazyListView.OnScrolled -= HandleListScrolled;
            lazyListView.OnBackgroundClicked -= HandleBackgroundClicked;
            actionsMenu.OnActionExecuted -= HandleActionExecuted;
            DestroyLocalAvatarSprite();
        }

        private void SubscribeServices()
        {
            _friendsService.OnFriendsListUpdated += HandleFriendsListUpdated;
            _friendsService.OnFriendStatusChanged += HandleFriendStatusChanged;
            _lobbyInviteService.OnInviteReceived += HandleLobbyInviteReceived;
            _lobbyInviteService.OnInviteRemoved += HandleLobbyInviteRemoved;

            if (useLobbySystem && LobbyManager.Instance != null)
            {
                LobbyManager.Instance.OnGameStarted -= HandleGameStarted;
                LobbyManager.Instance.OnGameStarted += HandleGameStarted;
            }
        }

        private void UnsubscribeServices()
        {
            if (_friendsService == null) return;

            _friendsService.OnFriendsListUpdated -= HandleFriendsListUpdated;
            _friendsService.OnFriendStatusChanged -= HandleFriendStatusChanged;
            _lobbyInviteService.OnInviteReceived -= HandleLobbyInviteReceived;
            _lobbyInviteService.OnInviteRemoved -= HandleLobbyInviteRemoved;

            if (useLobbySystem && LobbyManager.Instance != null)
            {
                LobbyManager.Instance.OnGameStarted -= HandleGameStarted;
            }
        }

        private void UpdateLocalPlayerHeader()
        {
            localUserNameText.text = _identity.DisplayName;
            copyUserCodeText.text = _identity.UserCode;

            UpdateLocalAvatar();
        }

        private void UpdateLocalAvatar()
        {

            DestroyLocalAvatarSprite();

            Texture2D tex = _identity?.AvatarTexture;
            if (tex != null)
            {
                _createdLocalAvatarSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                localAvatarImage.sprite = _createdLocalAvatarSprite;
                localAvatarImage.color = Color.white;
            }
            else
            {
                localAvatarImage.sprite = null;
                localAvatarImage.color = new Color(0.85f, 0.85f, 0.85f, 1f);
            }
        }

        private void DestroyLocalAvatarSprite()
        {
            if (_createdLocalAvatarSprite != null)
            {
                Destroy(_createdLocalAvatarSprite);
                _createdLocalAvatarSprite = null;
            }
        }

        public void SelectTab(FriendsTabType tab)
        {
            _currentTab = tab;
            UpdateTabVisuals();

            actionsMenu.Hide();

            // Clear search filter when switching tabs
            searchInputField.text = string.Empty;
            if (searchInputField.placeholder is TMP_Text placeholder)
            {
                placeholder.text = tab == FriendsTabType.AddFriend ? "Enter user code or account ID..." : "Search...";
            }

            PopulateTabContent();
        }

        private Button GetTabButton(FriendsTabType tab)
        {
            return tab switch
            {
                FriendsTabType.AllFriends => allFriendsTab,
                FriendsTabType.Platform => platformTab,
                FriendsTabType.CrossPlay => crossPlayTab,
                FriendsTabType.Notifications => notificationsTab,
                FriendsTabType.AddFriend => addFriendTab,
                FriendsTabType.Blocked => blockedTab,
                _ => allFriendsTab
            };
        }

        private void UpdateTabVisuals()
        {
            Button newButton = GetTabButton(_currentTab);
            if (_activeTabButton != null && _activeTabButton != newButton)
            {
                SetTabButtonVisual(_activeTabButton, false);
            }
            SetTabButtonVisual(newButton, true);
            _activeTabButton = newButton;
        }

        private void SetTabButtonVisual(Button btn, bool isActive)
        {
            Image img = btn.GetComponent<Image>();
            if (img != null)
            {
                img.color = isActive ? tabActiveColor : tabInactiveColor;
            }
        }

        private void PopulateTabContent()
        {
            string query = searchInputField.text.Trim();

            switch (_currentTab)
            {
                case FriendsTabType.AllFriends:
                    FilterAndDisplayFriends(_friendsService.AllFriends, query);
                    break;

                case FriendsTabType.Platform:
                    FilterAndDisplayPlatformFriends(_friendsService.AllFriends, query);
                    break;

                case FriendsTabType.CrossPlay:
                    FilterAndDisplayCrossPlayFriends(_friendsService.AllFriends, query);
                    break;

                case FriendsTabType.Notifications:
                    DisplayNotifications();
                    break;

                case FriendsTabType.Blocked:
                    FilterAndDisplayFriends(_friendsService.BlockedPlayers, query);
                    break;

                case FriendsTabType.AddFriend:
                    if (!string.IsNullOrEmpty(query))
                    {
                        ExecuteAddFriendLookup(query);
                    }
                    else
                    {
                        lazyListView.Clear();
                    }
                    break;
            }
        }

        private void FilterAndDisplayFriends(IReadOnlyList<FriendModel> source, string query)
        {
            _filteredFriends.Clear();
            for (int i = 0; i < source.Count; i++)
            {
                FriendModel f = source[i];
                if (MatchesQuery(f, query))
                {
                    _filteredFriends.Add(f);
                }
            }

            lazyListView.SetFriends(_filteredFriends);
        }

        private void FilterAndDisplayPlatformFriends(IReadOnlyList<FriendModel> source, string query)
        {
            _filteredFriends.Clear();
            for (int i = 0; i < source.Count; i++)
            {
                FriendModel f = source[i];
                if (f.Status == FriendRelationStatus.Platform && MatchesQuery(f, query))
                {
                    _filteredFriends.Add(f);
                }
            }

            lazyListView.SetFriends(_filteredFriends);
        }

        private void FilterAndDisplayCrossPlayFriends(IReadOnlyList<FriendModel> source, string query)
        {
            _filteredFriends.Clear();
            for (int i = 0; i < source.Count; i++)
            {
                FriendModel f = source[i];
                if (f.Status == FriendRelationStatus.CrossPlay && MatchesQuery(f, query))
                {
                    _filteredFriends.Add(f);
                }
            }

            lazyListView.SetFriends(_filteredFriends);
        }

        private void DisplayNotifications()
        {
            IReadOnlyList<FriendModel> requests = _friendsService?.IncomingRequests;
            IReadOnlyList<LobbyInviteModel> invites = _lobbyInviteService?.PendingInvites;
            lazyListView.SetNotifications(requests, invites);
        }

        private static bool MatchesQuery(FriendModel model, string query)
        {
            if (string.IsNullOrEmpty(query)) return true;
            if (model.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (model.ProductUserId.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (model.PlatformUserId.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private void ExecuteAddFriendLookup(string query)
        {
            if (_friendsService == null || string.IsNullOrWhiteSpace(query))
            {
                lazyListView.Clear();
                return;
            }

            lazyListView.Clear();
            _friendsService.ResolvePlayer(query, HandlePlayerLookupResult);
        }

        private void HandlePlayerLookupResult(PlayerLookupResult result)
        {
            _filteredFriends.Clear();
            if (result.Found && result.Player != null)
            {
                _filteredFriends.Add(result.Player);
            }
            lazyListView.SetFriends(_filteredFriends);
        }

        public void RefreshSocialData()
        {
            PopulateTabContent();
            _friendsService?.RefreshFriends(HandleRefreshCompleted);
            FriendsManager.Instance?.PingAllFriends();
        }

        private void HandleRefreshCompleted()
        {
            PopulateTabContent();
        }

        private void HandleFriendsListUpdated()
        {
            UpdateLocalPlayerHeader();
            PopulateTabContent();
        }

        private void HandleFriendStatusChanged(FriendModel model)
        {
            PopulateTabContent();
        }

        private void HandleLobbyInviteReceived(LobbyInviteModel invite)
        {
            if (_currentTab == FriendsTabType.Notifications)
            {
                PopulateTabContent();
            }
        }

        private void HandleLobbyInviteRemoved(string inviteId)
        {
            if (_currentTab == FriendsTabType.Notifications)
            {
                PopulateTabContent();
            }
        }

        private void HandleActionExecuted()
        {
            PopulateTabContent();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            actionsMenu?.Hide();
        }

        private void HandleBackgroundClicked()
        {
            actionsMenu?.Hide();
        }

        private void HandleCardClicked(FriendCardUI card, FriendModel model)
        {
            Assert.IsNotNull(actionsMenu, "[FriendsOverlayManager] actionsMenu is null.");
            Assert.IsNotNull(card, "[FriendsOverlayManager] card is null.");

            if (actionsMenu.IsOpen && actionsMenu.CurrentTargetFriend == model)
            {
                actionsMenu.Hide();
                return;
            }

            actionsMenu.Hide();
            actionsMenu.ShowForFriend(model, _currentTab, card.transform as RectTransform);
        }

        private void HandleInviteCardClicked(FriendCardUI card, LobbyInviteModel invite)
        {
            Assert.IsNotNull(actionsMenu, "[FriendsOverlayManager] actionsMenu is null.");
            Assert.IsNotNull(card, "[FriendsOverlayManager] card is null.");

            if (actionsMenu.IsOpen && actionsMenu.CurrentTargetInvite == invite)
            {
                actionsMenu.Hide();
                return;
            }

            actionsMenu.Hide();
            actionsMenu.ShowForInvite(invite, card.transform as RectTransform);
        }

        private void HandleListScrolled()
        {
            actionsMenu.Hide();
        }

        private void HandleAllFriendsTabClicked()
        {
            SelectTab(FriendsTabType.AllFriends);
        }

        private void HandlePlatformTabClicked()
        {
            SelectTab(FriendsTabType.Platform);
        }

        private void HandleCrossPlayTabClicked()
        {
            SelectTab(FriendsTabType.CrossPlay);
        }

        private void HandleNotificationsTabClicked()
        {
            SelectTab(FriendsTabType.Notifications);
        }

        private void HandleAddFriendTabClicked()
        {
            SelectTab(FriendsTabType.AddFriend);
        }

        private void HandleBlockedTabClicked()
        {
            SelectTab(FriendsTabType.Blocked);
        }

        private void HandleSearchButtonClicked()
        {
            actionsMenu?.Hide();
            if (_currentTab == FriendsTabType.AddFriend)
            {
                string q = searchInputField.text.Trim();
                ExecuteAddFriendLookup(q);
            }
            else
            {
                PopulateTabContent();
            }
        }

        private void HandleSearchInputChanged(string value)
        {
            actionsMenu?.Hide();
            if (_currentTab != FriendsTabType.AddFriend)
            {
                PopulateTabContent();
            }
        }

        private void HandleSearchInputSubmitted(string value)
        {
            HandleSearchButtonClicked();
        }

        public void OpenOverlay()
        {
            _isOpen = true;
            CursorManager.Instance?.SetCursorLocked(false);
            UpdateLocalPlayerHeader();
            RefreshSocialData();

            if (_slideCoroutine != null) StopCoroutine(_slideCoroutine);
            _slideCoroutine = StartCoroutine(SlideRoutine(GetOpenPositionX(), true));

            if (_pingCoroutine != null) StopCoroutine(_pingCoroutine);
            _pingCoroutine = StartCoroutine(PeriodicPingRoutine());
        }

        public void CloseOverlay()
        {
            if (_pingCoroutine != null)
            {
                StopCoroutine(_pingCoroutine);
                _pingCoroutine = null;
            }

            if (_slideCoroutine != null) StopCoroutine(_slideCoroutine);
            _slideCoroutine = StartCoroutine(SlideRoutine(GetClosedPositionX(), false));
        }

        private IEnumerator PeriodicPingRoutine()
        {
            var wait = new WaitForSeconds(10f);
            while (_isOpen)
            {
                yield return wait;
                FriendsManager.Instance?.PingAllFriends();
            }
        }

        public void ToggleOverlay()
        {
            if (_isOpen)
            {
                CloseOverlay();
            }
            else
            {
                OpenOverlay();
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

        private IEnumerator SlideRoutine(float targetX, bool opening)
        {
            float startX = _rectTransform.anchoredPosition.x;
            float startAlpha = _canvasGroup.alpha;
            float targetAlpha = opening ? 1f : 0f;
            float elapsed = 0f;
            float duration = Mathf.Max(0.01f, slideDuration);

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
            _isOpen = opening;
            _slideCoroutine = null;

            if (!opening)
            {
                actionsMenu.Hide();
                _canvasGroup.blocksRaycasts = false;
                _canvasGroup.interactable = false;
            }
        }

        private void HandleOpenButtonClicked()
        {
            OpenOverlay();
        }

        private void HandleCloseButtonClicked()
        {
            CloseOverlay();
        }

        private void HandleCopyUserCodeClicked()
        {
            actionsMenu?.Hide();
            string code = _identity?.UserCode;
            if (string.IsNullOrEmpty(code)) return;

            GUIUtility.systemCopyBuffer = code;

            if (_copyFeedbackCoroutine != null) StopCoroutine(_copyFeedbackCoroutine);
            _copyFeedbackCoroutine = StartCoroutine(ShowCopyFeedbackRoutine());
        }

        private IEnumerator ShowCopyFeedbackRoutine()
        {
            copyUserCodeText.text = "Copied!";
            yield return new WaitForSecondsRealtime(1.5f);
            copyUserCodeText.text = _identity.UserCode;
            _copyFeedbackCoroutine = null;
        }

        // AI implementation: Persists the Friends UI root across scenes when join-in-progress is allowed.
        public void PersistForGameScene()
        {
            actionsMenu?.Hide();

            Canvas canvas = GetComponentInParent<Canvas>();
            GameObject rootObject = canvas != null ? canvas.gameObject : gameObject;
            rootObject.transform.SetParent(null);
            DontDestroyOnLoad(rootObject);
        }

        // AI implementation: Callback triggered on lobby match start. Only persists UI if AllowJoinInProgress is enabled.
        private void HandleGameStarted(string hostAddress)
        {
            if (useLobbySystem && LobbyManager.Instance != null && LobbyManager.Instance.AllowJoinInProgress)
            {
                PersistForGameScene();
            }
        }

        // AI implementation: Destroys the Friends UI root when logging out to ensure clean lifecycle separation.
        private void HandleLoggedOut()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            Canvas canvas = GetComponentInParent<Canvas>();
            GameObject toDestroy = canvas != null ? canvas.gameObject : gameObject;
            Destroy(toDestroy);
        }
    }
}