using System;
using UnityEngine;

namespace KickTheBuddy.Physics
{
    public enum RagdollAttackType
    {
        Wall,
        Bullet,
        Hammer,
        Needle,
        Explosion,
        Custom,
        Lollipop,
        Jelly,
        CandyStick,
        ChocolateBar,
        GummyBear,
        CandyJar,
        CandyProjectile
    }

    /// <summary>
    /// Attach to any collider that is allowed to damage the ragdoll.
    /// It describes the attack; RagdollDamageManager performs the calculation and applies damage.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider2D))]
    public sealed class RagdollAttackManager2D : MonoBehaviour
    {
        [SerializeField] private RagdollAttackType attackType = RagdollAttackType.Custom;
        [Tooltip("Damage applied once the minimum impact speed is reached.")]
        [Min(0f)] [SerializeField] private float baseDamage;
        [Tooltip("Additional damage for each speed unit above Minimum Impact Speed.")]
        [Min(0f)] [SerializeField] private float damagePerSpeed = 2.5f;
        [Min(0f)] [SerializeField] private float minimumImpactSpeed = 3.5f;
        [Min(0f)] [SerializeField] private float maximumDamage = 35f;
        [Tooltip("Absolute motion floor for a legitimate strike. Prevents resting, solver jitter, and attached contacts from dealing fixed damage.")]
        [Min(.1f)] [SerializeField] private float minimumAttackMotionSpeed = 1f;
        [SerializeField] private LayerMask damageableLayers = ~0;
        [SerializeField] private bool enabledForDamage = true;
        [SerializeField] private bool disableAfterSuccessfulHit;

        public event Action<RagdollAttackManager2D, Rigidbody2D, float, float, Vector2> DamageDealt;

        private Rigidbody2D attackBody;
        private Joint2D[] attackJoints = Array.Empty<Joint2D>();

        public RagdollAttackType AttackType => attackType;
        public bool DamageEnabled => enabledForDamage;

        private void Awake()
        {
            attackBody = GetComponentInParent<Rigidbody2D>();
            if (attackBody != null) attackJoints = attackBody.GetComponents<Joint2D>();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            Rigidbody2D hitBody = other.attachedRigidbody;
            if (hitBody == null) return;

            RagdollDamageManager damageManager = hitBody.GetComponentInParent<RagdollDamageManager>();
            if (damageManager == null) return;

            Vector2 attackVelocity = attackBody != null ? attackBody.velocity : Vector2.zero;
            Vector2 relativeVelocity = attackVelocity - hitBody.velocity;
            if (!CanDamageImpact(damageManager.gameObject, hitBody, relativeVelocity.magnitude)) return;
            damageManager.ApplyAttack(
                hitBody,
                this,
                relativeVelocity.magnitude,
                relativeVelocity,
                other.ClosestPoint(transform.position));
        }

        public bool CanDamage(GameObject target)
        {
            return attackType != RagdollAttackType.Jelly &&
                   enabledForDamage &&
                   target != null &&
                   (damageableLayers.value & (1 << target.layer)) != 0;
        }

        /// <summary>Validates that a contact represents a moving attack rather than resting or attached contact.</summary>
        public bool CanDamageImpact(GameObject target, Rigidbody2D hitBody, float relativeSpeed)
        {
            if (!CanDamage(target) || relativeSpeed < Mathf.Max(.1f, minimumAttackMotionSpeed)) return false;
            if (hitBody == null || attackJoints == null) return true;

            RagdollController targetRagdoll = hitBody.GetComponentInParent<RagdollController>();
            if (targetRagdoll == null) return true;
            for (int i = 0; i < attackJoints.Length; i++)
            {
                Joint2D joint = attackJoints[i];
                if (joint == null || !joint.enabled || joint.connectedBody == null) continue;
                if (joint.connectedBody.GetComponentInParent<RagdollController>() == targetRagdoll)
                    return false;
            }
            return true;
        }

        /// <summary>Speed curve shared by walls, bullets, hammers, needles, and custom hazards.</summary>
        public float CalculateDamage(float relativeSpeed)
        {
            // Jelly is a presentation-only nuisance. Keeping this invariant here prevents an
            // accidental Inspector value from ever feeding health, score, combo, cracks, or KO.
            if (attackType == RagdollAttackType.Jelly) return 0f;
            float excessSpeed = Mathf.Max(0f, relativeSpeed - minimumImpactSpeed);
            if (excessSpeed <= 0f && baseDamage <= 0f) return 0f;
            return Mathf.Min(baseDamage + excessSpeed * damagePerSpeed, maximumDamage);
        }

        internal void NotifyDamageDealt(Rigidbody2D body, float damage, float speed, Vector2 point)
        {
            DamageDealt?.Invoke(this, body, damage, speed, point);
            if (disableAfterSuccessfulHit) enabledForDamage = false;
        }

        /// <summary>Convenient runtime configuration for spawned bullets or tools.</summary>
        public void Configure(
            RagdollAttackType type,
            float fixedDamage,
            float speedDamage,
            float minimumSpeed,
            float damageCap)
        {
            attackType = type;
            if (type == RagdollAttackType.Jelly)
            {
                baseDamage = 0f;
                damagePerSpeed = 0f;
                minimumImpactSpeed = 0f;
                maximumDamage = 0f;
                enabledForDamage = true;
                return;
            }
            baseDamage = Mathf.Max(0f, fixedDamage);
            damagePerSpeed = Mathf.Max(0f, speedDamage);
            minimumImpactSpeed = Mathf.Max(0f, minimumSpeed);
            maximumDamage = Mathf.Max(baseDamage, damageCap);
            enabledForDamage = true;
        }

        public void ResetAttack()
        {
            enabledForDamage = true;
        }

        /// <summary>Explicitly enables or disables this authored attack surface.</summary>
        public void SetDamageEnabled(bool value)
        {
            enabledForDamage = value && attackType != RagdollAttackType.Jelly;
        }

        private void OnValidate()
        {
            if (attackType == RagdollAttackType.Jelly)
            {
                baseDamage = 0f;
                damagePerSpeed = 0f;
                minimumImpactSpeed = 0f;
                maximumDamage = 0f;
                return;
            }
            baseDamage = Mathf.Max(0f, baseDamage);
            damagePerSpeed = Mathf.Max(0f, damagePerSpeed);
            minimumImpactSpeed = Mathf.Max(0f, minimumImpactSpeed);
            maximumDamage = Mathf.Max(baseDamage, maximumDamage);
            minimumAttackMotionSpeed = Mathf.Max(.1f, minimumAttackMotionSpeed);
        }
    }
}
