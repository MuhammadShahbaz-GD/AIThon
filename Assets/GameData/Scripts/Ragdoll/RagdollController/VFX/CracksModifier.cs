using System;
using UnityEngine;
using UnityEngine.Events;

namespace KickTheBuddy.Physics.VFX
{
    /// <summary>
    /// Event-driven crack-skin presentation for one ragdoll body part. Add this component beside
    /// RagdollPartHealth and order Crack Skins from lightest damage to most severe damage.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RagdollPartHealth))]
    [RequireComponent(typeof(DismemberableLimb))]
    public sealed class CracksModifier : MonoBehaviour
    {
        [Serializable]
        public sealed class LimbExplodedEvent : UnityEvent<CracksModifier, Vector2> { }

        [Header("Health Source")]
        [SerializeField] private RagdollPartHealth limbHealth;
        [SerializeField] private DismemberableLimb dismemberableLimb;

        [Header("Crack Skins")]
        [Tooltip("Order from the lightest crack skin to the most damaged skin. Only one stage is active at a time.")]
        [SerializeField] private GameObject[] crackSkins = Array.Empty<GameObject>();
        [Tooltip("Cracks remain hidden above this normalized health. 0.85 means cracks begin below 85% health.")]
        [Range(.01f, 1f)] [SerializeField] private float cracksStartAtNormalizedHealth = .85f;
        [Tooltip("Hide the optional healthy skin while a crack skin is active.")]
        [SerializeField] private bool replaceHealthySkin;
        [SerializeField] private GameObject healthySkin;

        [Header("Depletion")]
        [Tooltip("Detach this limb from its parent joint when local health reaches zero.")]
        [SerializeField] private bool breakLimbAtZeroHealth = true;
        [Tooltip("Optional impulse passed to the detached limb. Usually zero because the attack already applied force.")]
        [SerializeField] private Vector2 breakImpulse;
        [SerializeField] private LimbExplodedEvent onExplode = new LimbExplodedEvent();

        private Vector2 lastDamagePoint;
        private int activeSkinIndex = -1;
        private bool exploded;

        public event Action<CracksModifier, int> CrackStageChanged;
        public event Action<CracksModifier, Vector2> Exploded;

        public RagdollPartHealth LimbHealth => limbHealth;
        public int ActiveSkinIndex => activeSkinIndex;
        public bool HasExploded => exploded;
        public LimbExplodedEvent OnExplode => onExplode;

        private void Awake()
        {
            CacheReferences();
            SetCrackStage(ResolveCrackStage());
        }

        private void OnEnable()
        {
            CacheReferences();
            Subscribe();
            SetCrackStage(ResolveCrackStage());
        }

        private void OnDisable() => Unsubscribe();

        private void OnDestroy() => Unsubscribe();

        private void HandleDamaged(RagdollPartHealth part, float damage, Vector2 point)
        {
            lastDamagePoint = point;
            SetCrackStage(ResolveCrackStage());
        }

        private void HandleDepleted(RagdollPartHealth part)
        {
            SetCrackStage(crackSkins == null ? -1 : crackSkins.Length - 1);
            if (exploded) return;

            exploded = true;
            Vector2 point = lastDamagePoint;
            onExplode?.Invoke(this, point);
            Exploded?.Invoke(this, point);

            if (breakLimbAtZeroHealth && dismemberableLimb != null && !dismemberableLimb.IsSevered)
                dismemberableLimb.ForceSever(breakImpulse, point);
        }

        private void HandleRestored(RagdollPartHealth part)
        {
            exploded = false;
            lastDamagePoint = transform.position;
            SetCrackStage(-1);
        }

        private int ResolveCrackStage()
        {
            if (limbHealth == null || crackSkins == null || crackSkins.Length == 0) return -1;

            float health = limbHealth.NormalizedHealth;
            if (health >= cracksStartAtNormalizedHealth) return -1;

            float damageProgress = Mathf.InverseLerp(cracksStartAtNormalizedHealth, 0f, health);
            return Mathf.Min(Mathf.FloorToInt(damageProgress * crackSkins.Length), crackSkins.Length - 1);
        }

        private void SetCrackStage(int requestedIndex)
        {
            int nextIndex = crackSkins == null || crackSkins.Length == 0
                ? -1
                : Mathf.Clamp(requestedIndex, -1, crackSkins.Length - 1);

            for (int i = 0; i < crackSkins.Length; i++)
            {
                GameObject skin = crackSkins[i];
                if (skin != null && skin.activeSelf != (i == nextIndex))
                    skin.SetActive(i == nextIndex);
            }

            if (healthySkin != null && replaceHealthySkin)
                healthySkin.SetActive(nextIndex < 0);

            if (activeSkinIndex == nextIndex) return;
            activeSkinIndex = nextIndex;
            CrackStageChanged?.Invoke(this, activeSkinIndex);
        }

        private void CacheReferences()
        {
            if (limbHealth == null) limbHealth = GetComponent<RagdollPartHealth>();
            if (dismemberableLimb == null) dismemberableLimb = GetComponent<DismemberableLimb>();
            if (lastDamagePoint == Vector2.zero) lastDamagePoint = transform.position;
        }

        private void Subscribe()
        {
            if (limbHealth == null) return;
            Unsubscribe();
            limbHealth.Damaged += HandleDamaged;
            limbHealth.Depleted += HandleDepleted;
            limbHealth.Restored += HandleRestored;
        }

        private void Unsubscribe()
        {
            if (limbHealth == null) return;
            limbHealth.Damaged -= HandleDamaged;
            limbHealth.Depleted -= HandleDepleted;
            limbHealth.Restored -= HandleRestored;
        }

        private void OnValidate()
        {
            cracksStartAtNormalizedHealth = Mathf.Clamp(cracksStartAtNormalizedHealth, .01f, 1f);
            CacheReferences();
            if (!Application.isPlaying) SetCrackStage(-1);
        }
    }
}




