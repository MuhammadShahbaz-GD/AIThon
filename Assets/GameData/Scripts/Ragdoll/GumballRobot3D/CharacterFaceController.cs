using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace KickTheBuddy.Ragdoll3D
{
    public enum ExpressionType
    {
        Idle,
        Happy,
        Sad,
        Surprised,
        Dizzy,
        Laughing
    }

    [Serializable]
    public struct ExpressionSprite
    {
        public ExpressionType expression;
        public Sprite sprite;
    }

    /// <summary>Owns persistent and temporary sprite expressions for the spherical head face.</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterFaceController : MonoBehaviour
    {
        [Header("Face Renderer")]
        public SpriteRenderer faceRenderer;

        [Header("Expression Library")]
        public List<ExpressionSprite> expressions = new List<ExpressionSprite>(6);
        [SerializeField] private ExpressionType initialExpression = ExpressionType.Idle;
        [SerializeField] private bool temporaryExpressionsUseUnscaledTime;

        private Coroutine temporaryExpressionCoroutine;
        private ExpressionType persistentExpression;
        private ExpressionType currentExpression;

        public ExpressionType CurrentExpression => currentExpression;
        public bool IsPlayingTemporaryExpression => temporaryExpressionCoroutine != null;

        public event Action<ExpressionType> ExpressionChanged;
        public event Action<ExpressionType> ExpressionMissing;

        private void Awake()
        {
            persistentExpression = initialExpression;
            ApplyExpression(initialExpression);
        }

        /// <summary>Sets a persistent expression and cancels any temporary override.</summary>
        public void SetExpression(ExpressionType expression)
        {
            StopTemporaryExpression(false);
            persistentExpression = expression;
            ApplyExpression(expression);
        }

        /// <summary>
        /// Temporarily overrides the face, then returns to the last persistent expression.
        /// Starting another temporary expression replaces the current override without changing
        /// the persistent state. The coroutine allocates only when this command is invoked.
        /// </summary>
        public Coroutine TriggerTemporaryExpression(ExpressionType expression, float duration)
        {
            if (!isActiveAndEnabled || duration <= 0f) return null;
            if (!TryGetSprite(expression, out Sprite sprite))
            {
                ExpressionMissing?.Invoke(expression);
                return null;
            }

            StopTemporaryExpression(false);
            ApplySprite(expression, sprite);
            temporaryExpressionCoroutine = StartCoroutine(TemporaryExpressionRoutine(duration));
            return temporaryExpressionCoroutine;
        }

        /// <summary>Cancels an active temporary expression and optionally restores the persistent one.</summary>
        public void CancelTemporaryExpression(bool restorePersistentExpression = true)
        {
            StopTemporaryExpression(restorePersistentExpression);
        }

        private IEnumerator TemporaryExpressionRoutine(float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += temporaryExpressionsUseUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                yield return null;
            }

            temporaryExpressionCoroutine = null;
            ApplyExpression(persistentExpression);
        }

        private bool ApplyExpression(ExpressionType expression)
        {
            if (!TryGetSprite(expression, out Sprite sprite))
            {
                ExpressionMissing?.Invoke(expression);
                return false;
            }

            ApplySprite(expression, sprite);
            return true;
        }

        private void ApplySprite(ExpressionType expression, Sprite sprite)
        {
            if (faceRenderer != null) faceRenderer.sprite = sprite;
            currentExpression = expression;
            ExpressionChanged?.Invoke(expression);
        }

        private bool TryGetSprite(ExpressionType expression, out Sprite sprite)
        {
            int count = expressions != null ? expressions.Count : 0;
            for (int i = 0; i < count; i++)
            {
                ExpressionSprite entry = expressions[i];
                if (entry.expression != expression || entry.sprite == null) continue;
                sprite = entry.sprite;
                return true;
            }

            sprite = null;
            return false;
        }

        private void StopTemporaryExpression(bool restorePersistentExpression)
        {
            if (temporaryExpressionCoroutine != null)
            {
                StopCoroutine(temporaryExpressionCoroutine);
                temporaryExpressionCoroutine = null;
            }

            if (restorePersistentExpression) ApplyExpression(persistentExpression);
        }

        private void OnDisable()
        {
            StopTemporaryExpression(true);
        }

        private void OnValidate()
        {
            if (expressions == null) expressions = new List<ExpressionSprite>(6);
        }
    }
}
