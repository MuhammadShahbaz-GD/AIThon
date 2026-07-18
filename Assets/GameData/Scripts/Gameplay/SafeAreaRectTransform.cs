using UnityEngine;

namespace KickTheBuddy.Gameplay
{
    /// <summary>
    /// Keeps edge-anchored HUD controls inside display cutouts while preserving
    /// the authored 1920x1080 composition on ordinary landscape displays.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaRectTransform : MonoBehaviour
    {
        private RectTransform rectTransform;
        private Rect appliedSafeArea;
        private Vector2Int appliedScreenSize;

        private void Awake()
        {
            rectTransform = (RectTransform)transform;
            ApplyIfChanged();
        }

        private void OnEnable()
        {
            if (rectTransform == null) rectTransform = (RectTransform)transform;
            ApplyIfChanged();
        }

        private void OnRectTransformDimensionsChange()
        {
            if (rectTransform != null) ApplyIfChanged();
        }

        private void ApplyIfChanged()
        {
            Rect safeArea = Screen.safeArea;
            var screenSize = new Vector2Int(Screen.width, Screen.height);
            if (screenSize.x <= 0 || screenSize.y <= 0 ||
                (safeArea == appliedSafeArea && screenSize == appliedScreenSize))
                return;

            Vector2 anchorMin = safeArea.position;
            Vector2 anchorMax = safeArea.position + safeArea.size;
            anchorMin.x /= screenSize.x;
            anchorMin.y /= screenSize.y;
            anchorMax.x /= screenSize.x;
            anchorMax.y /= screenSize.y;

            // Cache first because changing anchors can synchronously notify child RectTransforms.
            appliedSafeArea = safeArea;
            appliedScreenSize = screenSize;
            rectTransform.anchorMin = anchorMin;
            rectTransform.anchorMax = anchorMax;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
        }
    }
}
