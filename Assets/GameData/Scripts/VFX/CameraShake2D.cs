using System;
using KickTheBuddy.Physics;
using UnityEngine;

namespace KickTheBuddy.VFX
{
    /// <summary>Allocation-free camera trauma driven by resolved ragdoll damage and death events.</summary>
    [DisallowMultipleComponent]
    public sealed class CameraShake2D : MonoBehaviour
    {
        [Header("Event Source")]
        [SerializeField] private RagdollController controller;

        [Header("Damage Shake")]
        [Min(0f)] [SerializeField] private float minimumImpactSpeed = 4f;
        [Min(.01f)] [SerializeField] private float speedForMaximumShake = 18f;
        [Min(0f)] [SerializeField] private float damageAmplitude = .08f;
        [Min(0f)] [SerializeField] private float damageDuration = .14f;
        [Min(1f)] [SerializeField] private float damageFrequency = 30f;

        [Header("Death Shake")]
        [Min(0f)] [SerializeField] private float deathAmplitude = .24f;
        [Min(0f)] [SerializeField] private float deathDuration = .55f;
        [Min(1f)] [SerializeField] private float deathFrequency = 34f;

        private Vector3 appliedOffset;
        private float shakeStartedAt;
        private float shakeEndsAt;
        private float activeAmplitude;
        private float activeFrequency;

        public event Action<float, float> ShakeStarted;

        public float DeathAmplitude => deathAmplitude;
        public float DeathDuration => deathDuration;

        private void OnEnable()
        {
            if (controller == null) return;
            controller.OnImpactResolved += HandleImpact;
            controller.OnCharacterDied += HandleDeath;
        }

        private void OnDisable()
        {
            if (controller != null)
            {
                controller.OnImpactResolved -= HandleImpact;
                controller.OnCharacterDied -= HandleDeath;
            }
            RemoveAppliedOffset();
            shakeEndsAt = 0f;
        }

        public void HandleImpact(float damage, float impactSpeed, Vector2 point)
        {
            if (impactSpeed < minimumImpactSpeed) return;
            float strength = Mathf.InverseLerp(minimumImpactSpeed, speedForMaximumShake, impactSpeed);
            StartShake(damageAmplitude * strength, damageDuration, damageFrequency);
        }

        private void HandleDeath(Vector2 point) => StartShake(deathAmplitude, deathDuration, deathFrequency);

        public void StartShake(float amplitude, float duration, float frequency)
        {
            if (amplitude <= 0f || duration <= 0f) return;
            float now = Time.unscaledTime;
            activeAmplitude = Mathf.Max(activeAmplitude, amplitude);
            activeFrequency = Mathf.Max(activeFrequency, frequency);
            shakeStartedAt = now;
            shakeEndsAt = Mathf.Max(shakeEndsAt, now + duration);
            ShakeStarted?.Invoke(amplitude, duration);
        }

        private void LateUpdate()
        {
            // Remove the previous frame's offset first so authored camera movement remains intact.
            RemoveAppliedOffset();
            float now = Time.unscaledTime;
            if (now >= shakeEndsAt)
            {
                activeAmplitude = 0f;
                activeFrequency = 0f;
                return;
            }

            float duration = Mathf.Max(.001f, shakeEndsAt - shakeStartedAt);
            float normalizedTime = Mathf.Clamp01((now - shakeStartedAt) / duration);
            float falloff = 1f - normalizedTime;
            falloff *= falloff;
            float phase = now * activeFrequency;
            appliedOffset = new Vector3(
                Mathf.Sin(phase * 1.17f),
                Mathf.Sin(phase * 1.73f + 1.41f),
                0f) * (activeAmplitude * falloff);
            transform.localPosition += appliedOffset;
        }

        private void RemoveAppliedOffset()
        {
            if (appliedOffset == Vector3.zero) return;
            transform.localPosition -= appliedOffset;
            appliedOffset = Vector3.zero;
        }

        private void OnValidate()
        {
            minimumImpactSpeed = Mathf.Max(0f, minimumImpactSpeed);
            speedForMaximumShake = Mathf.Max(minimumImpactSpeed + .01f, speedForMaximumShake);
            damageAmplitude = Mathf.Max(0f, damageAmplitude);
            damageDuration = Mathf.Max(0f, damageDuration);
            damageFrequency = Mathf.Max(1f, damageFrequency);
            deathAmplitude = Mathf.Max(0f, deathAmplitude);
            deathDuration = Mathf.Max(0f, deathDuration);
            deathFrequency = Mathf.Max(1f, deathFrequency);
        }
    }
}
