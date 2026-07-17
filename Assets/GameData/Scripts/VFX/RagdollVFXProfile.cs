using UnityEngine;

namespace KickTheBuddy.VFX
{
    [CreateAssetMenu(menuName = "Kick The Buddy/VFX/Ragdoll VFX Profile", fileName = "Ragdoll VFX Profile")]
    public sealed class RagdollVFXProfile : ScriptableObject
    {
        [SerializeField] private ParticleSystem hitPrefab;
        [SerializeField] private ParticleSystem comboPrefab;
        [SerializeField] private ParticleSystem knockoutPrefab;
        [SerializeField] private ParticleSystem deathPrefab;
        [SerializeField] private ParticleSystem collisionFumePrefab;

        public ParticleSystem HitPrefab => hitPrefab;
        public ParticleSystem ComboPrefab => comboPrefab;
        public ParticleSystem KnockoutPrefab => knockoutPrefab;
        public ParticleSystem DeathPrefab => deathPrefab;
        public ParticleSystem CollisionFumePrefab => collisionFumePrefab;
    }
}
