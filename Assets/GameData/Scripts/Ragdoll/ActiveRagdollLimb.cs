using System;
using UnityEngine;

namespace KickTheBuddy.Physics
{
    /// <summary>Drives one hinge toward an authored local rest angle using a damped motor.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class ActiveRagdollLimb : MonoBehaviour
    {
        [SerializeField] private HingeJoint2D joint;
        [SerializeField] private float targetRotation;
        [Min(0f)] [SerializeField] private float springForce = 8f;
        [Min(0f)] [SerializeField] private float damping = 0.25f;
        [Min(0f)] [SerializeField] private float maximumMotorSpeed = 220f;
        [Min(0f)] [SerializeField] private float maximumMotorTorque = 100f;

        private Rigidbody2D body;
        private bool externallyDriven;

        public event Action<ActiveRagdollLimb, float> MotorUpdated;
        public event Action<ActiveRagdollLimb, bool> MotorStateChanged;

        public Rigidbody2D Body => body;
        public HingeJoint2D Joint => joint;
        public float TargetRotation { get => targetRotation; set => targetRotation = value; }
        public float SpringForce { get => springForce; set => springForce = Mathf.Max(0f, value); }
        public float Damping { get => damping; set => damping = Mathf.Max(0f, value); }

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            if (joint == null) joint = GetComponent<HingeJoint2D>();
        }

        private void FixedUpdate()
        {
            if (externallyDriven || joint == null || !joint.enabled || !joint.useMotor) return;

            // DeltaAngle returns the shortest signed path in [-180, 180]. Damping removes energy
            // proportional to angular velocity, preventing the proportional spring from wobbling.
            float error = Mathf.DeltaAngle(joint.jointAngle, targetRotation);
            float desiredSpeed = (error * springForce) - (body.angularVelocity * damping);
            JointMotor2D motor = joint.motor;
            motor.motorSpeed = Mathf.Clamp(desiredSpeed, -maximumMotorSpeed, maximumMotorSpeed);
            motor.maxMotorTorque = maximumMotorTorque;
            joint.motor = motor;
            MotorUpdated?.Invoke(this, error);
        }

        public void SetMotorEnabled(bool enabled)
        {
            if (joint == null || joint.useMotor == enabled) return;
            joint.useMotor = enabled;
            MotorStateChanged?.Invoke(this, enabled);
        }

        internal void Configure(float restAngle, float force, float motorDamping, float torque)
        {
            targetRotation = restAngle;
            springForce = Mathf.Max(0f, force);
            damping = Mathf.Max(0f, motorDamping);
            maximumMotorTorque = Mathf.Max(0f, torque);
        }

        /// <summary>Prevents two pose controllers from writing the same motor in one physics tick.</summary>
        internal void SetExternallyDriven(bool value) { externallyDriven = value; }

        private void OnValidate()
        {
            springForce = Mathf.Max(0f, springForce);
            damping = Mathf.Max(0f, damping);
            maximumMotorSpeed = Mathf.Max(0f, maximumMotorSpeed);
            maximumMotorTorque = Mathf.Max(0f, maximumMotorTorque);
        }
    }
}
