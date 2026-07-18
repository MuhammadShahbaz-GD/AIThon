using System;
using DG.Tweening;
using UnityEngine;

namespace KickTheBuddy.Gameplay
{
    /// <summary>Reusable, authored DOTween entrance motion for a scene's primary UI canvas.</summary>
    [DisallowMultipleComponent]
    public sealed class SceneUIEntranceAnimator : MonoBehaviour
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private RectTransform[] targets = Array.Empty<RectTransform>();
        [Header("Entrance Feel")]
        [Range(.15f, 1f)] [SerializeField] private float fadeDuration = .32f;
        [Range(.2f, 1.2f)] [SerializeField] private float itemDuration = .48f;
        [Range(0f, .15f)] [SerializeField] private float stagger = .045f;
        [Range(0f, 120f)] [SerializeField] private float verticalOffset = 42f;
        [Range(.75f, 1f)] [SerializeField] private float startingScale = .92f;

        private Vector2[] positions = Array.Empty<Vector2>();
        private Vector3[] scales = Array.Empty<Vector3>();
        private Sequence entrance;

        private void Awake() => CacheAuthoredState();

        private void OnEnable()
        {
            if (positions.Length != targets.Length) CacheAuthoredState();
            PlayEntrance();
        }

        private void CacheAuthoredState()
        {
            if (targets == null) targets = Array.Empty<RectTransform>();
            positions = new Vector2[targets.Length];
            scales = new Vector3[targets.Length];
            for (int i = 0; i < targets.Length; i++)
            {
                RectTransform target = targets[i];
                if (target == null) continue;
                positions[i] = target.anchoredPosition;
                scales[i] = target.localScale;
            }
        }

        private void PlayEntrance()
        {
            entrance?.Kill(false);
            if (canvasGroup != null) canvasGroup.alpha = 0f;
            entrance = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            if (canvasGroup != null)
                entrance.Append(canvasGroup.DOFade(1f, fadeDuration).SetEase(Ease.OutQuad));

            for (int i = 0; i < targets.Length; i++)
            {
                RectTransform target = targets[i];
                if (target == null) continue;
                target.anchoredPosition = positions[i] + Vector2.down * verticalOffset;
                target.localScale = scales[i] * startingScale;
                float delay = .05f + i * stagger;
                entrance.Insert(delay,
                    target.DOAnchorPos(positions[i], itemDuration).SetEase(Ease.OutCubic));
                entrance.Insert(delay,
                    target.DOScale(scales[i], itemDuration).SetEase(Ease.OutBack));
            }
        }

        private void RestoreAuthoredState()
        {
            if (canvasGroup != null) canvasGroup.alpha = 1f;
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null) continue;
                targets[i].anchoredPosition = positions[i];
                targets[i].localScale = scales[i];
            }
        }

        private void OnDisable()
        {
            entrance?.Kill(false);
            entrance = null;
            RestoreAuthoredState();
        }

        private void OnValidate()
        {
            fadeDuration = Mathf.Clamp(fadeDuration, .15f, 1f);
            itemDuration = Mathf.Clamp(itemDuration, .2f, 1.2f);
            stagger = Mathf.Clamp(stagger, 0f, .15f);
            verticalOffset = Mathf.Clamp(verticalOffset, 0f, 120f);
            startingScale = Mathf.Clamp(startingScale, .75f, 1f);
            if (targets == null) targets = Array.Empty<RectTransform>();
        }
    }
}
