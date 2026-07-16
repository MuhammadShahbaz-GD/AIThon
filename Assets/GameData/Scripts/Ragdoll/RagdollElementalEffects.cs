using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace KickTheBuddy.Physics
{
    public enum ElementalState { Normal, Burning, Frozen, Dissolving }

    /// <summary>Coordinates elemental state while each limb retains ownership of its physics.</summary>
    [DisallowMultipleComponent]
    public sealed class RagdollElementalEffects : MonoBehaviour
    {
        [Header("Burning")]
        [SerializeField] private GameObject fireParticlesPrefab;
        [SerializeField] private Color charredColor = new Color(0.12f, 0.09f, 0.08f, 1f);
        [Min(0.1f)] [SerializeField] private float burnDuration = 6f;
        [Min(0f)] [SerializeField] private float burnDamagePerSecond = 4f;
        [Header("Frozen")]
        [SerializeField] private Color frozenTint = new Color(0.45f, 0.8f, 1f, 1f);
        [Min(0f)] [SerializeField] private float frozenShatterImpact = 35f;
        [Header("Dissolving")]
        [Min(0.1f)] [SerializeField] private float dissolveDuration = 2.5f;

        private RagdollController controller;
        private RagdollDamageManager damageManager;
        private SpriteRenderer[] renderers = Array.Empty<SpriteRenderer>();
        private Color[] originalColors = Array.Empty<Color>();
        private DismemberableLimb[] limbs = Array.Empty<DismemberableLimb>();
        private Rigidbody2D[] bodies = Array.Empty<Rigidbody2D>();
        private RigidbodyConstraints2D[] originalConstraints = Array.Empty<RigidbodyConstraints2D>();
        private Coroutine effectRoutine;
        private GameObject fireInstance;

        public event Action<ElementalState, ElementalState> StateChanging;
        public event Action<ElementalState> StateChanged;
        public event Action<DismemberableLimb, Vector2> FrozenShatterStarted;
        public event Action DissolveCompleted;

        public ElementalState State { get; private set; } = ElementalState.Normal;
        internal void Initialize(RagdollController owner, RagdollDamageManager damage,
            IReadOnlyList<RagdollController.RagdollPart> authoredParts)
        {
            controller = owner;
            damageManager = damage;
            int count = authoredParts != null ? authoredParts.Count : 0;
            renderers = new SpriteRenderer[count];
            originalColors = new Color[count];
            limbs = new DismemberableLimb[count];
            bodies = new Rigidbody2D[count];
            originalConstraints = new RigidbodyConstraints2D[count];
            for (int i = 0; i < count; i++)
            {
                RagdollController.RagdollPart part = authoredParts[i];
                renderers[i] = part.Visual;
                originalColors[i] = part.Visual != null ? part.Visual.color : Color.white;
                limbs[i] = part.DismemberableLimb;
                bodies[i] = part.Body;
                originalConstraints[i] = part.Body != null ? part.Body.constraints : RigidbodyConstraints2D.None;
                part.DamageReceiver?.SetElementalEffects(this);
            }
        }

        public void SetState(ElementalState newState)
        {
            if (State == newState) return;
            ElementalState previous = State;
            StateChanging?.Invoke(previous, newState);
            StopCurrentEffect();
            RestoreVisualsAndPhysics();
            State = newState;

            switch (newState)
            {
                case ElementalState.Burning: effectRoutine = StartCoroutine(BurnRoutine()); break;
                case ElementalState.Frozen: ApplyFrozen(); break;
                case ElementalState.Dissolving: effectRoutine = StartCoroutine(DissolveRoutine()); break;
            }
            StateChanged?.Invoke(State);
        }

        public void NotifyImpact(DismemberableLimb hitLimb, float impactMagnitude, Vector2 point)
        {
            if (State != ElementalState.Frozen || impactMagnitude < frozenShatterImpact) return;
            FrozenShatterStarted?.Invoke(hitLimb, point);
            for (int i = 0; i < limbs.Length; i++)
            {
                if (limbs[i] == null || limbs[i].IsSevered) continue;
                Vector2 direction = ((Vector2)limbs[i].transform.position - point).normalized;
                limbs[i].ForceSever(direction * impactMagnitude, point);
            }
        }

        private IEnumerator BurnRoutine()
        {
            if (fireParticlesPrefab != null) fireInstance = Instantiate(fireParticlesPrefab, transform);
            float elapsed = 0f;
            var wait = new WaitForFixedUpdate();
            while (elapsed < burnDuration)
            {
                elapsed += Time.fixedDeltaTime;
                float t = Mathf.Clamp01(elapsed / burnDuration);
                for (int i = 0; i < renderers.Length; i++)
                    if (renderers[i] != null) renderers[i].color = Color.Lerp(originalColors[i], charredColor, t);
                float damage = burnDamagePerSecond * Time.fixedDeltaTime;
                for (int i = 0; i < bodies.Length; i++)
                    if (bodies[i] != null) damageManager?.ApplyDirectDamage(
                        bodies[i], damage, damage, Vector2.zero, bodies[i].worldCenterOfMass);
                yield return wait;
            }
            effectRoutine = null;
        }

        private void ApplyFrozen()
        {
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].color = originalColors[i] * frozenTint;
            controller?.SetLimpState(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i] == null) continue;
                bodies[i].velocity = Vector2.zero;
                bodies[i].angularVelocity = 0f;
                bodies[i].constraints = RigidbodyConstraints2D.FreezeAll;
            }
        }

        private IEnumerator DissolveRoutine()
        {
            Vector3 initialScale = transform.localScale;
            float elapsed = 0f;
            while (elapsed < dissolveDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / dissolveDuration);
                transform.localScale = Vector3.Lerp(initialScale, Vector3.zero, t);
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] == null) continue;
                    Color color = originalColors[i]; color.a *= 1f - t; renderers[i].color = color;
                }
                yield return null;
            }
            effectRoutine = null;
            DissolveCompleted?.Invoke();
            Destroy(gameObject);
        }
        private void RestoreVisualsAndPhysics()
        {
            for (int i = 0; i < renderers.Length; i++) if (renderers[i] != null) renderers[i].color = originalColors[i];
            for (int i = 0; i < bodies.Length; i++) if (bodies[i] != null) bodies[i].constraints = originalConstraints[i];
            if (controller != null && State == ElementalState.Frozen) controller.SetLimpState(false);
        }

        private void StopCurrentEffect()
        {
            if (effectRoutine != null) StopCoroutine(effectRoutine);
            effectRoutine = null;
            if (fireInstance != null) Destroy(fireInstance);
            fireInstance = null;
        }

        private void OnDisable() { StopCurrentEffect(); }
    }
}
