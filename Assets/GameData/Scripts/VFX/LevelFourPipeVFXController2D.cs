using System;
using UnityEngine;

namespace KickTheBuddy.VFX
{
    /// <summary>Pool-safe presentation for Level 4 pipe launches and impacts.</summary>
    [DisallowMultipleComponent]
    public sealed class LevelFourPipeVFXController2D : MonoBehaviour
    {
        [SerializeField] private ParticleSystem bombMuzzle;
        [SerializeField] private ParticleSystem sodaMuzzle;
        [SerializeField] private ParticleSystem[] bombImpactPool = Array.Empty<ParticleSystem>();
        [SerializeField] private ParticleSystem[] sodaImpactPool = Array.Empty<ParticleSystem>();

        private int nextBombImpact;
        private int nextSodaImpact;

        public int BombImpactPoolSize => bombImpactPool != null ? bombImpactPool.Length : 0;
        public int SodaImpactPoolSize => sodaImpactPool != null ? sodaImpactPool.Length : 0;

        public void PlayMuzzle(bool bomb, Vector2 point, Vector2 direction)
        {
            ParticleSystem system = bomb ? bombMuzzle : sodaMuzzle;
            if (system == null) return;
            system.transform.position = point;
            system.transform.rotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            system.Play(true);
            system.Emit(bomb ? 5 : 6);
        }

        public void PlayImpact(bool bomb, Vector2 point, Vector2 incomingDirection)
        {
            ParticleSystem[] pool = bomb ? bombImpactPool : sodaImpactPool;
            if (pool == null || pool.Length == 0) return;
            int index = bomb ? nextBombImpact : nextSodaImpact;
            ParticleSystem system = pool[index % pool.Length];
            if (bomb) nextBombImpact = (index + 1) % pool.Length;
            else nextSodaImpact = (index + 1) % pool.Length;
            if (system == null) return;
            system.transform.position = point;
            system.transform.rotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(incomingDirection.y, incomingDirection.x) * Mathf.Rad2Deg);
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            system.Play(true);
            system.Emit(bomb ? 10 : 8);
        }

        public void ResetVFX()
        {
            nextBombImpact = nextSodaImpact = 0;
            Stop(bombMuzzle);
            Stop(sodaMuzzle);
            StopPool(bombImpactPool);
            StopPool(sodaImpactPool);
        }

        private void OnDisable() => ResetVFX();

        private static void StopPool(ParticleSystem[] pool)
        {
            if (pool == null) return;
            for (int i = 0; i < pool.Length; i++) Stop(pool[i]);
        }

        private static void Stop(ParticleSystem system)
        {
            if (system != null) system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
