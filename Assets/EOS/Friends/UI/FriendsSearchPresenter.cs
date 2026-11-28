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
        private readonly FriendsListController _listView;
        private readonly IFriendsService _friendsService;
        private readonly ILobbyInviteService _inviteService;
        private readonly Action _onBeforeSearch;

        private readonly List<FriendModel> _filteredFriends = new();
        private FriendsTabType _currentTab = FriendsTabType.Friends;
        private FriendsFilterType _currentFilter = FriendsFilterType.Platform;

        public FriendsSearchPresenter(
            TMP_InputField searchInputField,
            Button searchButton,
            FriendsListController listView,
            IFriendsService friendsService,
            ILobbyInviteService inviteService,
            Action onBeforeSearch)
        {
            _searchInputField = searchInputField;
            _searchButton = searchButton;
            _listView = listView;
            _friendsService = friendsService;
            _inviteService = inviteService;
            _onBeforeSearch = onBeforeSearch;

            _searchButton.onClick.AddListener(HandleSearchButtonClicked);
            _searchInputField.onSubmit.AddListener(HandleSearchInputSubmitted);
        }

        public void OnTabSwitched(FriendsTabType tab)
        {
            _currentTab = tab;
            if (tab == FriendsTabType.AddFriend)
            {
                _searchInputField.text = string.Empty;
            }
        }

        public void PopulateTab(FriendsTabType tab, FriendsFilterType filter)
        {
            _currentTab = tab;
            _currentFilter = filter;

            switch (tab)
            {
                case FriendsTabType.Friends:
                    FriendRelationStatus requiredStatus = filter == FriendsFilterType.Platform
                        ? FriendRelationStatus.Platform
                        : FriendRelationStatus.CrossPlay;
                    FilterByStatus(_friendsService.AllFriends, requiredStatus);
                    break;

                case FriendsTabType.Notifications:
                    _listView.SetNotifications(_friendsService.IncomingRequests, _inviteService.PendingInvites);
                    break;

                case FriendsTabType.Blocked:
                    _listView.SetFriends(_friendsService.BlockedPlayers, FriendsTabType.Blocked);
                    break;

                case FriendsTabType.AddFriend:
                    string query = _searchInputField.text.Trim();
                    if (!string.IsNullOrEmpty(query))
                        ExecuteAddFriendLookup(query);
                    else
                        _listView.Clear();
                    break;
            }
        }

        private void FilterByStatus(IReadOnlyList<FriendModel> source, FriendRelationStatus requiredStatus)
        {
            _filteredFriends.Clear();
            for (int i = 0; i < source.Count; i++)
            {
                FriendModel f = source[i];
                if (f.Status == requiredStatus)
                {
                    _filteredFriends.Add(f);
                }
            }

            _listView.SetFriends(_filteredFriends, FriendsTabType.Friends);
        }

        private void ExecuteAddFriendLookup(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                _listView.Clear();
                return;
            }

            _listView.Clear();
            _friendsService.ResolvePlayer(query, HandlePlayerLookupResult);
        }

        private void HandlePlayerLookupResult(PlayerLookupResult result)
        {
            if (_currentTab != FriendsTabType.AddFriend)
            {
                return;
            }

            _filteredFriends.Clear();
            if (result.Found && result.Player != null &&
                result.Player.Status != FriendRelationStatus.CrossPlay &&
                result.Player.Status != FriendRelationStatus.Platform &&
                result.Player.Status != FriendRelationStatus.Blocked)
            {
                _filteredFriends.Add(result.Player);
            }
            _listView.SetFriends(_filteredFriends, FriendsTabType.AddFriend);
        }

        private void HandleSearchButtonClicked()
        {
            _onBeforeSearch.Invoke();
            ExecuteAddFriendLookup(_searchInputField.text.Trim());
        }

        private void HandleSearchInputSubmitted(string value) => HandleSearchButtonClicked();

        public void Dispose()
        {
            _searchButton.onClick.RemoveListener(HandleSearchButtonClicked);
            _searchInputField.onSubmit.RemoveListener(HandleSearchInputSubmitted);
        }
    }
}
