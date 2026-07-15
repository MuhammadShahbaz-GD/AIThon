using System;
using UnityEngine;

namespace KickTheBuddy.Physics
{
    /// <summary>
    /// Owns one body's local durability and detachable joints. Collision damage is supplied by the
    /// controller; sustained pulling is measured directly from HingeJoint2D reaction forces.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RagdollBreakableLimb : MonoBehaviour
    {
        private RagdollController owner;
        private Rigidbody2D body;
        private HingeJoint2D[] joints = Array.Empty<HingeJoint2D>();
        private bool[] originalJointEnabled = Array.Empty<bool>();
        private float baseHealth;
        private float stressThreshold;
        private float stressDamageRate;
        private float currentHealth;
        private bool broken;

        public Rigidbody2D Body => body;
        public float CurrentHealth => currentHealth;
        public float MaximumHealth => baseHealth;
        public bool IsBroken => broken;

        internal void Initialize(RagdollController controller, Rigidbody2D rigidbody, HingeJoint2D[] attachedJoints,
            float maximumHealth, float breakStressThreshold, float breakStressDamageRate)
        {
            owner = controller;
            body = rigidbody;
            joints = attachedJoints ?? Array.Empty<HingeJoint2D>();
            originalJointEnabled = new bool[joints.Length];
            for (int i = 0; i < joints.Length; i++) originalJointEnabled[i] = joints[i] != null && joints[i].enabled;
            baseHealth = Mathf.Max(1f, maximumHealth);
            currentHealth = baseHealth;
            stressThreshold = Mathf.Max(0f, breakStressThreshold);
            stressDamageRate = Mathf.Max(0f, breakStressDamageRate);
            broken = false;
        }

        private void FixedUpdate()
        {
            if (broken || owner == null) return;
            float strongestForce = 0f;
            for (int i = 0; i < joints.Length; i++)
            {
                HingeJoint2D joint = joints[i];
                if (joint != null && joint.enabled) strongestForce = Mathf.Max(strongestForce, joint.reactionForce.magnitude);
            }
            if (strongestForce <= stressThreshold) return;
            float damage = (strongestForce - stressThreshold) * stressDamageRate * Time.fixedDeltaTime;
            ApplyDamage(damage, body != null ? body.worldCenterOfMass : (Vector2)transform.position);
        }

        internal void ApplyDamage(float damage, Vector2 point)
        {
            if (broken || damage <= 0f) return;
            currentHealth = Mathf.Max(0f, currentHealth - damage);
            owner.NotifyLimbDamaged(this, damage, point);
            if (currentHealth <= 0f) Break(point);
        }

        internal void SetDurabilityMultiplier(float multiplier)
        {
            float fraction = baseHealth > 0f ? currentHealth / baseHealth : 1f;
            baseHealth = Mathf.Max(1f, owner.BaseLimbHealth * Mathf.Max(0.1f, multiplier));
            currentHealth = broken ? 0f : baseHealth * fraction;
        }

        internal void Restore()
        {
            broken = false;
            currentHealth = baseHealth;
            for (int i = 0; i < joints.Length; i++)
                if (joints[i] != null) joints[i].enabled = originalJointEnabled[i];
        }

        private void Break(Vector2 point)
        {
            broken = true;
            for (int i = 0; i < joints.Length; i++)
                if (joints[i] != null) joints[i].enabled = false;
            owner.NotifyLimbBroken(this, point);
        }
    }
}
