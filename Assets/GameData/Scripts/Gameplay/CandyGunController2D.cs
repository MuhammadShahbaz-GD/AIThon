using System;
using KickTheBuddy.Physics;
using UnityEngine;

namespace KickTheBuddy.Gameplay
{
    /// <summary>
    /// Automatic held-fire adapter for an authored SandboxTool2D candy gun. Projectiles are
    /// prewarmed scene children, keeping repeated mobile fire allocation-free and bounded.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CandyGunController2D : MonoBehaviour
    {
        private const float CandyBulletBaseDamage = 85f;
        private const float CandyBulletDamagePerSpeed = 0f;
        private const float CandyBulletMinimumDamageSpeed = 0f;
        private const float CandyBulletMaximumDamage = 85f;

        [SerializeField] private SandboxTool2D gunTool;
        [SerializeField] private Transform muzzle;
        [Tooltip("Authored ragdoll target used to aim every shot while the gun is held.")]
        [SerializeField] private Transform aimTarget;
        [SerializeField] private CandyGunProjectile2D[] projectilePool =
            Array.Empty<CandyGunProjectile2D>();
        [Min(1f)] [SerializeField] private float launchSpeed = 17f;
        [Min(.03f)] [SerializeField] private float fireCooldown = .14f;
        [Min(0f)] [SerializeField] private float recoilImpulse = .45f;
        [SerializeField] private bool automaticFireWhileGrabbed = true;
        [Tooltip("Preserves one-shot accessibility when a press is released before a physics step.")]
        [SerializeField] private bool tapToFire = true;
        [Tooltip("Keeps the visible gun barrel pointed at the ragdoll while held.")]
        [SerializeField] private bool lockAimWhileGrabbed = true;
        [SerializeField, Range(-180f, 180f)] private float aimOffsetDegrees;

        private int poolCursor;
        private float nextFireTime;
        private bool triggerHeld;

        public int FiredCount { get; private set; }
        public int HitCount { get; private set; }
        public Rigidbody2D LastHitBody { get; private set; }
        public Transform AimTarget => aimTarget;
        public int ActiveProjectileCount
        {
            get
            {
                int active = 0;
                for (int i = 0; i < projectilePool.Length; i++)
                    if (projectilePool[i] != null && !projectilePool[i].IsAvailable) active++;
                return active;
            }
        }

        public event Action<CandyGunController2D, Vector2, Vector2> Fired;
        public event Action<CandyGunController2D, Rigidbody2D, Vector2, float> ProjectileHit;
        public event Action<CandyGunController2D> PoolExhausted;

        private void OnEnable()
        {
            ResolveLiveRagdollTarget();
            if (gunTool != null)
            {
                gunTool.Grabbed += HandleGrabbed;
                gunTool.Released += HandleReleased;
                gunTool.Tapped += HandleTap;
            }
            for (int i = 0; i < projectilePool.Length; i++)
            {
                CandyGunProjectile2D projectile = projectilePool[i];
                if (projectile == null) continue;
                projectile.Attack?.Configure(
                    RagdollAttackType.CandyProjectile,
                    CandyBulletBaseDamage,
                    CandyBulletDamagePerSpeed,
                    CandyBulletMinimumDamageSpeed,
                    CandyBulletMaximumDamage);
                projectile.Hit += HandleProjectileHit;
            }
        }

        private void OnDisable()
        {
            triggerHeld = false;
            if (gunTool != null)
            {
                gunTool.Grabbed -= HandleGrabbed;
                gunTool.Released -= HandleReleased;
                gunTool.Tapped -= HandleTap;
            }
            for (int i = 0; i < projectilePool.Length; i++)
            {
                CandyGunProjectile2D projectile = projectilePool[i];
                if (projectile == null) continue;
                projectile.Hit -= HandleProjectileHit;
                projectile.Deactivate();
            }
        }

        private void HandleTap(SandboxTool2D tool, Vector2 point)
        {
            if (tapToFire && tool == gunTool) TryFire();
        }

        private void HandleGrabbed(SandboxTool2D tool, Vector2 point)
        {
            if (tool != gunTool || !automaticFireWhileGrabbed) return;
            triggerHeld = true;
            nextFireTime = Mathf.Min(nextFireTime, Time.fixedTime);
        }

        private void HandleReleased(SandboxTool2D tool, Vector2 point)
        {
            if (tool == gunTool) triggerHeld = false;
        }

        private void FixedUpdate()
        {
            if (!triggerHeld || gunTool == null || !gunTool.IsDragging) return;
            if (lockAimWhileGrabbed) AimGunBody();
            TryFireAt(Time.fixedTime);
        }

        public bool TryFire() => TryFireAt(Time.fixedTime);

        private bool TryFireAt(float currentTime)
        {
            if (gunTool == null || muzzle == null || currentTime < nextFireTime) return false;
            if (!ResolveLiveRagdollTarget()) return false;
            CandyGunProjectile2D projectile = NextAvailableProjectile();
            if (projectile == null)
            {
                nextFireTime = currentTime + fireCooldown;
                PoolExhausted?.Invoke(this);
                return false;
            }

            nextFireTime = currentTime + fireCooldown;
            Vector2 direction = ResolveFireDirection();
            if (direction.sqrMagnitude <= .0001f) return false;
            Vector2 velocity = direction * launchSpeed;
            float projectileRotation = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            projectile.Launch(muzzle.position, projectileRotation, velocity);
            Rigidbody2D gunBody = gunTool.Body;
            if (gunBody != null && recoilImpulse > 0f)
                gunBody.AddForceAtPosition(-direction * recoilImpulse, muzzle.position, ForceMode2D.Impulse);
            FiredCount++;
            Fired?.Invoke(this, muzzle.position, velocity);
            return true;
        }

        private Vector2 ResolveFireDirection()
        {
            if (aimTarget == null || muzzle == null) return Vector2.zero;
            Vector2 aimedDirection = (Vector2)aimTarget.position - (Vector2)muzzle.position;
            return aimedDirection.sqrMagnitude > .0001f ? aimedDirection.normalized : Vector2.zero;
        }

        private bool ResolveLiveRagdollTarget()
        {
            RagdollController ragdoll = aimTarget != null
                ? aimTarget.GetComponentInParent<RagdollController>()
                : GameplayLevelSceneController.Active?.ActiveRagdoll;
            if (ragdoll == null || ragdoll.CurrentHealth <= 0f)
            {
                aimTarget = null;
                return false;
            }

            var parts = ragdoll.Parts;
            for (int i = 0; i < parts.Count; i++)
            {
                RagdollController.RagdollPart part = parts[i];
                if (part == null || part.PartType != RagdollPartType.Torso || part.Body == null) continue;
                aimTarget = part.Body.transform;
                return true;
            }

            aimTarget = null;
            return false;
        }

        private void AimGunBody()
        {
            Rigidbody2D gunBody = gunTool.Body;
            if (gunBody == null || aimTarget == null) return;
            Vector2 direction = (Vector2)aimTarget.position - gunBody.worldCenterOfMass;
            if (direction.sqrMagnitude < .0001f) return;
            float targetRotation = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg +
                                   aimOffsetDegrees;
            gunBody.angularVelocity = 0f;
            gunBody.MoveRotation(targetRotation);
        }

        private CandyGunProjectile2D NextAvailableProjectile()
        {
            int count = projectilePool.Length;
            for (int offset = 0; offset < count; offset++)
            {
                int index = (poolCursor + offset) % count;
                CandyGunProjectile2D candidate = projectilePool[index];
                if (candidate == null || !candidate.IsAvailable) continue;
                poolCursor = (index + 1) % count;
                return candidate;
            }
            return null;
        }

        private void HandleProjectileHit(
            CandyGunProjectile2D projectile,
            Rigidbody2D body,
            Vector2 point,
            float speed)
        {
            HitCount++;
            LastHitBody = body;
            ProjectileHit?.Invoke(this, body, point, speed);
        }

        private void OnValidate()
        {
            launchSpeed = Mathf.Max(1f, launchSpeed);
            fireCooldown = Mathf.Max(.03f, fireCooldown);
            recoilImpulse = Mathf.Max(0f, recoilImpulse);
            if (projectilePool == null) projectilePool = Array.Empty<CandyGunProjectile2D>();
        }
    }
}
