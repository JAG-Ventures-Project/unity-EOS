using System.Collections;
using EOS.Auth;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EOS.Friends
{
    /// <summary>
    /// Master UI coordinator for the FriendsOverlay social hierarchy.
    /// Delegates header display, panel/tab view transitions, search/filtering, and list selection.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class FriendsOverlayManager : MonoBehaviour, IPointerDownHandler
    {
        [Header("Header Elements")]
        [SerializeField] private Image localAvatarImage;
        [SerializeField] private TMP_Text localUserNameText;
        [SerializeField] private Button copyUserCodeButton;
        [SerializeField] private TMP_Text copyUserCodeText;

        [Header("Navigation Tabs")]
        [SerializeField] private Toggle friendsTab;
        [SerializeField] private Toggle notificationsTab;
        [SerializeField] private Toggle addFriendTab;
        [SerializeField] private Toggle blockedTab;

        [Header("Friends Sub-Menu")]
        [SerializeField] private GameObject friendsSubMenuContainer;
        [SerializeField] private Toggle platformSubTab;
        [SerializeField] private Toggle crossPlaySubTab;

        [Header("Notification Badge")]
        [SerializeField] private GameObject notificationBadge;
        [SerializeField] private TMP_Text notificationBadgeText;

        [Header("Search Elements")]
        [SerializeField] private GameObject searchContainer;
        [SerializeField] private TMP_InputField searchInputField;
        [SerializeField] private Button searchButton;

        [Header("Layout")]
        [SerializeField] private VerticalLayoutGroup bottomSectionLayoutGroup;

        [Header("Overlay Visibility & Slide Animation")]
        [SerializeField] private Button openButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private RectTransform overlayMenu;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private float slideDuration = 0.25f;
        [SerializeField] private bool slideLeft = false;
        [SerializeField] private bool startClosed = true;

        [Header("Networking Mode")]
        [SerializeField] private bool useLobbySystem = true;

        public static FriendsOverlayManager Instance { get; private set; }

        private FriendsListController _friendsListView;

        private IFriendsService _friendsService;
        private ILobbyInviteService _lobbyInviteService;
        private ILocalPlayerIdentity _identity;

        private FriendsOverlayHeaderView _headerView;
        private FriendsOverlayPanelView _panelView;
        private FriendsSearchPresenter _searchPresenter;
        private FriendActionExecutor _actionExecutor;

        private FriendsTabType _currentTab = FriendsTabType.Friends;
        private FriendsFilterType _currentFriendsFilter = FriendsFilterType.Platform;
        private Coroutine _pingCoroutine;
        private readonly WaitForSeconds _pingWait = new(10f);
        private RectTransform _rectTransform;
        private Vector2 _lastCanvasSize;
        private bool _isHandlingDimensionChange;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (UnityEditor.EditorUtility.IsPersistent(this))
            {
                return;
            }

            FriendsManager.UseLobbySystem = useLobbySystem;
            _friendsListView = GetComponentInChildren<FriendsListController>(true);
            if (overlayMenu == null) Debug.LogWarning("[FriendsOverlayManager] overlayMenu reference is missing.", this);
            if (canvasGroup == null) Debug.LogWarning("[FriendsOverlayManager] canvasGroup reference is missing.", this);
            if (localAvatarImage == null) Debug.LogWarning("[FriendsOverlayManager] localAvatarImage reference is missing.", this);
            if (localUserNameText == null) Debug.LogWarning("[FriendsOverlayManager] localUserNameText reference is missing.", this);
            if (copyUserCodeButton == null) Debug.LogWarning("[FriendsOverlayManager] copyUserCodeButton reference is missing.", this);
            if (copyUserCodeText == null) Debug.LogWarning("[FriendsOverlayManager] copyUserCodeText reference is missing.", this);
            if (friendsTab == null) Debug.LogWarning("[FriendsOverlayManager] friendsTab reference is missing.", this);
            if (notificationsTab == null) Debug.LogWarning("[FriendsOverlayManager] notificationsTab reference is missing.", this);
            if (addFriendTab == null) Debug.LogWarning("[FriendsOverlayManager] addFriendTab reference is missing.", this);
            if (blockedTab == null) Debug.LogWarning("[FriendsOverlayManager] blockedTab reference is missing.", this);
            if (friendsSubMenuContainer == null) Debug.LogWarning("[FriendsOverlayManager] friendsSubMenuContainer reference is missing.", this);
            if (platformSubTab == null) Debug.LogWarning("[FriendsOverlayManager] platformSubTab reference is missing.", this);
            if (crossPlaySubTab == null) Debug.LogWarning("[FriendsOverlayManager] crossPlaySubTab reference is missing.", this);
            if (notificationBadge == null) Debug.LogWarning("[FriendsOverlayManager] notificationBadge reference is missing.", this);
            if (notificationBadgeText == null) Debug.LogWarning("[FriendsOverlayManager] notificationBadgeText reference is missing.", this);
            if (searchContainer == null) Debug.LogWarning("[FriendsOverlayManager] searchContainer reference is missing.", this);
            if (searchInputField == null) Debug.LogWarning("[FriendsOverlayManager] searchInputField reference is missing.", this);
            if (searchButton == null) Debug.LogWarning("[FriendsOverlayManager] searchButton reference is missing.", this);
            if (bottomSectionLayoutGroup == null) Debug.LogWarning("[FriendsOverlayManager] bottomSectionLayoutGroup reference is missing.", this);
            if (_friendsListView == null) Debug.LogWarning("[FriendsOverlayManager] FriendsListController missing in children.", this);
            if (openButton == null) Debug.LogWarning("[FriendsOverlayManager] openButton is missing.", this);
            if (closeButton == null) Debug.LogWarning("[FriendsOverlayManager] closeButton is missing.", this);

            friendsSubMenuContainer.SetActive(false);
            searchContainer.SetActive(false);
        }
#endif

        private void Awake()
        {
            FriendsManager.UseLobbySystem = useLobbySystem;

            if (Instance != null && Instance != this)
            {
                DestroyImmediate(openButton.GetComponentInParent<Canvas>().gameObject);
                DestroyImmediate(gameObject);
                return;
            }
            Instance = this;

            GameObject openButtonCanvas = openButton.GetComponentInParent<Canvas>().gameObject;
            openButtonCanvas.transform.SetParent(null);
            DontDestroyOnLoad(openButtonCanvas);
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);

            LogoutManager.OnLogoutInitiated += HandleLoggedOut;
            SessionAuthCoordinator.OnLoggedOut += HandleLoggedOut;

            _friendsListView = GetComponentInChildren<FriendsListController>(true);
            _identity = new LocalPlayerIdentity();
            _friendsService = FriendsManager.Instance;
            _lobbyInviteService = FriendsManager.Instance.LobbyInvites;
            _actionExecutor = new FriendActionExecutor();
            _actionExecutor.Initialize(_friendsService, _lobbyInviteService);

            _headerView = new FriendsOverlayHeaderView(
                this,
                localAvatarImage,
                localUserNameText,
                copyUserCodeButton,
                copyUserCodeText,
                _identity,
                HandleClearSelection);

            _panelView = new FriendsOverlayPanelView(
                this,
                overlayMenu,
                canvasGroup,
                openButton,
                closeButton,
                friendsTab,
                notificationsTab,
                addFriendTab,
                blockedTab,
                friendsSubMenuContainer,
                platformSubTab,
                crossPlaySubTab,
                searchContainer,
                notificationBadge,
                notificationBadgeText,
                bottomSectionLayoutGroup,
                slideDuration,
                slideLeft,
                startClosed);

            _panelView.OnTabClicked += SelectTab;
            _panelView.OnFilterClicked += SelectFriendsFilter;
            _panelView.OnOpenClicked += OpenOverlay;
            _panelView.OnCloseClicked += CloseOverlay;

            _searchPresenter = new FriendsSearchPresenter(
                searchInputField,
                searchButton,
                _friendsListView,
                _friendsService,
                _lobbyInviteService,
                HandleClearSelection);

            _friendsListView.Initialize(_actionExecutor);

            _rectTransform = (RectTransform)transform;
            _lastCanvasSize = _rectTransform.rect.size;
        }

        private void Start()
        {
            _headerView.RefreshHeader();
            ApplyTabSelection(FriendsTabType.Friends);
            UpdateNotificationBadge();
            _friendsService.RefreshFriends(RefreshBadgeAndVisibleList);
            FriendsManager.Instance.PingAllFriends();
        }

        private void OnEnable()
        {
            if (Instance != this) return;
            SubscribeServices();
        }

        private void OnRectTransformDimensionsChange()
        {
            RefreshLayoutOnDimensionsChange();
        }

        private void RefreshLayoutOnDimensionsChange()
        {
            if (_panelView == null || _isHandlingDimensionChange || !gameObject.activeInHierarchy)
            {
                return;
            }

            Vector2 currentSize = _rectTransform.rect.size;
            if (currentSize == _lastCanvasSize)
            {
                return;
            }

            _isHandlingDimensionChange = true;
            try
            {
                _panelView.RefreshOverlayAnchors();
                _panelView.UpdateTabContentLayout(_currentTab);

                _lastCanvasSize = _rectTransform.rect.size;
            }
            finally
            {
                _isHandlingDimensionChange = false;
            }
        }

        private void OnDisable()
        {
            if (Instance != this) return;

            if (_pingCoroutine != null)
            {
                StopCoroutine(_pingCoroutine);
                _pingCoroutine = null;
            }

            UnsubscribeServices();
            _friendsListView.ClearSelection();
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;

            LogoutManager.OnLogoutInitiated -= HandleLoggedOut;
            SessionAuthCoordinator.OnLoggedOut -= HandleLoggedOut;

            _panelView.OnTabClicked -= SelectTab;
            _panelView.OnFilterClicked -= SelectFriendsFilter;
            _panelView.OnOpenClicked -= OpenOverlay;
            _panelView.OnCloseClicked -= CloseOverlay;

            _headerView.Dispose();
            _panelView.Dispose();
            _searchPresenter.Dispose();
        }

        private void SubscribeServices()
        {
            _actionExecutor.OnActionExecuted += RefreshBadgeAndVisibleList;
            _friendsService.OnFriendsListUpdated += RefreshBadgeAndVisibleList;
            _lobbyInviteService.OnInviteReceived += HandleLobbyInviteReceived;
            _lobbyInviteService.OnInviteRemoved += HandleLobbyInviteRemoved;
        }

        private void UnsubscribeServices()
        {
            _actionExecutor.OnActionExecuted -= RefreshBadgeAndVisibleList;
            _friendsService.OnFriendsListUpdated -= RefreshBadgeAndVisibleList;

            _lobbyInviteService.OnInviteReceived -= HandleLobbyInviteReceived;
            _lobbyInviteService.OnInviteRemoved -= HandleLobbyInviteRemoved;
        }

        public void SelectTab(FriendsTabType tab)
        {
            if (_currentTab == tab)
            {
                return;
            }

            ApplyTabSelection(tab);
        }

        private void ApplyTabSelection(FriendsTabType tab)
        {
            _currentTab = tab;
            _friendsListView.ClearSelection();
            _panelView.UpdateTabContentLayout(tab);
            _panelView.UpdateTabVisuals(tab, _currentFriendsFilter);
            _searchPresenter.OnTabSwitched(tab);
            _searchPresenter.PopulateTab(tab, _currentFriendsFilter);
        }

        public void SelectFriendsFilter(FriendsFilterType filter)
        {
            _currentFriendsFilter = filter;
            _friendsListView.ClearSelection();
            _panelView.UpdateTabVisuals(_currentTab, _currentFriendsFilter);
            _searchPresenter.PopulateTab(_currentTab, _currentFriendsFilter);
        }

        public void RefreshSocialData()
        {
            _searchPresenter.PopulateTab(_currentTab, _currentFriendsFilter);
            UpdateNotificationBadge();
            _friendsService.RefreshFriends(RefreshBadgeAndVisibleList);
            FriendsManager.Instance.PingAllFriends();
        }

        public void OpenOverlay()
        {
            _panelView.Open();
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

            _panelView.Close(HandleClearSelection);
        }

        public void ToggleOverlay()
        {
            if (_panelView.IsOpen)
            {
                CloseOverlay();
            }
            else
            {
                OpenOverlay();
            }
        }

        public void OnPointerDown(PointerEventData eventData) => _friendsListView.ClearSelection();

        private void UpdateNotificationBadge()
        {
            int count = _friendsService.IncomingRequests.Count + _lobbyInviteService.PendingInvites.Count;
            _panelView.UpdateNotificationBadge(count);
        }

        private IEnumerator PeriodicPingRoutine()
        {
            while (_panelView.IsOpen)
            {
                yield return _pingWait;
                FriendsManager.Instance.PingAllFriends();
            }
        }

        private void HandleClearSelection() => _friendsListView.ClearSelection();
        private void HandleLobbyInviteReceived(LobbyInviteModel invite) => RefreshBadgeAndNotificationsTab();
        private void HandleLobbyInviteRemoved(string inviteId) => RefreshBadgeAndNotificationsTab();

        private void RefreshBadgeAndVisibleList()
        {
            UpdateNotificationBadge();
            _searchPresenter.PopulateTab(_currentTab, _currentFriendsFilter);
        }

        private void RefreshBadgeAndNotificationsTab()
        {
            UpdateNotificationBadge();
            if (_currentTab == FriendsTabType.Notifications)
            {
                _searchPresenter.PopulateTab(_currentTab, _currentFriendsFilter);
            }
        }

        private void HandleLoggedOut()
        {
            LogoutManager.OnLogoutInitiated -= HandleLoggedOut;
            SessionAuthCoordinator.OnLoggedOut -= HandleLoggedOut;

            Destroy(openButton.GetComponentInParent<Canvas>().gameObject);
            Destroy(gameObject);
        }
    }
}
