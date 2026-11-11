using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EOS
{
    // AI implementation
    // lazy-pooling scroll view for the friends list.
    // Instantiates only the minimum cards required to fill the viewport plus a 2-item buffer.
    // Recycles card GameObjects dynamically during scrolling with zero runtime GC allocations.
    [RequireComponent(typeof(ScrollRect))]
    internal class LazyFriendsListView : MonoBehaviour, IPointerDownHandler
    {
        [Header("Scroll References")]
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private RectTransform viewport;
        [SerializeField] private RectTransform content;
        [SerializeField] private FriendCardUI cardTemplate;

        [Header("Layout Configuration")]
        [SerializeField] private float spacing = 4f;
        [SerializeField] private float topPadding = 4f;
        [SerializeField] private float bottomPadding = 4f;

        private readonly List<SocialItem> _items = new();
        private readonly List<FriendCardUI> _cardPool = new();
        private readonly List<FriendCardUI> _activeCards = new();

        private float _cardHeight = 52f;
        private float _itemStride = 56f;
        private int _visibleBufferCount = 10;
        private int _lastFirstIndex = -1;
        private int _lastLastIndex = -1;

        public event Action<FriendCardUI, FriendModel> OnFriendClicked;
        public event Action<FriendCardUI, LobbyInviteModel> OnInviteClicked;
        public event Action OnScrolled;
        public event Action OnBackgroundClicked;

        public void OnPointerDown(PointerEventData eventData)
        {
            OnBackgroundClicked?.Invoke();
        }

        internal enum SocialItemType
        {
            Friend = 0,
            LobbyInvite = 1
        }

        internal readonly struct SocialItem
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

#if UNITY_EDITOR
        private void OnValidate()
        {
            Assert.IsNotNull(scrollRect, "[LazyFriendsListView] ScrollRect component missing.");
            Assert.IsNotNull(viewport, "[LazyFriendsListView] Viewport RectTransform missing.");
            Assert.IsNotNull(content, "[LazyFriendsListView] Content RectTransform missing.");
            Assert.IsNotNull(cardTemplate, "[LazyFriendsListView] CardTemplate reference missing.");
        }
#endif

        private void Awake()
        {
            if (scrollRect == null) scrollRect = GetComponent<ScrollRect>();


            scrollRect.onValueChanged.AddListener(HandleScrollValueChanged);

            InitializeMetrics();
            cardTemplate.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            scrollRect.onValueChanged.RemoveListener(HandleScrollValueChanged);
            ReleaseAllActiveCards();
        }

        private void InitializeMetrics()
        {
            RectTransform templateRect = cardTemplate.transform as RectTransform;
            _cardHeight = templateRect.rect.height;

            _itemStride = _cardHeight + spacing;

            float vpHeight = viewport != null ? viewport.rect.height : 400f;
            _visibleBufferCount = Mathf.CeilToInt(vpHeight / _itemStride) + 3;
        }

        public void SetFriends(IReadOnlyList<FriendModel> friends)
        {
            _items.Clear();
            if (friends != null)
            {
                for (int i = 0; i < friends.Count; i++)
                {
                    _items.Add(new SocialItem(friends[i]));
                }
            }

            ApplyDataChange();
        }

        public void SetNotifications(IReadOnlyList<FriendModel> requests, IReadOnlyList<LobbyInviteModel> invites)
        {
            _items.Clear();
            if (requests != null)
            {
                for (int i = 0; i < requests.Count; i++)
                {
                    _items.Add(new SocialItem(requests[i]));
                }
            }
            if (invites != null)
            {
                for (int i = 0; i < invites.Count; i++)
                {
                    _items.Add(new SocialItem(invites[i]));
                }
            }

            ApplyDataChange();
        }

        public void Clear()
        {
            _items.Clear();
            ApplyDataChange();
        }

        private void ApplyDataChange()
        {
            InitializeMetrics();

            int totalCount = _items.Count;
            float totalHeight = topPadding + bottomPadding + (totalCount * _itemStride);
            content.sizeDelta = new Vector2(content.sizeDelta.x, totalHeight);

            // Reset scroll position to top
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, 0f);

            _lastFirstIndex = -1;
            _lastLastIndex = -1;

            ReleaseAllActiveCards();
            UpdateVisibleWindow();
        }

        private void HandleScrollValueChanged(Vector2 pos)
        {
            UpdateVisibleWindow();
            OnScrolled?.Invoke();
        }

        private void UpdateVisibleWindow()
        {
            int totalCount = _items.Count;
            if (totalCount == 0)
            {
                ReleaseAllActiveCards();
                return;
            }

            float scrollY = content.anchoredPosition.y;
            int firstVisibleIndex = Mathf.Clamp(Mathf.FloorToInt((scrollY - topPadding) / _itemStride), 0, Mathf.Max(0, totalCount - 1));
            int lastVisibleIndex = Mathf.Clamp(firstVisibleIndex + _visibleBufferCount - 1, 0, totalCount - 1);

            if (firstVisibleIndex == _lastFirstIndex && lastVisibleIndex == _lastLastIndex)
            {
                return;
            }

            _lastFirstIndex = firstVisibleIndex;
            _lastLastIndex = lastVisibleIndex;

            ReleaseAllActiveCards();

            for (int i = firstVisibleIndex; i <= lastVisibleIndex; i++)
            {
                FriendCardUI card = GetOrCreatePooledCard();
                ConfigureCardTransform(card, i);
                BindCardItem(card, _items[i]);

                card.OnClicked += HandleCardClicked;
                card.OnInviteClicked += HandleInviteClicked;
                card.gameObject.SetActive(true);

                _activeCards.Add(card);
            }
        }

        private void ConfigureCardTransform(FriendCardUI card, int itemIndex)
        {
            RectTransform rt = card.transform as RectTransform;

            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(0f, _cardHeight);

            float posY = -(topPadding + (itemIndex * _itemStride) + (_cardHeight * 0.5f));
            rt.anchoredPosition = new Vector2(0f, posY);
        }

        private static void BindCardItem(FriendCardUI card, SocialItem item)
        {
            if (item.Type == SocialItemType.Friend)
            {
                card.Bind(item.Friend);
            }
            else
            {
                card.BindLobbyInvite(item.LobbyInvite);
            }
        }

        private FriendCardUI GetOrCreatePooledCard()
        {
            for (int i = 0; i < _cardPool.Count; i++)
            {
                FriendCardUI card = _cardPool[i];
                if (!card.gameObject.activeSelf && !_activeCards.Contains(card))
                {
                    return card;
                }
            }

            Assert.IsNotNull(cardTemplate, "[LazyFriendsListView] cardTemplate is null!");
            GameObject newObj = Instantiate(cardTemplate.gameObject, content);
            var newCard = newObj.GetComponent<FriendCardUI>();
            Assert.IsNotNull(newCard, "[LazyFriendsListView] Spawned object missing FriendCardUI component.");

            _cardPool.Add(newCard);
            return newCard;
        }

        private void ReleaseAllActiveCards()
        {
            for (int i = 0; i < _activeCards.Count; i++)
            {
                FriendCardUI card = _activeCards[i];
                card.OnClicked -= HandleCardClicked;
                card.OnInviteClicked -= HandleInviteClicked;
                card.gameObject.SetActive(false);
            }
            _activeCards.Clear();
        }

        private void HandleCardClicked(FriendCardUI card, FriendModel model)
        {
            OnFriendClicked?.Invoke(card, model);
        }

        private void HandleInviteClicked(FriendCardUI card, LobbyInviteModel invite)
        {
            OnInviteClicked?.Invoke(card, invite);
        }
    }
}

