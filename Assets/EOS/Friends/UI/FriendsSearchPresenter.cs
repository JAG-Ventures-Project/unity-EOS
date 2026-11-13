using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine.UI;

namespace EOS.Friends
{
    /// <summary>
    /// Handles search input events, local friend list filtering by tab/query,
    /// notifications rendering, and Add Friend lookup requests.
    /// </summary>
    internal sealed class FriendsSearchPresenter : IDisposable
    {
        private readonly TMP_InputField _searchInputField;
        private readonly Button _searchButton;
        private readonly LazyFriendsListView _listView;
        private readonly Func<IFriendsService> _getFriendsService;
        private readonly Func<ILobbyInviteService> _getInviteService;
        private readonly Func<FriendsTabType> _getCurrentTab;
        private readonly Action _hideActionsMenu;

        private readonly List<FriendModel> _filteredFriends = new();

        public FriendsSearchPresenter(
            TMP_InputField searchInputField,
            Button searchButton,
            LazyFriendsListView listView,
            Func<IFriendsService> getFriendsService,
            Func<ILobbyInviteService> getInviteService,
            Func<FriendsTabType> getCurrentTab,
            Action hideActionsMenu)
        {
            _searchInputField = searchInputField;
            _searchButton = searchButton;
            _listView = listView;
            _getFriendsService = getFriendsService;
            _getInviteService = getInviteService;
            _getCurrentTab = getCurrentTab;
            _hideActionsMenu = hideActionsMenu;

            _searchButton?.onClick.AddListener(HandleSearchButtonClicked);
            _searchInputField?.onValueChanged.AddListener(HandleSearchInputChanged);
            _searchInputField?.onSubmit.AddListener(HandleSearchInputSubmitted);
        }

        public void OnTabSwitched(FriendsTabType tab)
        {
            if (_searchInputField == null) return;

            _searchInputField.text = string.Empty;
            if (_searchInputField.placeholder is TMP_Text placeholder)
            {
                placeholder.text = tab == FriendsTabType.AddFriend
                    ? "Enter user code or account ID..."
                    : "Search...";
            }
        }

        public void PopulateTab(FriendsTabType tab)
        {
            IFriendsService friendsService = _getFriendsService?.Invoke();
            if (friendsService == null || _listView == null) return;

            string query = _searchInputField != null ? _searchInputField.text.Trim() : string.Empty;

            switch (tab)
            {
                case FriendsTabType.AllFriends:
                    FilterAndDisplay(friendsService.AllFriends, query, null);
                    break;

                case FriendsTabType.Platform:
                    FilterAndDisplay(friendsService.AllFriends, query, FriendRelationStatus.Platform);
                    break;

                case FriendsTabType.CrossPlay:
                    FilterAndDisplay(friendsService.AllFriends, query, FriendRelationStatus.CrossPlay);
                    break;

                case FriendsTabType.Notifications:
                    DisplayNotifications(friendsService);
                    break;

                case FriendsTabType.Blocked:
                    FilterAndDisplay(friendsService.BlockedPlayers, query, null);
                    break;

                case FriendsTabType.AddFriend:
                    if (!string.IsNullOrEmpty(query))
                    {
                        ExecuteAddFriendLookup(query);
                    }
                    else
                    {
                        _listView.Clear();
                    }
                    break;
            }
        }

        private void FilterAndDisplay(IReadOnlyList<FriendModel> source, string query, FriendRelationStatus? requiredStatus)
        {
            _filteredFriends.Clear();
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    FriendModel f = source[i];
                    if (requiredStatus.HasValue && f.Status != requiredStatus.Value) continue;
                    if (MatchesQuery(f, query))
                    {
                        _filteredFriends.Add(f);
                    }
                }
            }

            _listView.SetFriends(_filteredFriends);
        }

        private void DisplayNotifications(IFriendsService friendsService)
        {
            IReadOnlyList<FriendModel> requests = friendsService?.IncomingRequests;
            IReadOnlyList<LobbyInviteModel> invites = _getInviteService?.Invoke()?.PendingInvites;
            _listView.SetNotifications(requests, invites);
        }

        private static bool MatchesQuery(FriendModel model, string query)
        {
            if (string.IsNullOrEmpty(query)) return true;
            if (!string.IsNullOrEmpty(model.DisplayName) &&
                model.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (!string.IsNullOrEmpty(model.ProductUserId) &&
                model.ProductUserId.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (!string.IsNullOrEmpty(model.PlatformUserId) &&
                model.PlatformUserId.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private void ExecuteAddFriendLookup(string query)
        {
            IFriendsService friendsService = _getFriendsService?.Invoke();
            if (friendsService == null || string.IsNullOrWhiteSpace(query))
            {
                _listView.Clear();
                return;
            }

            _listView.Clear();
            friendsService.ResolvePlayer(query, HandlePlayerLookupResult);
        }

        private void HandlePlayerLookupResult(PlayerLookupResult result)
        {
            _filteredFriends.Clear();
            if (result.Found && result.Player != null)
            {
                _filteredFriends.Add(result.Player);
            }
            _listView.SetFriends(_filteredFriends);
        }

        private void HandleSearchButtonClicked()
        {
            _hideActionsMenu?.Invoke();
            FriendsTabType currentTab = _getCurrentTab();
            if (currentTab == FriendsTabType.AddFriend)
            {
                string q = _searchInputField != null ? _searchInputField.text.Trim() : string.Empty;
                ExecuteAddFriendLookup(q);
            }
            else
            {
                PopulateTab(currentTab);
            }
        }

        private void HandleSearchInputChanged(string value)
        {
            _hideActionsMenu?.Invoke();
            FriendsTabType currentTab = _getCurrentTab();
            if (currentTab != FriendsTabType.AddFriend)
            {
                PopulateTab(currentTab);
            }
        }

        private void HandleSearchInputSubmitted(string value) => HandleSearchButtonClicked();

        public void Dispose()
        {
            _searchButton?.onClick.RemoveListener(HandleSearchButtonClicked);
            _searchInputField?.onValueChanged.RemoveListener(HandleSearchInputChanged);
            _searchInputField?.onSubmit.RemoveListener(HandleSearchInputSubmitted);
        }
    }
}

