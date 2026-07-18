using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace KickTheBuddy.Gameplay
{
    /// <summary>Plays the authored menu intro once, then reveals the saved-game Play command.</summary>
    [DisallowMultipleComponent]
    public sealed class MainMenuVideoSequenceController : MonoBehaviour
    {
        [SerializeField] private VideoPlayer videoPlayer;
        [SerializeField] private CanvasGroup staticScreen;
        [SerializeField] private Button playButton;
        [Min(1f)] [SerializeField] private float preparationTimeout = 6f;

        private Sequence revealSequence;
        private Coroutine prepareTimeout;
        private GameBootstrapper root;
        private bool revealed;

        public bool IsReadyToPlay => revealed && playButton != null && playButton.interactable;

        private void Awake() => HideStaticScreen();

        private void OnEnable()
        {
            if (videoPlayer == null) return;
            videoPlayer.prepareCompleted += HandlePrepared;
            videoPlayer.loopPointReached += HandleFinished;
            videoPlayer.errorReceived += HandleVideoError;
        }

        private void Start()
        {
            root = GameBootstrapper.Instance;
            if (root != null)
            {
                root.Sounds?.StopMusic(.18f);
                if (videoPlayer != null && videoPlayer.audioTrackCount > 0)
                    videoPlayer.SetDirectAudioVolume(0, root.Saves.Data.soundVolume);
            }

            if (videoPlayer == null || videoPlayer.clip == null)
            {
                RevealStaticScreen();
                return;
            }

            videoPlayer.Prepare();
            prepareTimeout = StartCoroutine(WaitForPreparation());
        }

        private IEnumerator WaitForPreparation()
        {
            float elapsed = 0f;
            while (!revealed && videoPlayer != null && !videoPlayer.isPrepared && elapsed < preparationTimeout)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            prepareTimeout = null;
            if (!revealed && (videoPlayer == null || !videoPlayer.isPrepared))
                RevealStaticScreen();
        }

        private void HandlePrepared(VideoPlayer source)
        {
            if (revealed || source == null) return;
            source.Play();
        }

        private void HandleFinished(VideoPlayer source) => RevealStaticScreen();

        private void HandleVideoError(VideoPlayer source, string message)
        {
            Debug.LogWarning("Main Menu intro video could not play. Showing the static menu instead: " + message, this);
            RevealStaticScreen();
        }

        private void HideStaticScreen()
        {
            revealed = false;
            if (staticScreen != null)
            {
                staticScreen.gameObject.SetActive(true);
                staticScreen.alpha = 0f;
                staticScreen.interactable = false;
                staticScreen.blocksRaycasts = false;
            }
            if (playButton != null) playButton.interactable = false;
        }

        private void RevealStaticScreen()
        {
            if (revealed) return;
            revealed = true;
            if (prepareTimeout != null)
            {
                StopCoroutine(prepareTimeout);
                prepareTimeout = null;
            }
            if (videoPlayer != null)
            {
                videoPlayer.Stop();
                videoPlayer.enabled = false;
            }
            root?.Sounds?.PlayMusic(false, .35f);

            if (staticScreen == null)
            {
                if (playButton != null) playButton.interactable = true;
                return;
            }

            staticScreen.gameObject.SetActive(true);
            staticScreen.alpha = 0f;
            staticScreen.interactable = false;
            staticScreen.blocksRaycasts = false;
            if (playButton != null) playButton.transform.localScale = Vector3.one * .72f;

            revealSequence?.Kill(false);
            revealSequence = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            revealSequence.Append(staticScreen.DOFade(1f, .38f).SetEase(Ease.OutQuad));
            if (playButton != null)
                revealSequence.Join(playButton.transform.DOScale(1f, .58f).SetEase(Ease.OutBack));
            revealSequence.OnComplete(() =>
            {
                if (staticScreen == null) return;
                staticScreen.interactable = true;
                staticScreen.blocksRaycasts = true;
                if (playButton != null) playButton.interactable = true;
            });
        }

        private void OnDisable()
        {
            if (videoPlayer != null)
            {
                videoPlayer.prepareCompleted -= HandlePrepared;
                videoPlayer.loopPointReached -= HandleFinished;
                videoPlayer.errorReceived -= HandleVideoError;
                videoPlayer.Stop();
            }
            if (prepareTimeout != null) StopCoroutine(prepareTimeout);
            prepareTimeout = null;
            revealSequence?.Kill(false);
            revealSequence = null;
        }

        private void OnValidate() => preparationTimeout = Mathf.Max(1f, preparationTimeout);
    }
}
