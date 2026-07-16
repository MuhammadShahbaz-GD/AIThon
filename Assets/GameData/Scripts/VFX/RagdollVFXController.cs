using System;
using KickTheBuddy.Physics;
using UnityEngine;

namespace KickTheBuddy.VFX
{
    /// <summary>
    /// Displays all ragdoll feedback through one reusable world-space ParticleSystem.
    /// A normal collision emits exactly once at the authoritative contact point.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RagdollController))]
    public sealed class RagdollVFXController : MonoBehaviour
    {
        [Header("Single Particle")]
        [SerializeField] private bool effectsEnabled = true;
        [Tooltip("Only Hit Prefab is used. One instance is created and reused for every impact.")]
        [SerializeField] private RagdollVFXProfile profile;
        [SerializeField] private Color lightHitColor = new Color(1f, .85f, .25f);
        [SerializeField] private Color heavyHitColor = new Color(1f, .18f, .12f);
        [SerializeField] private Color deathColor = new Color(1f, .28f, .08f);
        [Min(.001f)] [SerializeField] private float minimumParticleSize = .045f;
        [Min(.001f)] [SerializeField] private float maximumParticleSize = .11f;
        [Min(.01f)] [SerializeField] private float speedForMaximumStrength = 18f;

        private RagdollController controller;
        private ParticleSystem sharedParticle;
        private Color profileColor = Color.white;

        public event Action<Vector2, float> ImpactEffectPlayed;
        public event Action<Vector2> DeathEffectPlayed;

        private void Awake()
        {
            controller = GetComponent<RagdollController>();
            CreateSharedParticle();
        }

        private void OnEnable()
        {
            if (controller == null) return;
            controller.OnImpactResolved += HandleImpact;
            controller.OnProfileDamageEffect += HandleProfileColor;
            controller.OnCharacterKO += HandleDeath;
            controller.OnCharacterRevived += StopEffect;
        }

        private void OnDisable()
        {
            if (controller != null)
            {
                controller.OnImpactResolved -= HandleImpact;
                controller.OnProfileDamageEffect -= HandleProfileColor;
                controller.OnCharacterKO -= HandleDeath;
                controller.OnCharacterRevived -= StopEffect;
            }

            StopEffect();
        }

        private void CreateSharedParticle()
        {
            if (profile == null || profile.HitPrefab == null) return;

            sharedParticle = Instantiate(profile.HitPrefab, transform);
            sharedParticle.gameObject.name = "Shared Hit Particle";
            ParticleSystem.MainModule main = sharedParticle.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            sharedParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void HandleImpact(float damage, float impactSpeed, Vector2 point)
        {
            if (!effectsEnabled || sharedParticle == null) return;

            float strength = Mathf.Clamp01(impactSpeed / speedForMaximumStrength);
            Color speedColor = Color.Lerp(lightHitColor, heavyHitColor, strength);
            Color finalColor = Color.Lerp(speedColor, profileColor, .25f);
            float size = Mathf.Lerp(minimumParticleSize, maximumParticleSize, strength);

            EmitOne(point, finalColor, size);
            ImpactEffectPlayed?.Invoke(point, strength);
        }

        private void HandleProfileColor(RagdollProfileType type, Color color, Vector2 point)
        {
            profileColor = color;
        }

        private void HandleDeath()
        {
            // Temporary knockouts use the same KO event, but only zero health is true death.
            if (!effectsEnabled || sharedParticle == null || controller.CurrentHealth > 0f) return;

            Vector2 point = transform.position;
            if (controller.Parts.Count > 0 && controller.Parts[0].Body != null)
                point = controller.Parts[0].Body.worldCenterOfMass;

            EmitOne(point, Color.Lerp(deathColor, profileColor, .25f), maximumParticleSize);
            DeathEffectPlayed?.Invoke(point);
        }

        private void EmitOne(Vector2 point, Color color, float size)
        {
            ParticleSystem.EmitParams parameters = new ParticleSystem.EmitParams
            {
                position = point,
                startColor = color,
                startSize = size
            };

            // A single shared system and a count of one prevent per-limb duplicate bursts.
            sharedParticle.Emit(parameters, 1);
        }

        private void StopEffect()
        {
            if (sharedParticle != null)
                sharedParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void OnValidate()
        {
            minimumParticleSize = Mathf.Max(.001f, minimumParticleSize);
            maximumParticleSize = Mathf.Max(minimumParticleSize, maximumParticleSize);
            speedForMaximumStrength = Mathf.Max(.01f, speedForMaximumStrength);
        }
    }
}
