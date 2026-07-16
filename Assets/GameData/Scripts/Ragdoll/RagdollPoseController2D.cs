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

        private RagdollController owner;
        private RagdollRigController2D rig;
        private RagdollProfileController2D profiles;
        private float fallenTime;
        private bool isGettingUp;
        private bool isDragging;
        private bool initialized;

        public bool IsGettingUp => isGettingUp;
        public bool IsDragging => isDragging;
        public float StandUpDelay => standUpDelay;

        internal void Initialize(RagdollController controller, RagdollRigController2D ragdollRig, RagdollProfileController2D profileController)
        {
            owner = controller;
            rig = ragdollRig;
            initialized = true;
        }

        private void FixedUpdate()
        {
            if (initialized) UpdateStandingAssist();
        }

        internal void SetDragging(bool value)
        {
            isDragging = value;
            if (!value) return;
            CancelRecovery();
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
                SetMotors(false);
                return;
            }

            float tilt = Mathf.Abs(Mathf.DeltaAngle(torso.rotation, 0f));
            if (!isGettingUp && tilt > fallenAngle)
            {
                fallenTime += Time.fixedDeltaTime;
                SetMotors(false);
                if (fallenTime < standUpDelay) return;
                isGettingUp = true;
            }

            if (isGettingUp)
            {
                ApplyPoseMotors(getUpMotorTorque);
                ApplyBalance(torso, getUpBalanceTorque);
                float velocityLimit = maximumGetUpVelocity * standUpSpeed;
                if (torso.velocity.y < velocityLimit)
                    torso.AddForce(Vector2.up * getUpLiftForce * standUpSpeed, ForceMode2D.Force);

                if (tilt <= standingAngle && Mathf.Abs(torso.angularVelocity) < 45f)
                    CancelRecovery();
                return;
            }

            fallenTime = 0f;
            ApplyPoseMotors(standingMotorTorque);
            ApplyBalance(torso, standingBalanceTorque);
        }

        private void ApplyPoseMotors(float torque)
        {
            RagdollProfile profile = profiles.ActiveProfile;
            if (profile != null)
                torque = profile.JointSpringForce * (isGettingUp ? 1.75f : 1f);

            float speedMultiplier = isGettingUp ? standUpSpeed : 1f;
            torque *= speedMultiplier;
            var joints = rig.Joints;

            for (int i = 0; i < joints.Count; i++)
            {
                RagdollRigController2D.JointRuntime item = joints[i];
                HingeJoint2D joint = item.Joint;
                if (joint == null || joint.attachedRigidbody == null) continue;

                float error = Mathf.DeltaAngle(joint.jointAngle, item.RestAngle);
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
                motor.maxMotorTorque = torque;
                joint.motor = motor;
                joint.useMotor = true;
            }
        }

        private void ApplyBalance(Rigidbody2D torso, float torque)
        {
            float error = Mathf.DeltaAngle(torso.rotation, 0f);
            RagdollProfile profile = profiles.ActiveProfile;
            float damping = profile != null ? profile.JointDamping : balanceDamping;

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
        }
    }
}
