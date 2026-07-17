using System;
using UnityEngine;

namespace KickTheBuddy.Physics
{
    [DisallowMultipleComponent]
    public sealed class RagdollPoseController2D : MonoBehaviour
    {
        [Header("Timing")]
        [Min(0f)] [SerializeField] private float standUpDelay = 2f;
        [Range(.25f, 5f)] [SerializeField] private float standUpSpeed = 2f;
        [Range(10f, 80f)] [SerializeField] private float fallenAngle = 45f;
        [Range(2f, 35f)] [SerializeField] private float standingAngle = 16f;

        [Header("Joint Drive")]
        [Min(0f)] [SerializeField] private float standingMotorTorque = 95f;
        [Min(0f)] [SerializeField] private float getUpMotorTorque = 220f;
        [Min(0f)] [SerializeField] private float poseSpeedGain = 8f;
        [Min(0f)] [SerializeField] private float maximumMotorSpeed = 220f;

        [Header("Torso Balance")]
        [Min(0f)] [SerializeField] private float standingBalanceTorque = 20f;
        [Min(0f)] [SerializeField] private float getUpBalanceTorque = 65f;
        [Min(0f)] [SerializeField] private float balanceDamping = 3f;
        [Range(0f, 10f)] [SerializeField] private float idleAngleDeadZone = 2f;
        [Range(0f, 30f)] [SerializeField] private float idleAngularVelocityDeadZone = 5f;
        [Min(0f)] [SerializeField] private float getUpLiftForce = 52f;
        [Min(0f)] [SerializeField] private float maximumGetUpVelocity = 2.5f;

        [Header("Head Presence")]
        [Tooltip("Keeps the head alert in world space while the healthy character is active and not grabbed.")]
        [SerializeField] private bool keepHeadUpright = true;
        [Range(-30f, 30f)] [SerializeField] private float headUprightAngle;
        [Min(0f)] [SerializeField] private float headUprightTorque = 6f;
        [Min(0f)] [SerializeField] private float headUprightDamping = 1.2f;
        [Min(0f)] [SerializeField] private float maximumHeadUprightTorque = 45f;
        [Range(0f, 15f)] [SerializeField] private float headAngleDeadZone = 2.5f;
        [Range(0f, 30f)] [SerializeField] private float headAngularVelocityDeadZone = 6f;

        [Header("Active Limb Balance")]
        [Tooltip("Uses arms as counterweights and legs to keep the feet underneath the torso.")]
        [SerializeField] private bool useLimbCounterBalance = true;
        [Range(0f, 2f)] [SerializeField] private float standingArmTorqueMultiplier = .65f;
        [Range(0f, 3f)] [SerializeField] private float standingLegTorqueMultiplier = 1.35f;
        [Range(0f, 3f)] [SerializeField] private float getUpArmTorqueMultiplier = .9f;
        [Range(0f, 4f)] [SerializeField] private float getUpLegTorqueMultiplier = 1.65f;
        [Range(-1f, 1f)] [SerializeField] private float armCounterBalanceResponse = -.35f;
        [Range(-1f, 1f)] [SerializeField] private float legStabilizationResponse = .28f;
        [Range(0f, 35f)] [SerializeField] private float maximumArmCounterBalanceAngle = 14f;
        [Range(0f, 30f)] [SerializeField] private float maximumLegStabilizationAngle = 10f;
        [Min(0f)] [SerializeField] private float limbBalanceResponseSpeed = 90f;

        [Header("Broken Leg Recovery")]
        [Tooltip("A severed leg forces the body to fall toward its missing support.")]
        [SerializeField] private bool reactToBrokenLeg = true;
        [Tooltip("Optional wounded behavior. Disable to keep the character grounded permanently after either leg is severed.")]
        [SerializeField] private bool allowOneLegRecovery;
        [Tooltip("Time spent with standing motors disabled while the body falls toward the missing support.")]
        [Min(0f)] [SerializeField] private float brokenLegFallDuration = .8f;
        [Tooltip("Additional grounded pause before one-leg stabilization begins.")]
        [Min(0f)] [SerializeField] private float brokenLegRecoveryDelay = .45f;
        [Tooltip("Continuous torso torque applied toward the broken-leg side during the forced stumble.")]
        [Min(0f)] [SerializeField] private float brokenLegFallTorque = 32f;
        [Tooltip("Joint motor strength retained while trying to stabilize on one leg.")]
        [Range(0f, 1f)] [SerializeField] private float oneLegMotorStrength = .55f;
        [Tooltip("Torso balance strength retained after one leg has broken.")]
        [Range(0f, 1f)] [SerializeField] private float oneLegBalanceStrength = .35f;
        [Tooltip("Upward get-up lift retained after one leg has broken. Keep low to prevent hovering.")]
        [Range(0f, 1f)] [SerializeField] private float oneLegLiftStrength = .12f;
        [Tooltip("Head-upright strength retained while wounded so the character still feels alive.")]
        [Range(0f, 1f)] [SerializeField] private float oneLegHeadStrength = .75f;

        private RagdollController owner;
        private RagdollRigController2D rig;
        private RagdollProfileController2D profiles;
        private float fallenTime;
        private bool isGettingUp;
        private bool isDragging;
        private bool initialized;
        private float smoothedTorsoBalanceAngle;
        private int brokenLegCount;
        private bool brokenLegFallPending;
        private bool ownerEventsSubscribed;
        private bool oneLegRecoveryStarted;
        private float brokenLegFallDirection = 1f;
        private float brokenLegFallUntil = float.NegativeInfinity;
        private float brokenLegRecoveryAt = float.NegativeInfinity;

        public event Action<Rigidbody2D, int> BrokenLegResponseStarted;
        public event Action OneLegRecoveryStarted;

        public bool IsGettingUp => isGettingUp;
        public bool IsDragging => isDragging;
        public float StandUpDelay => standUpDelay;
        public int BrokenLegCount => brokenLegCount;
        public bool IsBrokenLegStumbling => brokenLegCount == 1 &&
                                           (brokenLegFallPending || Time.fixedTime < brokenLegFallUntil);
        public bool IsOneLegRecoveryActive => allowOneLegRecovery && brokenLegCount == 1 && !brokenLegFallPending &&
                                              Time.fixedTime >= brokenLegRecoveryAt;
        public float BrokenLegFallDuration => brokenLegFallDuration;
        public float BrokenLegRecoveryDelay => brokenLegRecoveryDelay;
        public float BrokenLegFallTorque => brokenLegFallTorque;
        public float OneLegMotorStrength => oneLegMotorStrength;
        public float OneLegBalanceStrength => oneLegBalanceStrength;
        public float OneLegLiftStrength => oneLegLiftStrength;
        public float OneLegHeadStrength => oneLegHeadStrength;
        public bool ReactToBrokenLeg => reactToBrokenLeg;
        public bool AllowOneLegRecovery => allowOneLegRecovery;

        internal void Initialize(RagdollController controller, RagdollRigController2D ragdollRig, RagdollProfileController2D profileController)
        {
            UnsubscribeFromOwner();
            owner = controller;
            rig = ragdollRig;
            profiles = profileController;
            initialized = owner != null && rig != null && profiles != null;
            brokenLegCount = CountBrokenLegs();
            brokenLegFallPending = false;
            oneLegRecoveryStarted = false;
            brokenLegFallUntil = float.NegativeInfinity;
            brokenLegRecoveryAt = float.NegativeInfinity;
            SubscribeToOwner();
        }

        private void OnEnable() => SubscribeToOwner();

        private void OnDisable() => UnsubscribeFromOwner();

        private void OnDestroy() => UnsubscribeFromOwner();

        private void FixedUpdate()
        {
            if (initialized) UpdateStandingAssist();
        }

        internal void SetDragging(bool value)
        {
            isDragging = value;
            if (!value) return;
            CancelRecovery();
            smoothedTorsoBalanceAngle = 0f;
            SetMotors(false);
        }

        internal void CancelRecovery()
        {
            fallenTime = 0f;
            isGettingUp = false;
        }

        private void UpdateStandingAssist()
        {
            bool canStand = owner.CurrentState == RagdollState.Active ||
                            owner.CurrentState == RagdollState.Burning;
            RagdollProfile profile = profiles.ActiveProfile;
            if (profile != null &&
                (profile.ProfileType == RagdollProfileType.MagicDoll ||
                 profile.ProfileType == RagdollProfileType.GalaxySpace ||
                 profile.JointSpringForce <= 0f))
            {
                canStand = false;
            }

            Rigidbody2D torso = rig.Torso;
            if (!canStand || owner.CurrentHealth <= 0f || torso == null || isDragging)
            {
                CancelRecovery();
                smoothedTorsoBalanceAngle = 0f;
                SetMotors(false);
                return;
            }

            if (reactToBrokenLeg && brokenLegCount >= 2)
            {
                brokenLegFallPending = false;
                oneLegRecoveryStarted = false;
                CancelRecovery();
                smoothedTorsoBalanceAngle = 0f;
                SetMotors(false);
                ApplyHeadUpright(rig.Head, oneLegHeadStrength * .7f);
                return;
            }

            if (reactToBrokenLeg && brokenLegCount == 1)
            {
                if (brokenLegFallPending) BeginBrokenLegFall();
                if (Time.fixedTime < brokenLegFallUntil)
                {
                    CancelRecovery();
                    smoothedTorsoBalanceAngle = 0f;
                    SetMotors(false);
                    torso.AddTorque(brokenLegFallDirection * brokenLegFallTorque, ForceMode2D.Force);
                    ApplyHeadUpright(rig.Head, oneLegHeadStrength * .45f);
                    return;
                }

                // With a missing support limb there is no valid standing pose. Keep every pose motor,
                // torso balance force and get-up lift disabled so physics leaves the character grounded.
                // A small head-only world-space torque preserves an alive/wounded reaction without standing.
                if (!allowOneLegRecovery)
                {
                    oneLegRecoveryStarted = false;
                    CancelRecovery();
                    smoothedTorsoBalanceAngle = 0f;
                    SetMotors(false);
                    ApplyHeadUpright(rig.Head, oneLegHeadStrength * .55f);
                    return;
                }

                if (Time.fixedTime < brokenLegRecoveryAt)
                {
                    CancelRecovery();
                    smoothedTorsoBalanceAngle = 0f;
                    SetMotors(false);
                    ApplyHeadUpright(rig.Head, oneLegHeadStrength * .65f);
                    return;
                }

                if (!oneLegRecoveryStarted)
                {
                    oneLegRecoveryStarted = true;
                    OneLegRecoveryStarted?.Invoke();
                }
            }

            float torsoBalanceError = Mathf.DeltaAngle(torso.rotation, 0f);
            float tilt = Mathf.Abs(torsoBalanceError);
            UpdateLimbBalanceAngle(torsoBalanceError);
            if (!isGettingUp && tilt > fallenAngle)
            {
                fallenTime += Time.fixedDeltaTime;

                // Keep an alert, coherent body pose while waiting for the stronger get-up phase.
                // This avoids a dead-looking limp pause without adding torso lift or locomotion.
                ApplyPoseMotors(standingMotorTorque);
                ApplyHeadUpright(rig.Head, ResolveOneLegStrength(oneLegHeadStrength));
                if (fallenTime < standUpDelay) return;
                isGettingUp = true;
            }

            if (isGettingUp)
            {
                ApplyPoseMotors(getUpMotorTorque);
                ApplyBalance(torso, getUpBalanceTorque);
                ApplyHeadUpright(rig.Head, standUpSpeed * ResolveOneLegStrength(oneLegHeadStrength));
                float velocityLimit = maximumGetUpVelocity * standUpSpeed;
                if (torso.velocity.y < velocityLimit)
                    torso.AddForce(Vector2.up * getUpLiftForce * standUpSpeed *
                                   ResolveOneLegStrength(oneLegLiftStrength), ForceMode2D.Force);

                if (tilt <= standingAngle && Mathf.Abs(torso.angularVelocity) < 45f)
                    CancelRecovery();
                return;
            }

            fallenTime = 0f;
            ApplyPoseMotors(standingMotorTorque);
            ApplyBalance(torso, standingBalanceTorque);
            ApplyHeadUpright(rig.Head, ResolveOneLegStrength(oneLegHeadStrength));
        }

        private void BeginBrokenLegFall()
        {
            brokenLegFallPending = false;
            oneLegRecoveryStarted = false;
            brokenLegFallUntil = Time.fixedTime + brokenLegFallDuration;
            brokenLegRecoveryAt = brokenLegFallUntil + brokenLegRecoveryDelay;
            CancelRecovery();
        }

        private void HandleLimbBroken(Rigidbody2D brokenBody, Vector2 point)
        {
            if (!reactToBrokenLeg || brokenBody == null || rig == null) return;

            RagdollController.RagdollPart brokenPart = null;
            var parts = rig.Parts;
            for (int i = 0; i < parts.Count; i++)
            {
                RagdollController.RagdollPart part = parts[i];
                if (part != null && part.Body == brokenBody)
                {
                    brokenPart = part;
                    break;
                }
            }

            if (brokenPart == null || brokenPart.PartType != RagdollPartType.Leg) return;
            int previousCount = brokenLegCount;
            brokenLegCount = CountBrokenLegs();
            if (brokenLegCount <= previousCount) return;

            Rigidbody2D torso = rig.Torso;
            if (torso != null)
                brokenLegFallDirection = brokenBody.worldCenterOfMass.x >= torso.worldCenterOfMass.x ? -1f : 1f;

            brokenLegFallPending = brokenLegCount == 1;
            oneLegRecoveryStarted = false;
            brokenLegFallUntil = float.NegativeInfinity;
            brokenLegRecoveryAt = float.PositiveInfinity;
            CancelRecovery();
            BrokenLegResponseStarted?.Invoke(brokenBody, brokenLegCount);
        }

        private int CountBrokenLegs()
        {
            if (rig == null) return 0;
            int count = 0;
            var parts = rig.Parts;
            for (int i = 0; i < parts.Count; i++)
            {
                RagdollController.RagdollPart part = parts[i];
                if (part != null && part.PartType == RagdollPartType.Leg &&
                    part.DismemberableLimb != null && part.DismemberableLimb.IsSevered)
                    count++;
            }
            return count;
        }

        private void SubscribeToOwner()
        {
            if (!isActiveAndEnabled || owner == null || ownerEventsSubscribed) return;
            owner.OnLimbBroken += HandleLimbBroken;
            ownerEventsSubscribed = true;
        }

        private void UnsubscribeFromOwner()
        {
            if (!ownerEventsSubscribed) return;
            if (owner != null) owner.OnLimbBroken -= HandleLimbBroken;
            ownerEventsSubscribed = false;
        }

        private float ResolveOneLegStrength(float oneLegStrength) =>
            reactToBrokenLeg && brokenLegCount == 1 ? oneLegStrength : 1f;

        private void ApplyHeadUpright(Rigidbody2D head, float strengthMultiplier)
        {
            if (!keepHeadUpright || head == null) return;

            float error = Mathf.DeltaAngle(head.rotation, headUprightAngle);
            if (!isGettingUp &&
                Mathf.Abs(error) <= headAngleDeadZone &&
                Mathf.Abs(head.angularVelocity) <= headAngularVelocityDeadZone)
            {
                return;
            }

            // A clamped proportional-damped torque makes the head feel attentive without injecting
            // energy once it is settled. Dragging exits before this method, so the grabbed body hangs naturally.
            float multiplier = Mathf.Max(0f, strengthMultiplier);
            float requestedTorque =
                error * headUprightTorque * multiplier -
                head.angularVelocity * headUprightDamping * multiplier;
            float torqueLimit = maximumHeadUprightTorque * Mathf.Max(1f, multiplier);
            head.AddTorque(Mathf.Clamp(requestedTorque, -torqueLimit, torqueLimit), ForceMode2D.Force);
        }

        private void UpdateLimbBalanceAngle(float torsoBalanceError)
        {
            float target = useLimbCounterBalance && Mathf.Abs(torsoBalanceError) > idleAngleDeadZone
                ? torsoBalanceError
                : 0f;
            smoothedTorsoBalanceAngle = Mathf.MoveTowards(
                smoothedTorsoBalanceAngle,
                target,
                limbBalanceResponseSpeed * Time.fixedDeltaTime);
        }

        private void ApplyPoseMotors(float torque)
        {
            RagdollProfile profile = profiles.ActiveProfile;
            if (profile != null)
                torque = profile.JointSpringForce * (isGettingUp ? 1.75f : 1f);

            float speedMultiplier = isGettingUp ? standUpSpeed : 1f;
            torque *= speedMultiplier * ResolveOneLegStrength(oneLegMotorStrength);
            var joints = rig.Joints;

            for (int i = 0; i < joints.Count; i++)
            {
                RagdollRigController2D.JointRuntime item = joints[i];
                HingeJoint2D joint = item.Joint;
                if (joint == null || joint.attachedRigidbody == null) continue;

                float targetAngle = item.RestAngle + ResolveBalanceOffset(item);
                float error = Mathf.DeltaAngle(joint.jointAngle, targetAngle);
                JointMotor2D motor = joint.motor;
                bool settled = !isGettingUp &&
                               Mathf.Abs(error) <= idleAngleDeadZone &&
                               Mathf.Abs(joint.attachedRigidbody.angularVelocity) <= idleAngularVelocityDeadZone;
                motor.motorSpeed = settled
                    ? 0f
                    : Mathf.Clamp(
                        error * poseSpeedGain * speedMultiplier,
                        -maximumMotorSpeed * speedMultiplier,
                        maximumMotorSpeed * speedMultiplier);
                motor.maxMotorTorque = torque * ResolveJointTorqueMultiplier(item.Role);
                joint.motor = motor;
                joint.useMotor = true;
            }
        }

        private float ResolveBalanceOffset(RagdollRigController2D.JointRuntime item)
        {
            if (!useLimbCounterBalance || !item.IsUpperLimb) return 0f;

            switch (item.Role)
            {
                case RagdollRigController2D.JointRole.Arm:
                    return Mathf.Clamp(
                        smoothedTorsoBalanceAngle * armCounterBalanceResponse,
                        -maximumArmCounterBalanceAngle,
                        maximumArmCounterBalanceAngle);
                case RagdollRigController2D.JointRole.Leg:
                    return Mathf.Clamp(
                        smoothedTorsoBalanceAngle * legStabilizationResponse,
                        -maximumLegStabilizationAngle,
                        maximumLegStabilizationAngle);
                default:
                    return 0f;
            }
        }

        private float ResolveJointTorqueMultiplier(RagdollRigController2D.JointRole role)
        {
            switch (role)
            {
                case RagdollRigController2D.JointRole.Arm:
                    return isGettingUp ? getUpArmTorqueMultiplier : standingArmTorqueMultiplier;
                case RagdollRigController2D.JointRole.Leg:
                    return isGettingUp ? getUpLegTorqueMultiplier : standingLegTorqueMultiplier;
                default:
                    return 1f;
            }
        }
        private void ApplyBalance(Rigidbody2D torso, float torque)
        {
            float error = Mathf.DeltaAngle(torso.rotation, 0f);
            RagdollProfile profile = profiles.ActiveProfile;
            float damping = profile != null ? profile.JointDamping : balanceDamping;
            torque *= ResolveOneLegStrength(oneLegBalanceStrength);

            if (!isGettingUp &&
                Mathf.Abs(error) <= idleAngleDeadZone &&
                Mathf.Abs(torso.angularVelocity) <= idleAngularVelocityDeadZone)
            {
                return;
            }

            float multiplier = isGettingUp ? standUpSpeed : 1f;
            torso.AddTorque(
                error * torque * multiplier -
                torso.angularVelocity * damping * multiplier,
                ForceMode2D.Force);
        }

        private void SetMotors(bool enabled)
        {
            rig.SetAllMotors(enabled);
        }

        private void OnValidate()
        {
            standUpDelay = Mathf.Max(0f, standUpDelay);
            standUpSpeed = Mathf.Clamp(standUpSpeed, .25f, 5f);
            standingMotorTorque = Mathf.Max(0f, standingMotorTorque);
            getUpMotorTorque = Mathf.Max(0f, getUpMotorTorque);
            poseSpeedGain = Mathf.Max(0f, poseSpeedGain);
            maximumMotorSpeed = Mathf.Max(0f, maximumMotorSpeed);
            standingBalanceTorque = Mathf.Max(0f, standingBalanceTorque);
            getUpBalanceTorque = Mathf.Max(0f, getUpBalanceTorque);
            balanceDamping = Mathf.Max(0f, balanceDamping);
            getUpLiftForce = Mathf.Max(0f, getUpLiftForce);
            maximumGetUpVelocity = Mathf.Max(0f, maximumGetUpVelocity);
            headUprightTorque = Mathf.Max(0f, headUprightTorque);
            headUprightDamping = Mathf.Max(0f, headUprightDamping);
            maximumHeadUprightTorque = Mathf.Max(0f, maximumHeadUprightTorque);
            limbBalanceResponseSpeed = Mathf.Max(0f, limbBalanceResponseSpeed);
            brokenLegFallDuration = Mathf.Max(0f, brokenLegFallDuration);
            brokenLegRecoveryDelay = Mathf.Max(0f, brokenLegRecoveryDelay);
            brokenLegFallTorque = Mathf.Max(0f, brokenLegFallTorque);
            oneLegMotorStrength = Mathf.Clamp01(oneLegMotorStrength);
            oneLegBalanceStrength = Mathf.Clamp01(oneLegBalanceStrength);
            oneLegLiftStrength = Mathf.Clamp01(oneLegLiftStrength);
            oneLegHeadStrength = Mathf.Clamp01(oneLegHeadStrength);
        }
    }
}
