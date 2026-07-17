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

        [Header("Distance Break Safety")]
        [Tooltip("Break this parent connection if its two hinge anchors remain farther apart than the configured limit.")]
        [SerializeField] private bool breakWhenOverstretched = true;
        [Tooltip("Maximum world-space distance allowed between this limb hinge anchor and its connected-body anchor.")]
        [Min(.01f)] [SerializeField] private float maximumAnchorSeparation = .55f;
        [Tooltip("Consecutive physics steps outside the limit required before the limb breaks. Two filters one-frame solver spikes.")]
        [Range(1, 10)] [SerializeField] private int requiredOverstretchFixedSteps = 2;
        [Tooltip("Small outward impulse used when an overstretched limb separates.")]
        [Min(0f)] [SerializeField] private float overstretchBreakImpulse = 1.5f;
        [Tooltip("If disabled, grabbing never severs a limb; only impacts and ungrabbed structural overstretch can break it.")]
        [SerializeField] private bool allowDistanceBreakWhileDragging;

        [SerializeField] private Rigidbody2D body;
        [SerializeField] private HingeJoint2D parentJoint;
        [SerializeField] private RagdollController owner;
        private float maximumJointHealth;
        private bool severed;
        private int overstretchFixedStepCount;

        public event Action<DismemberableLimb, float, float, Vector2> Damaged;
        public event Action<DismemberableLimb, float, float, Vector2> DistanceLimitExceeded;
        public event Action<DismemberableLimb, Vector2, Vector2> Severing;
        public event Action<DismemberableLimb> Severed;

        public float JointHealth => jointHealth;
        public float MaximumJointHealth => maximumJointHealth;
        public bool CanBeSevered { get => canBeSevered; set => canBeSevered = value; }
        public bool IsSevered => severed;
        public Rigidbody2D Body => body;
        public bool BreakWhenOverstretched => breakWhenOverstretched;
        public float MaximumAnchorSeparation => maximumAnchorSeparation;
        public int RequiredOverstretchFixedSteps => requiredOverstretchFixedSteps;
        public bool AllowDistanceBreakWhileDragging => allowDistanceBreakWhileDragging;

        private void Awake() { maximumJointHealth = Mathf.Max(1f, jointHealth); }

        private void FixedUpdate()
        {
            if (EvaluateDistanceBreak()) return;
            if (!damageFromJointStress || severed || parentJoint == null || !parentJoint.enabled) return;
            // User dragging may create very large solver reaction forces. Those forces are not impacts
            // and must never consume structural health; damage is supplied by collisions/tools/status effects.
            if (owner != null && owner.IsUserDragging) return;
            float excessForce = parentJoint.reactionForce.magnitude - jointStressThreshold;
            if (excessForce > 0f)
                TakeDamage(excessForce * stressDamagePerForceSecond * Time.fixedDeltaTime, Vector2.zero, body.worldCenterOfMass);
        }

        /// <summary>
        /// Detects a failed 2D joint by measuring the error between its two physical anchors. This is
        /// independent from collision damage: the joint is allowed to flex normally, but cannot leave a
        /// visually connected limb suspended several world units away from its parent.
        /// </summary>
        private bool EvaluateDistanceBreak()
        {
            if (!breakWhenOverstretched || severed || !canBeSevered || body == null ||
                parentJoint == null || !parentJoint.enabled)
            {
                overstretchFixedStepCount = 0;
                return false;
            }

            if (!allowDistanceBreakWhileDragging && owner != null && owner.IsUserDragging)
            {
                overstretchFixedStepCount = 0;
                return false;
            }

            Vector2 limbAnchor = parentJoint.transform.TransformPoint(parentJoint.anchor);
            Rigidbody2D connectedBody = parentJoint.connectedBody;
            Vector2 connectedAnchor = connectedBody != null
                ? connectedBody.transform.TransformPoint(parentJoint.connectedAnchor)
                : parentJoint.connectedAnchor;
            Vector2 separationVector = limbAnchor - connectedAnchor;
            float limit = Mathf.Max(.01f, maximumAnchorSeparation);

            if (separationVector.sqrMagnitude <= limit * limit)
            {
                overstretchFixedStepCount = 0;
                return false;
            }

            overstretchFixedStepCount++;
            if (overstretchFixedStepCount < requiredOverstretchFixedSteps) return false;

            float separation = separationVector.magnitude;
            Vector2 direction = separation > .0001f ? separationVector / separation : Vector2.up;
            Vector2 breakPoint = (limbAnchor + connectedAnchor) * .5f;
            DistanceLimitExceeded?.Invoke(this, separation, limit, breakPoint);
            ForceSever(direction * overstretchBreakImpulse, breakPoint);
            return severed;
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
            overstretchFixedStepCount = 0;
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

        /// <summary>Editor/runtime authoring helper for the structural distance safety limit.</summary>
        public void ConfigureDistanceBreak(bool enabled, float maximumDistance, int fixedSteps,
            float breakImpulse, bool allowWhileDragging)
        {
            breakWhenOverstretched = enabled;
            maximumAnchorSeparation = Mathf.Max(.01f, maximumDistance);
            requiredOverstretchFixedSteps = Mathf.Clamp(fixedSteps, 1, 10);
            overstretchBreakImpulse = Mathf.Max(0f, breakImpulse);
            allowDistanceBreakWhileDragging = allowWhileDragging;
            overstretchFixedStepCount = 0;
        }

        internal void Initialize(RagdollController controller, Rigidbody2D authoredBody, HingeJoint2D authoredParentJoint, float health, float stressThreshold, float stressRate)
        {
            owner = controller;
            body = authoredBody;
            parentJoint = authoredParentJoint;
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
            maximumAnchorSeparation = Mathf.Max(.01f, maximumAnchorSeparation);
            requiredOverstretchFixedSteps = Mathf.Clamp(requiredOverstretchFixedSteps, 1, 10);
            overstretchBreakImpulse = Mathf.Max(0f, overstretchBreakImpulse);
        }
    }
}
