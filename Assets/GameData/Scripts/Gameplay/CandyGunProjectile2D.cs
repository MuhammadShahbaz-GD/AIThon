using System;
using KickTheBuddy.Physics;
using UnityEngine;

namespace KickTheBuddy.Gameplay
{
    /// <summary>A pre-authored, pool-owned candy projectile. It never instantiates or destroys itself.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D), typeof(RagdollAttackManager2D))]
    public sealed class CandyGunProjectile2D : MonoBehaviour
    {
        private const float MinimumCandyLimbImpulse = 24f;
        private const float MinimumCandyBodyImpulse = 14f;

        [SerializeField] private Rigidbody2D body;
        [SerializeField] private Collider2D projectileCollider;
        [SerializeField] private RagdollAttackManager2D attack;
        [Min(.25f)] [SerializeField] private float lifetime = 4f;

        [Header("Candy Hit Reaction")]
        [Tooltip("Extra point impulse on the exact limb hit, producing a sharp springy snap.")]
        [Min(0f)] [SerializeField] private float limbHitImpulse = MinimumCandyLimbImpulse;
        [Tooltip("Additional impulse on the torso so each candy visibly pushes the whole ragdoll away.")]
        [Min(0f)] [SerializeField] private float bodyPushImpulse = MinimumCandyBodyImpulse;
        [Tooltip("Adds a small upward component so repeated shots feel lively instead of sliding horizontally.")]
        [Range(0f, .5f)] [SerializeField] private float upwardLift = .22f;

        private float remainingLifetime;
        private bool active;

        public bool IsAvailable => !active;
        public Rigidbody2D Body => body;
        public RagdollAttackManager2D Attack => attack;
        public event Action<CandyGunProjectile2D, Rigidbody2D, Vector2, float> Hit;
        public event Action<CandyGunProjectile2D> Expired;

        private void Awake()
        {
            if (body == null) body = GetComponent<Rigidbody2D>();
            if (projectileCollider == null) projectileCollider = GetComponent<Collider2D>();
            if (attack == null) attack = GetComponent<RagdollAttackManager2D>();
        }

        private void FixedUpdate()
        {
            if (!active) return;
            remainingLifetime -= Time.fixedDeltaTime;
            if (remainingLifetime > 0f) return;
            Expired?.Invoke(this);
            Deactivate();
        }

        public void Launch(Vector2 position, float rotationDegrees, Vector2 velocity)
        {
            if (body == null || projectileCollider == null) return;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, rotationDegrees));
            gameObject.SetActive(true);
            active = true;
            remainingLifetime = lifetime;
            attack?.ResetAttack();
            body.simulated = true;
            projectileCollider.enabled = true;
            body.velocity = velocity;
            body.angularVelocity = Mathf.Sign(velocity.x) * -240f;
            body.WakeUp();
        }

        public void Deactivate()
        {
            active = false;
            remainingLifetime = 0f;
            if (body != null)
            {
                body.velocity = Vector2.zero;
                body.angularVelocity = 0f;
                body.simulated = false;
            }
            if (projectileCollider != null) projectileCollider.enabled = false;
            gameObject.SetActive(false);
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (!active) return;
            Rigidbody2D hitBody = collision.collider != null ? collision.collider.attachedRigidbody : null;
            Vector2 point = collision.contactCount > 0 ? collision.GetContact(0).point : body.worldCenterOfMass;
            float speed = collision.relativeVelocity.magnitude;
            RagdollPartHealth hitHealth = hitBody != null ? hitBody.GetComponent<RagdollPartHealth>() : null;
            if (hitHealth != null)
                ApplyCandyHitImpulse(hitBody, point);
            Hit?.Invoke(this, hitBody, point, speed);
            if (hitHealth != null) Deactivate();
        }

        private void ApplyCandyHitImpulse(Rigidbody2D hitBody, Vector2 point)
        {
            Vector2 direction = body != null && body.velocity.sqrMagnitude > .0001f
                ? body.velocity.normalized
                : (hitBody.worldCenterOfMass - point).normalized;
            direction = (direction + Vector2.up * upwardLift).normalized;

            float resolvedLimbImpulse = Mathf.Max(MinimumCandyLimbImpulse, limbHitImpulse);
            hitBody.AddForceAtPosition(direction * resolvedLimbImpulse, point, ForceMode2D.Impulse);

            RagdollController ragdoll = hitBody.GetComponentInParent<RagdollController>();
            if (ragdoll == null) return;
            float resolvedBodyImpulse = Mathf.Max(MinimumCandyBodyImpulse, bodyPushImpulse);
            var parts = ragdoll.Parts;
            for (int i = 0; i < parts.Count; i++)
            {
                RagdollController.RagdollPart part = parts[i];
                if (part == null || part.PartType != RagdollPartType.Torso || part.Body == null) continue;
                part.Body.AddForce(direction * resolvedBodyImpulse, ForceMode2D.Impulse);
                break;
            }
        }

        private void OnDisable()
        {
            active = false;
            remainingLifetime = 0f;
        }

        private void OnValidate()
        {
            lifetime = Mathf.Max(.25f, lifetime);
            limbHitImpulse = Mathf.Max(0f, limbHitImpulse);
            bodyPushImpulse = Mathf.Max(0f, bodyPushImpulse);
            upwardLift = Mathf.Clamp(upwardLift, 0f, .5f);
        }
    }
}
