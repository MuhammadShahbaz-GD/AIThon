using System;
using UnityEngine;

namespace KickTheBuddy.Physics
{
    /// <summary>Owns the structural integrity of the joint connecting this limb to its parent.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class DismemberableLimb : MonoBehaviour
    {
        [Min(1f)] [SerializeField] private float jointHealth = 100f;
        [SerializeField] private bool canBeSevered = true;
        [SerializeField] private GameObject breakParticlesPrefab;
        [Min(0f)] [SerializeField] private float jointStressThreshold = 450f;
        [Min(0f)] [SerializeField] private float stressDamagePerForceSecond = 0.02f;
        [Tooltip("Optional hazard mode. Leave disabled for normal characters so only collisions and attacks damage joints.")]
        [SerializeField] private bool damageFromJointStress;

        private Rigidbody2D body;
        private HingeJoint2D parentJoint;
        private RagdollController owner;
        private float maximumJointHealth;
        private bool severed;

        public event Action<DismemberableLimb, float, float, Vector2> Damaged;
        public event Action<DismemberableLimb, Vector2, Vector2> Severing;
        public event Action<DismemberableLimb> Severed;

        public float JointHealth => jointHealth;
        public float MaximumJointHealth => maximumJointHealth;
        public bool CanBeSevered { get => canBeSevered; set => canBeSevered = value; }
        public bool IsSevered => severed;
        public Rigidbody2D Body => body;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            parentJoint = GetComponent<HingeJoint2D>();
            owner = GetComponentInParent<RagdollController>();
            maximumJointHealth = Mathf.Max(1f, jointHealth);
        }

        private void FixedUpdate()
        {
            if (!damageFromJointStress || severed || parentJoint == null || !parentJoint.enabled) return;
            // User dragging may create very large solver reaction forces. Those forces are not impacts
            // and must never consume structural health; damage is supplied by collisions/tools/status effects.
            if (owner != null && owner.IsUserDragging) return;
            float excessForce = parentJoint.reactionForce.magnitude - jointStressThreshold;
            if (excessForce > 0f)
                TakeDamage(excessForce * stressDamagePerForceSecond * Time.fixedDeltaTime, Vector2.zero, body.worldCenterOfMass);
        }

        internal void SetStressDamageEnabled(bool enabled)
        {
            if (!enabled) return;
            // Kept as an explicit extension point for hazards that deliberately stress joints.
        }

        public void TakeDamage(float damage, Vector2 damageForce, Vector2 damagePoint)
        {
            if (severed || damage <= 0f) return;
            jointHealth = Mathf.Max(0f, jointHealth - damage);
            Damaged?.Invoke(this, damage, jointHealth, damagePoint);
            owner?.NotifyLimbDamaged(this, damage, damagePoint);
            if (jointHealth <= 0f && canBeSevered) SeverLimb(damageForce, damagePoint);
        }

        public void SeverLimb(Vector2 force, Vector2 point)
        {
            if (severed || !canBeSevered) return;
            severed = true;
            Vector2 anchor = parentJoint != null ? parentJoint.transform.TransformPoint(parentJoint.anchor) : (Vector2)transform.position;
            Severing?.Invoke(this, force, point);

            // Children stay parented beneath this transform, preserving the detached branch as one sub-ragdoll.
            if (parentJoint != null) { parentJoint.enabled = false; Destroy(parentJoint); }
            transform.SetParent(null, true);
            if (force.sqrMagnitude > 0f) body.AddForceAtPosition(force, point, ForceMode2D.Impulse);
            if (breakParticlesPrefab != null) Instantiate(breakParticlesPrefab, anchor, Quaternion.identity);

            owner?.NotifyLimbBroken(this, anchor);
            Severed?.Invoke(this);
        }

        public void ForceSever(Vector2 force, Vector2 point)
        {
            if (!canBeSevered) return;
            jointHealth = 0f;
            SeverLimb(force, point);
        }

        internal void Initialize(RagdollController controller, float health, float stressThreshold, float stressRate)
        {
            owner = controller;
            body = GetComponent<Rigidbody2D>();
            parentJoint = GetComponent<HingeJoint2D>();
            maximumJointHealth = Mathf.Max(1f, health);
            jointHealth = maximumJointHealth;
            jointStressThreshold = Mathf.Max(0f, stressThreshold);
            stressDamagePerForceSecond = Mathf.Max(0f, stressRate);
        }

        internal void SetDurabilityMultiplier(float baseHealth, float multiplier)
        {
            float fraction = maximumJointHealth > 0f ? jointHealth / maximumJointHealth : 1f;
            maximumJointHealth = Mathf.Max(1f, baseHealth * Mathf.Max(0.1f, multiplier));
            jointHealth = severed ? 0f : maximumJointHealth * fraction;
        }

        private void OnValidate()
        {
            jointHealth = Mathf.Max(0f, jointHealth);
            jointStressThreshold = Mathf.Max(0f, jointStressThreshold);
            stressDamagePerForceSecond = Mathf.Max(0f, stressDamagePerForceSecond);
        }
    }
}
