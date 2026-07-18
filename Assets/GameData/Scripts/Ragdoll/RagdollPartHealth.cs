using System;
using UnityEngine;

namespace KickTheBuddy.Physics
{
    public enum RagdollPartType
    {
        Torso,
        Head,
        Arm,
        Leg,
        Other
    }

    /// <summary>Owns health and tuning for one physical ragdoll body part.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class RagdollPartHealth : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private RagdollPartType partType = RagdollPartType.Other;
        [Tooltip("A depleted critical part immediately kills the complete ragdoll.")]
        [SerializeField] private bool criticalPart;

        [Header("Health")]
        [Min(1f)] [SerializeField] private float maximumHealth = 50f;
        [Tooltip("Contribution of this part to combined character health.")]
        [Min(0f)] [SerializeField] private float healthContribution = 1f;
        [Tooltip("Incoming damage multiplier for this body part.")]
        [Min(0f)] [SerializeField] private float damageRatio = 1f;

        [Header("Physical Personality")]
        [Tooltip("Scales TargetJoint drag softness for this part. Lower is softer.")]
        [Range(.25f, 2f)] [SerializeField] private float flexibility = 1f;
        [Tooltip("Scales visual/animation reaction intensity when this part is damaged.")]
        [Range(0f, 2f)] [SerializeField] private float damageReactionStrength = 1f;

        private float currentHealth;
        private float authoredMaximumHealth;
        [SerializeField] private DismemberableLimb structuralLimb;

        public event Action<RagdollPartHealth, float, Vector2> Damaged;
        public event Action<RagdollPartHealth> Depleted;
        public event Action<RagdollPartHealth> Restored;

        public RagdollPartType PartType => partType;
        public float CurrentHealth => currentHealth;
        public float MaximumHealth => maximumHealth;
        public float HealthContribution => healthContribution;
        public float DamageRatio => damageRatio;
        public float Flexibility => flexibility;
        public float DamageReactionStrength => damageReactionStrength;
        public bool IsCritical => criticalPart;
        public bool IsDepleted => currentHealth <= 0f;
        public float NormalizedHealth => maximumHealth > 0f ? currentHealth / maximumHealth : 0f;
        public float WeightedCurrentHealth => currentHealth * healthContribution;
        public float WeightedMaximumHealth => maximumHealth * healthContribution;

        private void Awake()
        {
            authoredMaximumHealth = Mathf.Max(1f, maximumHealth);
            maximumHealth = authoredMaximumHealth;
            currentHealth = maximumHealth;
        }

        internal void Initialize(float fallbackHealth, DismemberableLimb authoredStructuralLimb)
        {
            structuralLimb = authoredStructuralLimb;
            if (maximumHealth <= 0f) maximumHealth = Mathf.Max(1f, fallbackHealth);
            if (authoredMaximumHealth <= 0f) authoredMaximumHealth = maximumHealth;
            currentHealth = maximumHealth;
        }

        internal void ApplyDurabilityMultiplier(float multiplier)
        {
            if (authoredMaximumHealth <= 0f)
                authoredMaximumHealth = Mathf.Max(1f, maximumHealth);
            maximumHealth = authoredMaximumHealth * Mathf.Max(.1f, multiplier);
            currentHealth = maximumHealth;
        }

        internal float GetMaximumDamagePerHit(int requiredHits)
        {
            float limitingCapacity = maximumHealth;
            if (structuralLimb != null && structuralLimb.CanBeSevered)
                limitingCapacity = Mathf.Min(limitingCapacity, structuralLimb.MaximumJointHealth);
            return limitingCapacity / Mathf.Max(1, requiredHits);
        }

        public float TakeDamage(float rawDamage, Vector2 force, Vector2 point)
        {
            if (rawDamage <= 0f || currentHealth <= 0f) return 0f;

            float appliedDamage = Mathf.Max(0f, rawDamage * damageRatio);
            if (appliedDamage <= 0f) return 0f;

            float previousHealth = currentHealth;
            currentHealth = Mathf.Max(0f, currentHealth - appliedDamage);
            float consumedDamage = previousHealth - currentHealth;

            structuralLimb?.TakeDamage(consumedDamage, force, point);
            Damaged?.Invoke(this, consumedDamage, point);
            if (currentHealth <= 0f) Depleted?.Invoke(this);
            return consumedDamage;
        }

        internal void ForceDeplete(Vector2 point)
        {
            if (currentHealth <= 0f) return;
            float remaining = currentHealth;
            currentHealth = 0f;
            Damaged?.Invoke(this, remaining, point);
            Depleted?.Invoke(this);
        }

        public void Restore()
        {
            currentHealth = maximumHealth;
            Restored?.Invoke(this);
        }

        /// <summary>Editor/runtime authoring helper for generated ragdolls.</summary>
        public void Configure(
            RagdollPartType type,
            float health,
            float contribution,
            float incomingDamageRatio,
            float partFlexibility,
            float reactionStrength,
            bool isCritical)
        {
            partType = type;
            maximumHealth = Mathf.Max(1f, health);
            authoredMaximumHealth = maximumHealth;
            healthContribution = Mathf.Max(0f, contribution);
            damageRatio = Mathf.Max(0f, incomingDamageRatio);
            flexibility = Mathf.Clamp(partFlexibility, .25f, 2f);
            damageReactionStrength = Mathf.Clamp(reactionStrength, 0f, 2f);
            criticalPart = isCritical;
            currentHealth = maximumHealth;
        }

        private void OnValidate()
        {
            maximumHealth = Mathf.Max(1f, maximumHealth);
            healthContribution = Mathf.Max(0f, healthContribution);
            damageRatio = Mathf.Max(0f, damageRatio);
            flexibility = Mathf.Clamp(flexibility, .25f, 2f);
            damageReactionStrength = Mathf.Clamp(damageReactionStrength, 0f, 2f);
            if (!Application.isPlaying) currentHealth = maximumHealth;
        }
    }
}
