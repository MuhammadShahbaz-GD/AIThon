using System;
using KickTheBuddy.Gameplay;
using UnityEngine;

namespace KickTheBuddy.VFX
{
    /// <summary>
    /// Presentation-only consumer for Level 3 cannon impacts. Every burst is authored in the scene
    /// and reused in a round-robin pool; firing never instantiates or destroys a GameObject.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CandyCannonVFXController2D : MonoBehaviour
    {
        [Serializable]
        private sealed class MuzzleLayers
        {
            [SerializeField] private ParticleSystem core;
            [SerializeField] private ParticleSystem rays;
            [SerializeField] private ParticleSystem candy;

            public ParticleSystem Core => core;
            public ParticleSystem Rays => rays;
            public ParticleSystem Candy => candy;
        }

        [SerializeField] private CandyCannonController2D cannons;
        [SerializeField] private MuzzleLayers leftMuzzle = new MuzzleLayers();
        [SerializeField] private MuzzleLayers rightMuzzle = new MuzzleLayers();
        [Tooltip("Pre-authored impact roots. Children are played recursively with each root.")]
        [SerializeField] private ParticleSystem[] impactPool = Array.Empty<ParticleSystem>();

        private int nextImpactIndex;

        public int ImpactPoolSize => impactPool != null ? impactPool.Length : 0;
        public int FirePlayCount { get; private set; }
        public int ImpactPlayCount { get; private set; }
        public Vector2 LastImpactPoint { get; private set; }

        public event Action<CandyCannonSide, Vector2, float> ImpactPlayed;
        public event Action<CandyCannonSide, Vector2, bool> FirePlayed;

        private void Awake() => ResetVFX();

        private void OnEnable()
        {
            if (cannons == null) return;
            cannons.CannonFired += HandleCannonFired;
            cannons.ProjectileHit += HandleProjectileHit;
        }

        private void OnDisable()
        {
            if (cannons != null)
            {
                cannons.CannonFired -= HandleCannonFired;
                cannons.ProjectileHit -= HandleProjectileHit;
            }
            StopAll();
        }

        public void ResetVFX()
        {
            nextImpactIndex = 0;
            FirePlayCount = 0;
            ImpactPlayCount = 0;
            LastImpactPoint = Vector2.zero;
            StopAll();
        }

        private void HandleCannonFired(CandyCannonSide side, Vector2 origin, Vector2 velocity, bool charged)
        {
            MuzzleLayers layers = side == CandyCannonSide.Left ? leftMuzzle : rightMuzzle;
            if (layers?.Core == null) return;
            Vector2 direction = velocity.sqrMagnitude > .001f ? velocity.normalized :
                side == CandyCannonSide.Left ? Vector2.right : Vector2.left;
            layers.Core.transform.position = origin;
            layers.Core.transform.rotation = Quaternion.Euler(
                0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
            layers.Core.Emit(charged ? 2 : 1);
            if (layers.Rays != null) layers.Rays.Emit(charged ? 8 : 6);
            if (layers.Candy != null) layers.Candy.Emit(charged ? 6 : 4);
            FirePlayCount++;
            FirePlayed?.Invoke(side, origin, charged);
        }

        private void HandleProjectileHit(CandyCannonSide side, Rigidbody2D body, float damage, Vector2 point)
        {
            if (impactPool == null || impactPool.Length == 0) return;
            ParticleSystem burst = impactPool[nextImpactIndex];
            nextImpactIndex = (nextImpactIndex + 1) % impactPool.Length;
            if (burst == null) return;

            burst.transform.position = point;
            burst.transform.rotation = Quaternion.Euler(0f, 0f,
                side == CandyCannonSide.Left ? 0f : 180f);
            float strength = Mathf.Clamp01(damage / 14f);
            burst.transform.localScale = Vector3.one * Mathf.Lerp(.85f, 1.25f, strength);
            burst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            burst.Play(true);

            LastImpactPoint = point;
            ImpactPlayCount++;
            ImpactPlayed?.Invoke(side, point, damage);
        }

        private void StopAll()
        {
            StopMuzzle(leftMuzzle);
            StopMuzzle(rightMuzzle);
            if (impactPool == null) return;
            for (int i = 0; i < impactPool.Length; i++)
                if (impactPool[i] != null)
                    impactPool[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private static void StopMuzzle(MuzzleLayers layers)
        {
            if (layers == null) return;
            if (layers.Core != null)
                layers.Core.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (layers.Rays != null)
                layers.Rays.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (layers.Candy != null)
                layers.Candy.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
