using System;
using UnityEngine;

namespace KickTheBuddy.Physics
{
    /// <summary>
    /// Sole authority for validating attacks, damaging the exact hit part, and aggregating character health.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RagdollController))]
    public sealed class RagdollDamageManager : MonoBehaviour, IRagdollCollisionReceiver
    {
        [Header("Safety")]
        [Tooltip("Prevents solver contact jitter from repeatedly damaging the same limb.")]
        [Min(0f)] [SerializeField] private float repeatHitCooldown = .08f;

        private RagdollController controller;
        private RagdollElementalEffects elementalEffects;
        private RagdollPartHealth[] parts = Array.Empty<RagdollPartHealth>();
        private Rigidbody2D lastHitBody;
        private int lastAttackId;
        private float lastHitTime = float.NegativeInfinity;

        public event Action<Rigidbody2D, RagdollPartHealth, RagdollAttackManager2D, float, float, Vector2> DamageCalculated;
        public event Action<float, float> AggregateHealthChanged;
        public event Action<RagdollPartHealth> CriticalPartDepleted;

        public float CurrentHealth { get; private set; }
        public float MaximumHealth { get; private set; }

        internal void Initialize(RagdollController owner)
        {
            controller = owner;
            elementalEffects = owner != null ? owner.GetComponent<RagdollElementalEffects>() : null;
        }

        private void Awake()
        {
            if (controller == null) controller = GetComponent<RagdollController>();
            if (elementalEffects == null) elementalEffects = GetComponent<RagdollElementalEffects>();
        }

        internal void RefreshParts()
        {
            parts = GetComponentsInChildren<RagdollPartHealth>(true);
            RecalculateAggregateHealth(0f, 0f, transform.position, false);
        }

        void IRagdollCollisionReceiver.ReportCollision(Rigidbody2D hitBody, Collision2D collision)
        {
            if (controller == null || hitBody == null || collision == null) return;
            RagdollAttackManager2D attack = collision.collider.GetComponentInParent<RagdollAttackManager2D>();
            if (attack == null || !attack.CanDamage(controller.gameObject)) return;

            float speed = collision.relativeVelocity.magnitude;
            Vector2 point = collision.contactCount > 0 ? collision.GetContact(0).point : hitBody.worldCenterOfMass;
            ApplyAttack(hitBody, attack, speed, collision.relativeVelocity, point);
        }

        public bool ApplyAttack(
            Rigidbody2D hitBody,
            RagdollAttackManager2D attack,
            float relativeSpeed,
            Vector2 force,
            Vector2 point)
        {
            if (controller == null || hitBody == null || attack == null) return false;
            if (controller.CurrentHealth <= 0f || controller.CurrentState == RagdollState.Frozen) return false;
            if (!attack.CanDamage(controller.gameObject) || IsRepeatedHit(hitBody, attack)) return false;

            float rawDamage = attack.CalculateDamage(relativeSpeed);
            if (rawDamage <= 0f) return false;

            RememberHit(hitBody, attack);
            bool applied = ApplyResolvedDamage(hitBody, rawDamage, relativeSpeed, force, point, attack);
            if (applied) attack.NotifyDamageDealt(hitBody, rawDamage, relativeSpeed, point);
            return applied;
        }

        public bool ApplyDirectDamage(
            Rigidbody2D hitBody,
            float rawDamage,
            float strength,
            Vector2 force,
            Vector2 point)
        {
            if (controller == null || hitBody == null || rawDamage <= 0f || controller.CurrentHealth <= 0f) return false;
            return ApplyResolvedDamage(hitBody, rawDamage, Mathf.Max(0f, strength), force, point, null);
        }

        internal void NotifyPartBroken(Rigidbody2D brokenBody, Vector2 point)
        {
            if (brokenBody == null) return;
            RagdollPartHealth part = brokenBody.GetComponent<RagdollPartHealth>();
            if (part == null || !part.IsCritical) return;
            part.ForceDeplete(point);
            RecalculateAggregateHealth(0f, 0f, point, true);
            CriticalPartDepleted?.Invoke(part);
        }

        internal void RestoreAllParts()
        {
            if (parts == null || parts.Length == 0) parts = GetComponentsInChildren<RagdollPartHealth>(true);
            for (int i = 0; i < parts.Length; i++)
                if (parts[i] != null) parts[i].Restore();
            RecalculateAggregateHealth(0f, 0f, transform.position, false);
        }

        private bool ApplyResolvedDamage(
            Rigidbody2D hitBody,
            float rawDamage,
            float impactSpeed,
            Vector2 force,
            Vector2 point,
            RagdollAttackManager2D attack)
        {
            RagdollPartHealth part = hitBody.GetComponent<RagdollPartHealth>();
            if (part == null) return false;

            float appliedDamage = part.TakeDamage(rawDamage, force, point);
            if (appliedDamage <= 0f) return false;

            DismemberableLimb structuralLimb = hitBody.GetComponent<DismemberableLimb>();
            if (elementalEffects == null) elementalEffects = GetComponent<RagdollElementalEffects>();
            elementalEffects?.NotifyImpact(structuralLimb, impactSpeed, point);

            bool criticalDeath = part.IsCritical && part.IsDepleted;
            RecalculateAggregateHealth(appliedDamage, impactSpeed, point, criticalDeath);
            DamageCalculated?.Invoke(hitBody, part, attack, appliedDamage, impactSpeed, point);
            if (criticalDeath) CriticalPartDepleted?.Invoke(part);
            return true;
        }

        private void RecalculateAggregateHealth(float appliedDamage, float impactSpeed, Vector2 point, bool forceDeath)
        {
            float current = 0f;
            float maximum = 0f;
            for (int i = 0; i < parts.Length; i++)
            {
                RagdollPartHealth part = parts[i];
                if (part == null) continue;
                current += part.WeightedCurrentHealth;
                maximum += part.WeightedMaximumHealth;
                if (part.IsCritical && part.IsDepleted) forceDeath = true;
            }

            MaximumHealth = Mathf.Max(1f, maximum);
            CurrentHealth = forceDeath ? 0f : Mathf.Clamp(current, 0f, MaximumHealth);
            controller?.SynchronizeAggregateHealth(CurrentHealth, MaximumHealth, appliedDamage, impactSpeed, point);
            AggregateHealthChanged?.Invoke(CurrentHealth, MaximumHealth);
        }

        private bool IsRepeatedHit(Rigidbody2D body, RagdollAttackManager2D attack)
        {
            return repeatHitCooldown > 0f && lastHitBody == body &&
                   lastAttackId == attack.GetInstanceID() &&
                   Time.time - lastHitTime < repeatHitCooldown;
        }

        private void RememberHit(Rigidbody2D body, RagdollAttackManager2D attack)
        {
            lastHitBody = body;
            lastAttackId = attack.GetInstanceID();
            lastHitTime = Time.time;
        }

        private void OnValidate()
        {
            repeatHitCooldown = Mathf.Max(0f, repeatHitCooldown);
        }
    }
}
