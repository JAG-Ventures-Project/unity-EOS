using System.Collections;
using EOS.Auth;
using EOS.Lobby;
using TMPro;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EOS.Friends
{
    /// <summary>
    /// Master UI coordinator for the FriendsOverlay social hierarchy.
    /// Delegates header display, panel/tab view transitions, and search/filtering to focused presenters.
    /// </summary>
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

        private RectTransform _rectTransform;
        private CanvasGroup _canvasGroup;

        private IFriendsService _friendsService;
        private ILobbyInviteService _lobbyInviteService;
        private ILocalPlayerIdentity _identity;

        private FriendsOverlayHeaderView _headerView;
        private FriendsOverlayPanelView _panelView;
        private FriendsSearchPresenter _searchPresenter;

        private FriendsTabType _currentTab = FriendsTabType.AllFriends;
        private Coroutine _pingCoroutine;

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
            SessionAuthCoordinator.OnLoggedOut += HandleLoggedOut;

            _rectTransform = GetComponent<RectTransform>();
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null) _canvasGroup = gameObject.AddComponent<CanvasGroup>();

            EnsureServices();

            _headerView = new FriendsOverlayHeaderView(
                this,
                localAvatarImage,
                localUserNameText,
                copyUserCodeButton,
                copyUserCodeText,
                _identity,
                () => actionsMenu?.Hide());

            _panelView = new FriendsOverlayPanelView(
                this,
                _rectTransform,
                _canvasGroup,
                openButton,
                closeButton,
                allFriendsTab,
                platformTab,
                crossPlayTab,
                notificationsTab,
                addFriendTab,
                blockedTab,
                tabActiveColor,
                tabInactiveColor,
                slideDuration,
                startClosed);

            _panelView.OnTabClicked += SelectTab;
            _panelView.OnOpenClicked += OpenOverlay;
            _panelView.OnCloseClicked += CloseOverlay;

            _searchPresenter = new FriendsSearchPresenter(
                searchInputField,
                searchButton,
                lazyListView,
                () => _friendsService,
                () => _lobbyInviteService,
                () => _currentTab,
                () => actionsMenu?.Hide());

            lazyListView.OnFriendClicked += HandleCardClicked;
            lazyListView.OnInviteClicked += HandleInviteCardClicked;
            lazyListView.OnScrolled += HandleListScrolled;
            lazyListView.OnBackgroundClicked += HandleBackgroundClicked;
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

            _headerView.RefreshHeader();
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
            SessionAuthCoordinator.OnLoggedOut -= HandleLoggedOut;

            Instance = null;

            UnsubscribeServices();
            lazyListView.OnFriendClicked -= HandleCardClicked;
            lazyListView.OnInviteClicked -= HandleInviteCardClicked;
            lazyListView.OnScrolled -= HandleListScrolled;
            lazyListView.OnBackgroundClicked -= HandleBackgroundClicked;
            actionsMenu.OnActionExecuted -= HandleActionExecuted;

            _headerView?.Dispose();
            _panelView?.Dispose();
            _searchPresenter?.Dispose();
        }

        private void SubscribeServices()
        {
            if (_friendsService != null)
            {
                _friendsService.OnFriendsListUpdated += HandleFriendsListUpdated;
                _friendsService.OnFriendStatusChanged += HandleFriendStatusChanged;
            }

            if (_lobbyInviteService != null)
            {
                _lobbyInviteService.OnInviteReceived += HandleLobbyInviteReceived;
                _lobbyInviteService.OnInviteRemoved += HandleLobbyInviteRemoved;
            }

            if (useLobbySystem && LobbyManager.Instance != null)
            {
                LobbyManager.Instance.OnGameStarted -= HandleGameStarted;
                LobbyManager.Instance.OnGameStarted += HandleGameStarted;
            }
        }

        private void UnsubscribeServices()
        {
            if (_friendsService != null)
            {
                _friendsService.OnFriendsListUpdated -= HandleFriendsListUpdated;
                _friendsService.OnFriendStatusChanged -= HandleFriendStatusChanged;
            }

            if (_lobbyInviteService != null)
            {
                _lobbyInviteService.OnInviteReceived -= HandleLobbyInviteReceived;
                _lobbyInviteService.OnInviteRemoved -= HandleLobbyInviteRemoved;
            }

            if (useLobbySystem && LobbyManager.Instance != null)
            {
                LobbyManager.Instance.OnGameStarted -= HandleGameStarted;
            }
        }

        public void SelectTab(FriendsTabType tab)
        {
            _currentTab = tab;
            _panelView?.UpdateTabVisuals(tab);
            actionsMenu?.Hide();
            _searchPresenter?.OnTabSwitched(tab);
            _searchPresenter?.PopulateTab(tab);
        }

        public void RefreshSocialData()
        {
            _searchPresenter?.PopulateTab(_currentTab);
            _friendsService?.RefreshFriends(HandleRefreshCompleted);
            FriendsManager.Instance?.PingAllFriends();
        }

        public void OpenOverlay()
        {
            _panelView?.Open();
            _headerView?.RefreshHeader();
            RefreshSocialData();

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

            _panelView?.Close(() => actionsMenu?.Hide());
        }

        public void ToggleOverlay()
        {
            if (_panelView != null && _panelView.IsOpen)
            {
                CloseOverlay();
            }
            else
            {
                OpenOverlay();
            }
        }

        public void PersistForGameScene()
        {
            actionsMenu?.Hide();

            Canvas canvas = GetComponentInParent<Canvas>();
            GameObject rootObject = canvas != null ? canvas.gameObject : gameObject;
            rootObject.transform.SetParent(null);
            DontDestroyOnLoad(rootObject);
        }

        public void OnPointerDown(PointerEventData eventData) => actionsMenu?.Hide();

        private IEnumerator PeriodicPingRoutine()
        {
            var wait = new WaitForSeconds(10f);
            while (_panelView != null && _panelView.IsOpen)
            {
                yield return wait;
                FriendsManager.Instance?.PingAllFriends();
            }
        }

        private void HandleRefreshCompleted() => _searchPresenter?.PopulateTab(_currentTab);

        private void HandleFriendsListUpdated()
        {
            _headerView?.RefreshHeader();
            _searchPresenter?.PopulateTab(_currentTab);
        }

        private void HandleFriendStatusChanged(FriendModel model) => _searchPresenter?.PopulateTab(_currentTab);

        private void HandleLobbyInviteReceived(LobbyInviteModel invite)
        {
            if (_currentTab == FriendsTabType.Notifications)
            {
                _searchPresenter?.PopulateTab(_currentTab);
            }
        }

        private void HandleLobbyInviteRemoved(string inviteId)
        {
            if (_currentTab == FriendsTabType.Notifications)
            {
                _searchPresenter?.PopulateTab(_currentTab);
            }
        }

        private void HandleActionExecuted() => _searchPresenter?.PopulateTab(_currentTab);
        private void HandleBackgroundClicked() => actionsMenu?.Hide();
        private void HandleListScrolled() => actionsMenu?.Hide();

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

        private void HandleGameStarted(string hostAddress)
        {
            if (useLobbySystem && LobbyManager.Instance != null && LobbyManager.Instance.AllowJoinInProgress)
            {
                PersistForGameScene();
            }
        }

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
