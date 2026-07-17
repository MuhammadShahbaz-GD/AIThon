using System;
using KickTheBuddy.Gameplay;
using KickTheBuddy.Physics;
using UnityEngine;

namespace KickTheBuddy.VFX
{
    /// <summary>
    /// Presentation-only Jelly contact feedback. It owns a small authored pool, so contacts never
    /// instantiate objects and can never enter the ragdoll damage pipeline.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class JellyContactVFXController : MonoBehaviour
    {
        [Header("Authored References")]
        [SerializeField] private SandboxTool2D jellyTool;
        [SerializeField] private RagdollController ragdoll;
        [SerializeField] private RagdollAnimationController animationController;
        [SerializeField] private SpriteRenderer[] splatRenderers = Array.Empty<SpriteRenderer>();
        [SerializeField] private ParticleSystem dropletParticle;

        [Header("Contact Filter")]
        [Min(0f)] [SerializeField] private float minimumImpactSpeed = 1.5f;

        [Header("Liquid Slide")]
        [Min(.1f)] [SerializeField] private float effectDuration = 1.6f;
        [Min(0f)] [SerializeField] private float slideDistance = .48f;
        [Range(0f, .95f)] [SerializeField] private float fadeStartNormalized = .55f;
        [SerializeField] private Vector2 splatSizeRange = new Vector2(.28f, .44f);
        [SerializeField] private Color liquidColor = new Color(.63f, .22f, .95f, .82f);

        [Header("Character Reaction")]
        [Min(.15f)] [SerializeField] private float annoyanceDuration = .9f;

        private SplatSlot[] slots = Array.Empty<SplatSlot>();
        private int nextSlot;
        private int activeEffectCount;
        private int completedEffectCount;

        public event Action<Rigidbody2D, Vector2, float> LiquidEffectStarted;
        public event Action<Rigidbody2D> LiquidEffectFinished;

        public int ActiveEffectCount => activeEffectCount;
        public int CompletedEffectCount => completedEffectCount;
        public Vector2 LastContactPoint { get; private set; }

        private struct SplatSlot
        {
            public Rigidbody2D Body;
            public Transform Target;
            public Vector3 LocalContactPoint;
            public float Age;
            public float HorizontalDrift;
            public Vector3 Scale;
            public bool Active;
        }

        private void Awake()
        {
            int count = splatRenderers != null ? splatRenderers.Length : 0;
            slots = new SplatSlot[count];
            for (int i = 0; i < count; i++)
                if (splatRenderers[i] != null) splatRenderers[i].enabled = false;

            if (dropletParticle != null)
                dropletParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void OnEnable()
        {
            if (jellyTool != null) jellyTool.Impacted += HandleImpact;
            if (ragdoll != null)
            {
                ragdoll.OnCharacterDied += HandleCharacterDied;
                ragdoll.OnCharacterRevived += ClearEffects;
            }
        }

        private void OnDisable()
        {
            if (jellyTool != null) jellyTool.Impacted -= HandleImpact;
            if (ragdoll != null)
            {
                ragdoll.OnCharacterDied -= HandleCharacterDied;
                ragdoll.OnCharacterRevived -= ClearEffects;
            }
            ClearEffects();
        }

        private void LateUpdate()
        {
            if (activeEffectCount == 0) return;

            float deltaTime = Time.deltaTime;
            for (int i = 0; i < slots.Length; i++)
            {
                if (!slots[i].Active) continue;
                SplatSlot slot = slots[i];
                slot.Age += deltaTime;
                if (slot.Target == null || slot.Age >= effectDuration)
                {
                    FinishSlot(i, slot.Body);
                    continue;
                }

                float normalizedAge = Mathf.Clamp01(slot.Age / effectDuration);
                float slide = normalizedAge * normalizedAge * (3f - 2f * normalizedAge);
                Vector3 contactPoint = slot.Target.TransformPoint(slot.LocalContactPoint);
                SpriteRenderer renderer = splatRenderers[i];
                if (renderer != null)
                {
                    renderer.transform.position = contactPoint + new Vector3(
                        slot.HorizontalDrift * normalizedAge,
                        -slideDistance * slide,
                        0f);
                    renderer.transform.localScale = slot.Scale * Mathf.Lerp(1f, .72f, normalizedAge);
                    Color color = liquidColor;
                    float fade = normalizedAge <= fadeStartNormalized
                        ? 1f
                        : 1f - Mathf.InverseLerp(fadeStartNormalized, 1f, normalizedAge);
                    color.a *= fade;
                    renderer.color = color;
                }
                slots[i] = slot;
            }
        }

        /// <summary>Public deterministic entry point used by the contact event and Play Mode tests.</summary>
        public bool TryPlay(SandboxTool2D tool, Rigidbody2D target, float impactSpeed, Vector2 point)
        {
            if (tool == null || tool != jellyTool || tool.Kind != SandboxToolKind.Jelly || tool.IsDragging ||
                target == null || impactSpeed < minimumImpactSpeed || ragdoll == null ||
                ragdoll.CurrentHealth <= 0f || !IsAuthoredRagdollPart(target) || slots.Length == 0)
                return false;

            int index = nextSlot;
            nextSlot = (nextSlot + 1) % slots.Length;
            if (slots[index].Active) FinishSlot(index, slots[index].Body);

            float strength = Mathf.InverseLerp(minimumImpactSpeed, minimumImpactSpeed + 10f, impactSpeed);
            float size = UnityEngine.Random.Range(splatSizeRange.x, splatSizeRange.y) * Mathf.Lerp(.9f, 1.2f, strength);
            slots[index] = new SplatSlot
            {
                Body = target,
                Target = target.transform,
                LocalContactPoint = target.transform.InverseTransformPoint(point),
                Age = 0f,
                HorizontalDrift = UnityEngine.Random.Range(-.09f, .09f),
                Scale = new Vector3(size, size * UnityEngine.Random.Range(.48f, .62f), 1f),
                Active = true
            };

            SpriteRenderer renderer = splatRenderers[index];
            if (renderer != null)
            {
                renderer.transform.position = point;
                renderer.transform.rotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(-28f, 28f));
                renderer.transform.localScale = slots[index].Scale;
                renderer.color = liquidColor;
                renderer.enabled = true;
            }

            activeEffectCount++;
            LastContactPoint = point;
            EmitDroplets(point, strength);
            animationController?.PlayAnnoyedReaction(target, point, strength, annoyanceDuration);
            LiquidEffectStarted?.Invoke(target, point, strength);
            return true;
        }

        public void ClearEffects()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (splatRenderers[i] != null) splatRenderers[i].enabled = false;
                slots[i] = default;
            }
            activeEffectCount = 0;
            completedEffectCount = 0;
            nextSlot = 0;
            if (dropletParticle != null)
                dropletParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void HandleImpact(SandboxTool2D tool, Rigidbody2D target, float speed, Vector2 point) =>
            TryPlay(tool, target, speed, point);

        private void HandleCharacterDied(Vector2 point) => ClearEffects();

        private bool IsAuthoredRagdollPart(Rigidbody2D target)
        {
            var parts = ragdoll.Parts;
            for (int i = 0; i < parts.Count; i++)
                if (parts[i] != null && parts[i].Body == target) return true;
            return false;
        }

        private void FinishSlot(int index, Rigidbody2D target)
        {
            if (!slots[index].Active) return;
            if (splatRenderers[index] != null) splatRenderers[index].enabled = false;
            slots[index] = default;
            activeEffectCount = Mathf.Max(0, activeEffectCount - 1);
            completedEffectCount++;
            LiquidEffectFinished?.Invoke(target);
        }

        private void EmitDroplets(Vector2 point, float strength)
        {
            if (dropletParticle == null) return;
            int count = 2 + Mathf.RoundToInt(strength * 3f);
            for (int i = 0; i < count; i++)
            {
                var emit = new ParticleSystem.EmitParams
                {
                    position = point + UnityEngine.Random.insideUnitCircle * .06f,
                    velocity = new Vector3(UnityEngine.Random.Range(-.18f, .18f), UnityEngine.Random.Range(-.75f, -.35f), 0f),
                    startSize = UnityEngine.Random.Range(.04f, .085f),
                    startColor = liquidColor,
                    rotation = UnityEngine.Random.Range(0f, Mathf.PI * 2f)
                };
                dropletParticle.Emit(emit, 1);
            }
        }

        private void OnValidate()
        {
            minimumImpactSpeed = Mathf.Max(0f, minimumImpactSpeed);
            effectDuration = Mathf.Max(.1f, effectDuration);
            slideDistance = Mathf.Max(0f, slideDistance);
            splatSizeRange.x = Mathf.Max(.02f, splatSizeRange.x);
            splatSizeRange.y = Mathf.Max(splatSizeRange.x, splatSizeRange.y);
            annoyanceDuration = Mathf.Max(.15f, annoyanceDuration);
        }
    }
}
