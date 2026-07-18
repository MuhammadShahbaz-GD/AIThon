using System;
using KickTheBuddy.Physics;
using KickTheBuddy.VFX;
using UnityEngine;

namespace KickTheBuddy.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D), typeof(RagdollAttackManager2D))]
    public sealed class LevelFourPipeProjectile2D : MonoBehaviour
    {
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private Collider2D hitCollider;
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private TrailRenderer trail;
        [SerializeField] private ParticleSystem motionTrail;
        [SerializeField] private RagdollAttackManager2D attack;

        private Action<LevelFourPipeProjectile2D> recycle;
        private bool bomb;
        private float blastDamage;
        private float hitImpulse;
        private float wholeBodyVelocity;
        private bool activeShot;
        private LevelFourPipeVFXController2D impactVfx;

        public Rigidbody2D Body => body;
        public Collider2D HitCollider => hitCollider;
        public SpriteRenderer SpriteRenderer => spriteRenderer;
        public RagdollAttackManager2D Attack => attack;
        public bool ActiveShot => activeShot;
        public bool HasMotionVFX => trail != null && motionTrail != null;
        public float RemainingLifetime { get; private set; }

        private void Awake()
        {
            if (body == null) body = GetComponent<Rigidbody2D>();
            if (hitCollider == null) hitCollider = GetComponent<Collider2D>();
            if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
            if (attack == null) attack = GetComponent<RagdollAttackManager2D>();
        }

        private void FixedUpdate()
        {
            if (!activeShot) return;
            RemainingLifetime -= Time.fixedDeltaTime;
            if (RemainingLifetime <= 0f) recycle?.Invoke(this);
        }

        public void Launch(Vector2 origin, Vector2 velocity, float lifetime, bool isBomb,
            float directDamage, float speedDamage, float damageCap, float explosionDamage,
            float limbImpulse, float bodyVelocity, LevelFourPipeVFXController2D vfx,
            Action<LevelFourPipeProjectile2D> recycleAction)
        {
            recycle = recycleAction;
            bomb = isBomb;
            blastDamage = Mathf.Max(0f, explosionDamage);
            hitImpulse = Mathf.Max(0f, limbImpulse);
            wholeBodyVelocity = Mathf.Max(0f, bodyVelocity);
            impactVfx = vfx;
            RemainingLifetime = Mathf.Max(.25f, lifetime);
            activeShot = true;
            gameObject.SetActive(true);
            body.simulated = true;
            body.position = origin;
            body.rotation = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
            body.velocity = velocity;
            body.angularVelocity = 0f;
            hitCollider.enabled = true;
            spriteRenderer.enabled = true;
            if (trail != null)
            {
                trail.Clear();
                trail.emitting = true;
            }
            if (motionTrail != null)
            {
                motionTrail.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                motionTrail.Play();
            }
            attack.Configure(isBomb ? RagdollAttackType.Explosion : RagdollAttackType.Custom,
                directDamage, speedDamage, 0f, damageCap);
            attack.ResetAttack();
            body.WakeUp();
        }

        public void Recycle()
        {
            activeShot = false;
            RemainingLifetime = 0f;
            if (body != null)
            {
                body.velocity = Vector2.zero;
                body.angularVelocity = 0f;
                body.simulated = false;
            }
            if (hitCollider != null) hitCollider.enabled = false;
            if (spriteRenderer != null) spriteRenderer.enabled = false;
            if (trail != null)
            {
                trail.emitting = false;
                trail.Clear();
            }
            if (motionTrail != null)
                motionTrail.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            gameObject.SetActive(false);
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (!activeShot || collision.collider == null) return;
            Rigidbody2D hitBody = collision.collider.attachedRigidbody;
            RagdollController ragdoll = hitBody != null ? hitBody.GetComponentInParent<RagdollController>() : null;
            if (ragdoll == null) return;

            Vector2 point = collision.contactCount > 0 ? collision.GetContact(0).point : hitBody.worldCenterOfMass;
            Vector2 direction = body.velocity.sqrMagnitude > .0001f ? body.velocity.normalized : Vector2.up;
            impactVfx?.PlayImpact(bomb, point, direction);
            RagdollPartHealth part = hitBody.GetComponent<RagdollPartHealth>();
            if (part == null)
            {
                recycle?.Invoke(this);
                return;
            }
            hitBody.AddForceAtPosition(direction * hitImpulse, point, ForceMode2D.Impulse);

            if (ragdoll != null)
            {
                for (int i = 0; i < ragdoll.Parts.Count; i++)
                {
                    Rigidbody2D target = ragdoll.Parts[i]?.Body;
                    if (target == null || !target.simulated) continue;
                    target.AddForce(direction * target.mass * wholeBodyVelocity, ForceMode2D.Impulse);
                    target.WakeUp();
                }
                if (bomb && blastDamage > 0f)
                {
                    RagdollDamageManager damage = ragdoll.GetComponent<RagdollDamageManager>();
                    damage?.ApplyDirectDamage(hitBody, blastDamage,
                        Mathf.Max(hitImpulse, body.velocity.magnitude), direction, point);
                }
            }
            recycle?.Invoke(this);
        }
    }
}
