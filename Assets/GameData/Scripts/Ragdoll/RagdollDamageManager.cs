using System;
using System.Collections.Generic;
using UnityEngine;

namespace KickTheBuddy.Physics
{
    /// <summary>Sole authority for damage against the explicitly authored main-part table.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RagdollController))]
    public sealed class RagdollDamageManager : MonoBehaviour, IRagdollCollisionReceiver
    {
        [Header("Damage Tuning")]
        [Tooltip("Character-wide damage scale applied after an attack calculates its speed-based raw damage.")]
        [Range(.05f, 1f)] [SerializeField] private float incomingDamageMultiplier = .65f;

        [Header("Safety")]
        [Min(0f)] [SerializeField] private float repeatHitCooldown = .08f;

        [Header("Authored References")]
        [SerializeField] private RagdollController controller;
        [SerializeField] private RagdollElementalEffects elementalEffects;

        private RagdollController.RagdollPart[] parts = Array.Empty<RagdollController.RagdollPart>();
        private Rigidbody2D lastHitBody;
        private int lastAttackId;
        private float lastHitTime = float.NegativeInfinity;

        public event Action<Rigidbody2D, RagdollPartHealth, RagdollAttackManager2D, float, float, Vector2> DamageCalculated;
        public event Action<float, float> AggregateHealthChanged;
        public event Action<RagdollPartHealth> CriticalPartDepleted;

        public float CurrentHealth { get; private set; }
        public float MaximumHealth { get; private set; }
        public float IncomingDamageMultiplier => incomingDamageMultiplier;

        internal void Initialize(RagdollController owner, RagdollElementalEffects elements)
        {
            controller = owner;
            elementalEffects = elements;
        }

        internal void ConfigureParts(IReadOnlyList<RagdollController.RagdollPart> authoredParts)
        {
            int count = authoredParts != null ? authoredParts.Count : 0;
            if (parts.Length != count) parts = new RagdollController.RagdollPart[count];
            for (int i = 0; i < count; i++) parts[i] = authoredParts[i];
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

        public bool ApplyAttack(Rigidbody2D hitBody, RagdollAttackManager2D attack,
            float relativeSpeed, Vector2 force, Vector2 point)
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

        public bool ApplyDirectDamage(Rigidbody2D hitBody, float rawDamage,
            float strength, Vector2 force, Vector2 point)
        {
            if (controller == null || hitBody == null || rawDamage <= 0f || controller.CurrentHealth <= 0f) return false;
            return ApplyResolvedDamage(hitBody, rawDamage, Mathf.Max(0f, strength), force, point, null);
        }

        internal void NotifyPartBroken(Rigidbody2D brokenBody, Vector2 point)
        {
            int index = FindPartIndex(brokenBody);
            if (index < 0) return;
            RagdollPartHealth part = parts[index].Health;
            if (part == null) return;

            // A physically detached part is depleted even when it broke from structural distance rather
            // than impact damage. This keeps combined health, crack presentation, and the visible body state aligned.
            part.ForceDeplete(point);
            RecalculateAggregateHealth(0f, 0f, point, part.IsCritical);
            if (part.IsCritical) CriticalPartDepleted?.Invoke(part);
        }

        internal void RestoreAllParts()
        {
            for (int i = 0; i < parts.Length; i++)
                if (parts[i]?.Health != null) parts[i].Health.Restore();
            RecalculateAggregateHealth(0f, 0f, transform.position, false);
        }

        private bool ApplyResolvedDamage(Rigidbody2D hitBody, float rawDamage, float impactSpeed,
            Vector2 force, Vector2 point, RagdollAttackManager2D attack)
        {
            int index = FindPartIndex(hitBody);
            if (index < 0) return false;
            RagdollController.RagdollPart configuredPart = parts[index];
            RagdollPartHealth health = configuredPart.Health;
            // Attacks remain responsible for speed/material tuning. This single character-wide scale
            // extends play time without duplicating balance multipliers across walls, tools and bullets.
            float appliedDamage = health.TakeDamage(rawDamage * incomingDamageMultiplier, force, point);
            if (appliedDamage <= 0f) return false;

            elementalEffects?.NotifyImpact(configuredPart.DismemberableLimb, impactSpeed, point);
            bool criticalDeath = health.IsCritical && health.IsDepleted;
            RecalculateAggregateHealth(appliedDamage, impactSpeed, point, criticalDeath);
            DamageCalculated?.Invoke(hitBody, health, attack, appliedDamage, impactSpeed, point);
            if (criticalDeath) CriticalPartDepleted?.Invoke(health);
            return true;
        }

        private int FindPartIndex(Rigidbody2D body)
        {
            for (int i = 0; i < parts.Length; i++)
                if (parts[i] != null && parts[i].Body == body) return i;
            return -1;
        }

        private void RecalculateAggregateHealth(float appliedDamage, float impactSpeed, Vector2 point, bool forceDeath)
        {
            float current = 0f;
            float maximum = 0f;
            for (int i = 0; i < parts.Length; i++)
            {
                RagdollPartHealth part = parts[i]?.Health;
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
            incomingDamageMultiplier = Mathf.Clamp(incomingDamageMultiplier, .05f, 1f);
            repeatHitCooldown = Mathf.Max(0f, repeatHitCooldown);
        }
    }
}
