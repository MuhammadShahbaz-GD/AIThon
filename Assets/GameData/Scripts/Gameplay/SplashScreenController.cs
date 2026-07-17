using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace KickTheBuddy.Gameplay
{
    /// <summary>Presentation-only splash timing. The persistent flow service performs the transition.</summary>
    [DisallowMultipleComponent]
    public sealed class SplashScreenController : MonoBehaviour
    {
        [Min(.1f)] [SerializeField] private float minimumDisplaySeconds = 1.5f;
        [SerializeField] private Image progressFill;
        [SerializeField] private Text statusText;

        private IEnumerator Start()
        {
            if (progressFill != null) progressFill.fillAmount = 0f;
            if (statusText != null) statusText.text = "LOADING";

            float elapsed = 0f;
            while (elapsed < minimumDisplaySeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                if (progressFill != null) progressFill.fillAmount = Mathf.Clamp01(elapsed / minimumDisplaySeconds);
                yield return null;
            }

            GameBootstrapper root = GameBootstrapper.Instance;
            if (root == null || root.Flow == null)
            {
                if (statusText != null) statusText.text = "SETUP REQUIRED";
                Debug.LogError("Splash could not find GameBootstrapper. Run Tools/Game/Build Full Game Flow.", this);
                yield break;
            }

            root.Flow.ShowMainMenu();
        }

        private void OnValidate() => minimumDisplaySeconds = Mathf.Max(.1f, minimumDisplaySeconds);
    }
}
