using UnityEngine;

namespace KickTheBuddy.VFX
{
    /// <summary>Single authored particle used for every ragdoll impact.</summary>
    [CreateAssetMenu(menuName = "Kick The Buddy/VFX/Ragdoll VFX Profile", fileName = "Ragdoll VFX Profile")]
    public sealed class RagdollVFXProfile : ScriptableObject
    {
        [Tooltip("One world-space ParticleSystem instance is reused for all hit points.")]
        [SerializeField] private ParticleSystem hitPrefab;

        public ParticleSystem HitPrefab => hitPrefab;
    }
}
