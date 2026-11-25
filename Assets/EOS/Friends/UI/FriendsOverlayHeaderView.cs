using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EOS.Friends
{
    /// <summary>
    /// Renders and updates the local player's header identity (display name, user code, avatar)
    /// and handles clipboard copy feedback.
    /// </summary>
    internal sealed class FriendsOverlayHeaderView : IDisposable
    {
        private readonly MonoBehaviour _coroutineHost;
        private readonly Image _localAvatarImage;
        private readonly TMP_Text _localUserNameText;
        private readonly Button _copyUserCodeButton;
        private readonly TMP_Text _copyUserCodeText;

        private ILocalPlayerIdentity _identity;
        private Action _beforeCopyAction;
        private Sprite _createdLocalAvatarSprite;
        private Coroutine _copyFeedbackCoroutine;
        private readonly WaitForSecondsRealtime _copyFeedbackWait = new(1.5f);

        public FriendsOverlayHeaderView(
            MonoBehaviour coroutineHost,
            Image localAvatarImage,
            TMP_Text localUserNameText,
            Button copyUserCodeButton,
            TMP_Text copyUserCodeText,
            ILocalPlayerIdentity identity,
            Action beforeCopyAction)
        {
            _coroutineHost = coroutineHost;
            _localAvatarImage = localAvatarImage;
            _localUserNameText = localUserNameText;
            _copyUserCodeButton = copyUserCodeButton;
            _copyUserCodeText = copyUserCodeText;
            _identity = identity;
            _beforeCopyAction = beforeCopyAction;

            _copyUserCodeButton?.onClick.AddListener(HandleCopyUserCodeClicked);
        }

        public void SetIdentity(ILocalPlayerIdentity identity)
        {
            _identity = identity;
        }

        public void RefreshHeader()
        {
            _localUserNameText.text = _identity.DisplayName;
            _copyUserCodeText.text = _identity.UserCode;

            UpdateLocalAvatar();
        }

        private void UpdateLocalAvatar()
        {
            DestroyLocalAvatarSprite();
            if (_localAvatarImage == null) return;

            Texture2D tex = _identity?.AvatarTexture;
            if (tex != null)
            {
                _createdLocalAvatarSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                _localAvatarImage.sprite = _createdLocalAvatarSprite;
                _localAvatarImage.color = Color.white;
            }
            else
            {
                _localAvatarImage.sprite = null;
                _localAvatarImage.color = new Color(0.1843f, 0.1843f, 0.2039f, 1f);
            }
        }

        private void HandleCopyUserCodeClicked()
        {
            _beforeCopyAction?.Invoke();
            string code = _identity?.UserCode;
            if (string.IsNullOrEmpty(code)) return;

            GUIUtility.systemCopyBuffer = code;

            if (_copyFeedbackCoroutine != null)
            {
                _coroutineHost.StopCoroutine(_copyFeedbackCoroutine);
            }
            _copyFeedbackCoroutine = _coroutineHost.StartCoroutine(ShowCopyFeedbackRoutine());
        }

        private IEnumerator ShowCopyFeedbackRoutine()
        {
            _copyUserCodeText.text = "Copied!";
            yield return _copyFeedbackWait;
            _copyUserCodeText.text = _identity.UserCode;
            _copyFeedbackCoroutine = null;
        }

        private void DestroyLocalAvatarSprite()
        {
            if (_createdLocalAvatarSprite != null)
            {
                UnityEngine.Object.Destroy(_createdLocalAvatarSprite);
                _createdLocalAvatarSprite = null;
            }
        }

        public void Dispose()
        {
            if (_copyFeedbackCoroutine != null)
            {
                _coroutineHost.StopCoroutine(_copyFeedbackCoroutine);
                _copyFeedbackCoroutine = null;
            }

            _copyUserCodeButton?.onClick.RemoveListener(HandleCopyUserCodeClicked);
            DestroyLocalAvatarSprite();
        }
    }
}

