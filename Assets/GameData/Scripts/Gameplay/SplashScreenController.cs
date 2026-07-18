using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace KickTheBuddy.Gameplay
{
    /// <summary>Presentation-only splash timing. The persistent flow service performs the transition.</summary>
    [DisallowMultipleComponent]
    public sealed class SplashScreenController : MonoBehaviour
    {
        [Range(3f, 4f)] [SerializeField] private float minimumDisplaySeconds = 3.5f;
        [SerializeField] private CanvasGroup splashGroup;
        [SerializeField] private RectTransform artwork;
        [SerializeField] private RectTransform loadingGroup;
        [SerializeField] private Image progressFill;
        [SerializeField] private Text statusText;

        private Sequence presentation;

        private IEnumerator Start()
        {
            if (progressFill != null) progressFill.fillAmount = 0f;
            if (statusText != null) statusText.text = "LOADING...";

            PlayPresentation();

            float elapsed = 0f;
            while (elapsed < minimumDisplaySeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            GameBootstrapper root = GameBootstrapper.Instance;
            if (root == null || root.Flow == null)
            {
                if (statusText != null) statusText.text = "SETUP REQUIRED";
                Debug.LogError("Splash could not find GameBootstrapper. Run Tools/Game/Build Full Game Flow.", this);
                yield break;
            }

            if (presentation != null) presentation.Kill(false);
            Sequence exit = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            if (splashGroup != null) exit.Join(splashGroup.DOFade(0f, .28f));
            if (artwork != null) exit.Join(artwork.DOScale(1.035f, .28f).SetEase(Ease.InCubic));
            yield return exit.WaitForCompletion();
            root.Flow.ShowMainMenu();
        }

        private void PlayPresentation()
        {
            presentation?.Kill(false);
            if (splashGroup != null) splashGroup.alpha = 0f;
            if (artwork != null) artwork.localScale = Vector3.one * 1.07f;
            if (loadingGroup != null) loadingGroup.localScale = Vector3.one * .82f;

            presentation = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            if (splashGroup != null)
                presentation.Append(splashGroup.DOFade(1f, .42f).SetEase(Ease.OutQuad));
            if (artwork != null)
                presentation.Join(artwork.DOScale(1f, 1.7f).SetEase(Ease.OutCubic));
            if (loadingGroup != null)
            {
                presentation.Insert(.28f,
                    loadingGroup.DOScale(1f, .62f).SetEase(Ease.OutBack));
                presentation.Insert(.95f,
                    loadingGroup.DOPunchScale(Vector3.one * .045f, 1.8f, 3, .25f));
            }
            if (progressFill != null)
                presentation.Insert(0f,
                    progressFill.DOFillAmount(1f, minimumDisplaySeconds).SetEase(Ease.InOutSine));
            if (statusText != null)
                presentation.Insert(.55f,
                    statusText.DOFade(.55f, .65f).SetLoops(4, LoopType.Yoyo));
        }

        private void OnDisable()
        {
            presentation?.Kill(false);
            presentation = null;
        }

        private void OnValidate() => minimumDisplaySeconds = Mathf.Clamp(minimumDisplaySeconds, 3f, 4f);
    }
}
