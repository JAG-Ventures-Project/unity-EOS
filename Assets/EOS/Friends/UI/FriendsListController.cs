using System;
using System.Collections.Generic;
using LazyList;

namespace EOS.Friends
{
    // player card list controller handling item binding and single-card selection.
    public class FriendsListController : LazyListController<FriendsListController.SocialItem, FriendCardUI>
    {
        public enum SocialItemType
        {
            Friend = 0,
            LobbyInvite = 1
        }

        public readonly struct SocialItem
        {
            public SocialItemType Type { get; }
            public FriendModel Friend { get; }
            public LobbyInviteModel LobbyInvite { get; }

            public SocialItem(FriendModel friend)
            {
                Type = SocialItemType.Friend;
                Friend = friend;
                LobbyInvite = null;
            }

            public SocialItem(LobbyInviteModel invite)
            {
                Type = SocialItemType.LobbyInvite;
                Friend = null;
                LobbyInvite = invite;
            }
        }

        private FriendActionExecutor _actionExecutor;
        private Action<FriendActionType, FriendModel, LobbyInviteModel> _actionHandler;
        private FriendsTabType _currentTab = FriendsTabType.Friends;
        private int _selectedModelIndex = -1;

        internal void Initialize(FriendActionExecutor actionExecutor)
        {
            _actionExecutor = actionExecutor;
            _actionHandler ??= HandleCardActionClicked;
        }

        public void SetFriends(IReadOnlyList<FriendModel> friends, FriendsTabType tab = FriendsTabType.Friends)
        {
            _currentTab = tab;
            _selectedModelIndex = -1;

            int count = friends?.Count ?? 0;
            var items = new SocialItem[count];
            for (int i = 0; i < count; i++)
            {
                items[i] = new SocialItem(friends[i]);
            }
            SetData(items);
        }

        public void SetNotifications(IReadOnlyList<FriendModel> requests, IReadOnlyList<LobbyInviteModel> invites)
        {
            _currentTab = FriendsTabType.Notifications;
            _selectedModelIndex = -1;

            int reqCount = requests?.Count ?? 0;
            int invCount = invites?.Count ?? 0;
            int total = reqCount + invCount;

            var items = new SocialItem[total];
            int idx = 0;
            for (int i = 0; i < reqCount; i++)
            {
                items[idx++] = new SocialItem(requests[i]);
            }
            for (int i = 0; i < invCount; i++)
            {
                items[idx++] = new SocialItem(invites[i]);
            }

            SetData(items);
        }

        public void Clear()
        {
            _selectedModelIndex = -1;
            SetData(Array.Empty<SocialItem>());
        }

        public void ToggleSelection(FriendCardUI card)
        {
            _selectedModelIndex = _selectedModelIndex == card.ModelIndex ? -1 : card.ModelIndex;
            RefreshSelectionStates();
        }

        public void ClearSelection()
        {
            if (_selectedModelIndex == -1)
            {
                return;
            }
            _selectedModelIndex = -1;
            RefreshSelectionStates();
        }

        protected override void OnViewCreated(FriendCardUI view)
        {
            view.OnClicked += HandleCardClicked;
        }

        protected override void OnViewDestroyed(FriendCardUI view)
        {
            view.OnClicked -= HandleCardClicked;
        }

        protected override void BindView(FriendCardUI view, SocialItem data)
        {
            if (data.Type == SocialItemType.Friend)
            {
                view.Bind(data.Friend, _currentTab, _actionExecutor.FriendsService, _actionHandler);
            }
            else
            {
                view.BindLobbyInvite(data.LobbyInvite, _actionExecutor.FriendsService, _actionHandler);
            }

            view.SetSelected(view.ModelIndex == _selectedModelIndex);
        }

        private void RefreshSelectionStates()
        {
            for (int i = 0; i < ActiveViewsCount; i++)
            {
                FriendCardUI view = ViewPool[i];
                view.SetSelected(view.ModelIndex == _selectedModelIndex);
            }
        }

        private void HandleCardClicked(FriendCardUI card) => ToggleSelection(card);


        private void HandleCardActionClicked(FriendActionType action, FriendModel model, LobbyInviteModel invite)
        {
            ClearSelection();
            _actionExecutor.Execute(action, model, invite);
        }
    }
}
