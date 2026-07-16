using UnityEngine;

namespace KickTheBuddy.Physics
{
    [DisallowMultipleComponent]
    public sealed class RagdollStateController2D : MonoBehaviour
    {
        [Header("Knockout")]
        [Min(0f)] [SerializeField] private float reviveDelay = 4f;

        [Header("Limp Physics")]
        [SerializeField] private float gravityScaleDefault = .6f;
        [SerializeField] private float gravityScaleKnockedOut = 1.2f;
        [Min(0f)] [SerializeField] private float dragDefault = 1f;
        [Min(0f)] [SerializeField] private float angularDragDefault = 1.5f;
        [SerializeField] private RagdollState currentState = RagdollState.Active;

        private RagdollController owner;
        private RagdollRigController2D rig;
        private RagdollPoseController2D pose;
        private RagdollProfileController2D profiles;
        private RagdollDamageManager damageManager;
        private float reviveAt = float.PositiveInfinity;
        private bool isLimp;
        private bool initialized;

        public RagdollState CurrentState => currentState;
        public bool IsLimp => isLimp;

        internal void Initialize(
            RagdollController controller,
            RagdollRigController2D ragdollRig,
            RagdollPoseController2D poseController,
            RagdollProfileController2D profileController,
            RagdollDamageManager damage)
        {
            owner = controller;
            rig = ragdollRig;
            pose = poseController;
            profiles = profileController;
            damageManager = damage;
            initialized = true;
        }

        private void Update()
        {
            if (initialized &&
                currentState == RagdollState.KnockedOut &&
                Time.time >= reviveAt)
            {
                Revive();
            }
        }

        public void SetState(RagdollState state)
        {
            if (state == RagdollState.KnockedOut)
            {
                Knockout(reviveDelay);
                return;
            }

            rig.RestoreAuthoredPhysics();
            if (profiles.ActiveProfile != null) profiles.ApplyProfile(profiles.ActiveProfile);
            currentState = state;

            if (state != RagdollState.Frozen) return;
            pose.SetDragging(false);
            pose.CancelRecovery();
            rig.SetAllMotors(false);
            rig.FreezeAll();
        }

        public void SetLimpState(bool makeLimp)
        {
            if (isLimp == makeLimp) return;

            isLimp = makeLimp;
            pose.SetDragging(false);
            pose.CancelRecovery();
            rig.ApplyLimpPhysics(
                makeLimp,
                gravityScaleDefault,
                gravityScaleKnockedOut,
                dragDefault,
                angularDragDefault);
            owner?.NotifyLimpStateChanged(makeLimp);
        }

        internal void KnockoutDefault()
        {
            Knockout(reviveDelay);
        }

        public void Knockout(float duration)
        {
            if (currentState == RagdollState.Dead) return;
            duration = Mathf.Max(0f, duration);
            pose.SetDragging(false);
            pose.CancelRecovery();
            currentState = RagdollState.KnockedOut;
            reviveAt = Time.time + duration;
            SetLimpState(true);
            rig.SetLimitsEnabled(false);
            owner?.NotifyKnockoutStarted(duration);
            owner?.NotifyCharacterKnockedOut();
        }

        public void Die()
        {
            if (currentState == RagdollState.Dead) return;
            reviveAt = float.PositiveInfinity;
            currentState = RagdollState.Dead;
            isLimp = true;
            pose.SetDragging(false);
            pose.CancelRecovery();
            rig.EnterDeathState();
            owner?.NotifyLimpStateChanged(true);
        }
        public void Revive()
        {
            rig.RestoreAuthoredPhysics();
            rig.RestoreDeathVisuals();
            if (profiles.ActiveProfile != null) profiles.ApplyProfile(profiles.ActiveProfile);

            damageManager?.RestoreAllParts();
            currentState = RagdollState.Active;
            reviveAt = float.PositiveInfinity;
            isLimp = false;
            pose.CancelRecovery();
            owner?.ResetCombo();
            owner?.NotifyLimpStateChanged(false);
            owner?.NotifyCharacterRevived();
        }

        private void OnValidate()
        {
            reviveDelay = Mathf.Max(0f, reviveDelay);
            gravityScaleDefault = Mathf.Max(0f, gravityScaleDefault);
            gravityScaleKnockedOut = Mathf.Max(0f, gravityScaleKnockedOut);
            dragDefault = Mathf.Max(0f, dragDefault);
            angularDragDefault = Mathf.Max(0f, angularDragDefault);
        }
    }
}


