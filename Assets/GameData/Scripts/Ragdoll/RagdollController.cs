using System;
using System.Collections.Generic;
using UnityEngine;

namespace KickTheBuddy.Physics
{
    /// <summary>Small public facade for the explicitly authored ragdoll subsystem.</summary>
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RagdollRigController2D), typeof(RagdollPoseController2D), typeof(RagdollProfileController2D))]
    [RequireComponent(typeof(RagdollStateController2D), typeof(RagdollDamageManager), typeof(RagdollElementalEffects))]
    [RequireComponent(typeof(RagdollAnimationController), typeof(RagdollInputManager))]
    public sealed class RagdollController : MonoBehaviour
    {
        [Serializable]
        public sealed class RagdollPart
        {
            [SerializeField] private string displayName;
            [SerializeField] private RagdollPartType partType;
            [SerializeField] private bool upperLimb;
            [SerializeField] private Rigidbody2D body;
            [SerializeField] private Collider2D[] colliders = Array.Empty<Collider2D>();
            [SerializeField] private HingeJoint2D[] hinges = Array.Empty<HingeJoint2D>();
            [SerializeField] private SpriteRenderer visual;
            [SerializeField] private RagdollLimb collisionRelay;
            [SerializeField] private ActiveRagdollLimb activeLimb;
            [SerializeField] private DismemberableLimb dismemberableLimb;
            [SerializeField] private DamageReceiver2D damageReceiver;
            [SerializeField] private RagdollPartHealth health;
            [SerializeField] private TargetJoint2D dragJoint;

            public string DisplayName => displayName;
            public RagdollPartType PartType => partType;
            public bool IsUpperLimb => upperLimb;
            public Rigidbody2D Body => body;
            public Collider2D[] Colliders => colliders;
            public HingeJoint2D[] Hinges => hinges;
            public SpriteRenderer Visual => visual;
            public RagdollLimb CollisionRelay => collisionRelay;
            public ActiveRagdollLimb ActiveLimb => activeLimb;
            public DismemberableLimb DismemberableLimb => dismemberableLimb;
            public DamageReceiver2D DamageReceiver => damageReceiver;
            public RagdollPartHealth Health => health;
            public TargetJoint2D DragJoint => dragJoint;
            public bool IsConfigured => body != null && health != null && collisionRelay != null && damageReceiver != null;
        }

        [Header("Focused Modules")]
        [SerializeField] private RagdollRigController2D rig;
        [SerializeField] private RagdollPoseController2D pose;
        [SerializeField] private RagdollProfileController2D profiles;
        [SerializeField] private RagdollStateController2D state;
        [SerializeField] private RagdollDamageManager damageManager;
        [SerializeField] private RagdollElementalEffects elementalEffects;
        [SerializeField] private RagdollAnimationController animationController;
        [SerializeField] private RagdollInputManager inputManager;

        [Header("Combo")]
        [Min(0f)] [SerializeField] private float comboTimeout = 1.25f;
        [SerializeField] private float currentHealth;
        [SerializeField] private float maximumHealth = 1f;
        [SerializeField] private int currentCombo;
        private float lastDamageTime = float.NegativeInfinity;
        private bool deathNotified;
        private Vector2 lastImpactPoint;

        public event Action<float, Vector2> OnDamageTaken;
        public event Action<float, float, Vector2> OnImpactResolved;
        public event Action OnCharacterKO;
        public event Action<Vector2> OnCharacterDied;
        public event Action<int, float, Vector2> OnComboAdvanced;
        public event Action OnCharacterRevived;
        public event Action<RagdollProfile> OnProfileApplied;
        public event Action<RagdollProfileType, Color, Vector2> OnProfileDamageEffect;
        public event Action<Rigidbody2D, float, float, Vector2> OnLimbDamaged;
        public event Action<Rigidbody2D, Vector2> OnLimbBroken;
        public event Action<bool> OnLimpStateChanged;
        public event Action<float> OnKnockoutStarted;

        private static readonly IReadOnlyList<RagdollPart> EmptyParts = Array.Empty<RagdollPart>();

        public IReadOnlyList<RagdollPart> Parts => rig != null ? rig.Parts : EmptyParts;
        public float CurrentHealth => currentHealth;
        public float MaximumHealth => maximumHealth;
        public int CurrentCombo => currentCombo;
        public Vector2 LastImpactPoint => lastImpactPoint;
        public RagdollState CurrentState => state != null ? state.CurrentState : RagdollState.Active;
        public bool IsGettingUp => pose != null && pose.IsGettingUp;
        public float StandUpDelay => pose != null ? pose.StandUpDelay : 0f;
        public RagdollProfile ActiveProfile => profiles != null ? profiles.ActiveProfile : null;
        public RagdollCategory Category => profiles != null ? profiles.Category : RagdollCategory.SolidRobot;
        public bool IsLimp => state != null && state.IsLimp;
        public bool IsUserDragging => pose != null && pose.IsDragging;
        public Color DamageParticleColor => profiles != null ? profiles.DamageParticleColor : Color.white;
        internal float BaseLimbHealth => rig != null ? rig.BaseLimbHealth : 45f;
        internal RagdollElementalEffects ElementalEffects => elementalEffects;

        private void Awake()
        {
            if (!HasRequiredReferences())
            {
                Debug.LogError("RagdollController is missing authored module references. Run Tools/Ragdoll/Setup Explicit Main Parts.", this);
                enabled = false;
                return;
            }

            damageManager.Initialize(this, elementalEffects);
            rig.Initialize(this, damageManager);
            damageManager.ConfigureParts(rig.Parts);
            elementalEffects.Initialize(this, damageManager, rig.Parts);
            profiles.Initialize(this, rig);
            pose.Initialize(this, rig, profiles);
            state.Initialize(this, rig, pose, profiles, damageManager);
            inputManager.ApplyDragSettingsToAllParts();
        }

        private bool HasRequiredReferences()
        {
            return rig != null && pose != null && profiles != null && state != null &&
                   damageManager != null && elementalEffects != null && animationController != null &&
                   inputManager != null;
        }

        private void Update()
        {
            if (currentCombo > 0 && Time.time - lastDamageTime >= comboTimeout)
                currentCombo = 0;
        }

        private void OnDisable()
        {
            if (pose != null) pose.SetDragging(false);
        }

        public void InitializeParts()
        {
            if (rig == null || damageManager == null) return;
            rig.Rebuild();
            damageManager.ConfigureParts(rig.Parts);
            inputManager?.ApplyDragSettingsToAllParts();
        }

        public void ApplyProfile(RagdollProfile newProfile) => profiles?.ApplyProfile(newProfile);
        public void SelectCategory(RagdollCategory category) => profiles?.SelectCategory(category);
        public void SetState(RagdollState newState) => state?.SetState(newState);
        public void SetLimpState(bool makeLimp) => state?.SetLimpState(makeLimp);
        public void Knockout(float duration) => state?.Knockout(duration);
        public void Revive() => state?.Revive();

        internal void SynchronizeAggregateHealth(float combinedHealth, float combinedMaximumHealth,
            float appliedDamage, float impactSpeed, Vector2 point)
        {
            maximumHealth = Mathf.Max(1f, combinedMaximumHealth);
            currentHealth = Mathf.Clamp(combinedHealth, 0f, maximumHealth);
            if (appliedDamage > 0f)
            {
                currentCombo++;
                lastDamageTime = Time.time;
                lastImpactPoint = point;
                OnComboAdvanced?.Invoke(currentCombo, appliedDamage, point);
                OnDamageTaken?.Invoke(appliedDamage, point);
                RagdollProfile profile = ActiveProfile;
                if (profile != null) OnProfileDamageEffect?.Invoke(profile.ProfileType, profile.DamageParticleColor, point);
                OnImpactResolved?.Invoke(appliedDamage, impactSpeed, point);
            }
            if (currentHealth <= 0f && !deathNotified)
            {
                deathNotified = true;
                state?.Die();
                OnCharacterDied?.Invoke(lastImpactPoint);
            }
        }

        internal void NotifyLimbDamaged(DismemberableLimb limb, float damage, Vector2 point)
        {
            if (limb != null) OnLimbDamaged?.Invoke(limb.Body, damage, limb.JointHealth, point);
        }

        internal void NotifyLimbBroken(DismemberableLimb limb, Vector2 point)
        {
            if (limb == null) return;
            damageManager?.NotifyPartBroken(limb.Body, point);
            OnLimbBroken?.Invoke(limb.Body, point);
        }

        internal void NotifyLimbDamaged(RagdollBreakableLimb limb, float damage, Vector2 point)
        {
            if (limb != null) OnLimbDamaged?.Invoke(limb.Body, damage, limb.CurrentHealth, point);
        }

        internal void NotifyLimbBroken(RagdollBreakableLimb limb, Vector2 point)
        {
            if (limb == null) return;
            damageManager?.NotifyPartBroken(limb.Body, point);
            OnLimbBroken?.Invoke(limb.Body, point);
        }

        internal void NotifyExternalDragStarted(Rigidbody2D grabbedBody)
        {
            pose?.SetDragging(true);
            rig?.SetGrabFlexibility(grabbedBody, true);
        }

        internal void NotifyExternalDragEnded(Rigidbody2D grabbedBody)
        {
            rig?.SetGrabFlexibility(grabbedBody, false);
            pose?.SetDragging(false);
        }
        internal void NotifyProfileApplied(RagdollProfile profile) => OnProfileApplied?.Invoke(profile);
        internal void NotifyLimpStateChanged(bool limp) => OnLimpStateChanged?.Invoke(limp);
        internal void NotifyKnockoutStarted(float duration) => OnKnockoutStarted?.Invoke(duration);
        internal void NotifyCharacterKnockedOut() => OnCharacterKO?.Invoke();
        internal void NotifyCharacterRevived()
        {
            deathNotified = false;
            OnCharacterRevived?.Invoke();
        }

        internal void ResetCombo()
        {
            currentCombo = 0;
            lastDamageTime = float.NegativeInfinity;
        }

        private void OnValidate()
        {
            comboTimeout = Mathf.Max(0f, comboTimeout);
            maximumHealth = Mathf.Max(1f, maximumHealth);
            currentHealth = Mathf.Clamp(currentHealth, 0f, maximumHealth);
        }
    }
}



