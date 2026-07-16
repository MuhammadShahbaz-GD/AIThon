using System;
using UnityEngine;

namespace KickTheBuddy.Physics
{
    /// <summary>Public interaction facade for grabbing, point forces, explosions, and impact routing.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
    public sealed class DamageReceiver2D : MonoBehaviour
    {
        [Header("Drag Flexibility")]
        [Tooltip("Lower values feel softer and more flexible. 3.5 is smooth by default.")]
        [Range(0.5f, 12f)] [SerializeField] private float dragFrequency = 3.5f;
        [Tooltip("Higher damping removes wobble while retaining elastic movement.")]
        [Range(0f, 1f)] [SerializeField] private float dragDampingRatio = 0.9f;
        [Min(0f)] [SerializeField] private float dragMaxForce = 1200f;
        [Tooltip("Seconds used to smooth the pointer target before physics receives it.")]
        [Range(0.01f, 0.25f)] [SerializeField] private float targetSmoothTime = 0.055f;
        [Tooltip("Maximum pointer target speed after smoothing.")]
        [Min(1f)] [SerializeField] private float maximumTargetSpeed = 45f;
        [Header("Elastic Limits")]
        [Tooltip("Pointer-follow distance before the drag spring becomes softer. The pointer target itself is never clamped.")]
        [Min(0.05f)] [SerializeField] private float headStretchLimit = 0.55f;
        [Min(0.05f)] [SerializeField] private float armStretchLimit = 1.1f;
        [Min(0.05f)] [SerializeField] private float legStretchLimit = 0.85f;
        [Min(0.05f)] [SerializeField] private float defaultStretchLimit = 0.65f;
        [Range(0.1f, 1f)] [SerializeField] private float stretchedFrequencyMultiplier = 0.8f;
        [Header("Head Drag Assist")]
        [Tooltip("Head dragging must pull the mass of the complete ragdoll through the neck joint.")]
        [Range(1f, 3f)] [SerializeField] private float headForceMultiplier = 1.5f;
        [Range(1f, 2f)] [SerializeField] private float headFrequencyMultiplier = 1.15f;
        [Header("Reactions")]
        [Min(0f)] [SerializeField] private float knockoutForceThreshold = 18f;
        [Min(0f)] [SerializeField] private float knockoutDuration = 2f;
        [Min(0f)] [SerializeField] private float explosionKnockoutDuration = 4f;

        private Rigidbody2D body;
        private RagdollController controller;
        private DismemberableLimb dismemberable;
        private RagdollElementalEffects elements;
        private RagdollDamageManager damageManager;
        private TargetJoint2D dragJoint;
        private Vector2 pendingDragTarget;
        private Vector2 smoothedDragTarget;
        private Vector2 targetVelocity;
        private bool dragging;
        private float stretchLimit;
        private float partForceMultiplier = 1f;
        private float partFrequencyMultiplier = 1f;

        public event Action<DamageReceiver2D, Vector2> Grabbed;
        public event Action<DamageReceiver2D, Vector2> Dragged;
        public event Action<DamageReceiver2D, Vector2> Released;
        public event Action<Rigidbody2D, Vector2, float> PointForceApplied;
        public event Action<Vector2, float, float> ExplosionApplied;
        public event Action<float> KnockoutRequested;

        public Rigidbody2D Body => body;
        public RagdollPartHealth PartHealth => body != null ? body.GetComponent<RagdollPartHealth>() : null;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            controller = GetComponentInParent<RagdollController>();
            dismemberable = GetComponent<DismemberableLimb>();
            stretchLimit = ResolveStretchLimit(name);
            bool isHead = name.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0;
            partForceMultiplier = isHead ? headForceMultiplier : 1f;
            partFrequencyMultiplier = isHead ? headFrequencyMultiplier : 1f;
            RagdollPartHealth partHealth = GetComponent<RagdollPartHealth>();
            if (partHealth != null) partFrequencyMultiplier *= partHealth.Flexibility;
            elements = controller != null ? controller.GetComponent<RagdollElementalEffects>() : null;
            damageManager = controller != null ? controller.GetComponent<RagdollDamageManager>() : null;
        }

        private void FixedUpdate()
        {
            if (dragging && dragJoint != null && dragJoint.enabled)
            {
                smoothedDragTarget = Vector2.SmoothDamp(smoothedDragTarget, pendingDragTarget, ref targetVelocity,
                    targetSmoothTime, maximumTargetSpeed, Time.fixedDeltaTime);
                Vector2 grabbedPoint = body.transform.TransformPoint(dragJoint.anchor);
                float followDistance = Vector2.Distance(grabbedPoint, smoothedDragTarget);
                float stretch = Mathf.Clamp01((followDistance - stretchLimit) / stretchLimit);
                float elasticFrequency = Mathf.Lerp(1f, stretchedFrequencyMultiplier, stretch);
                dragJoint.frequency = dragFrequency * partFrequencyMultiplier * elasticFrequency;
                // Never clamp the pointer target: pulling the head must translate the complete body.
                dragJoint.target = smoothedDragTarget;
            }
        }

        public bool BeginDrag(Vector2 worldPoint)
        {
            if (body == null || dragging || (controller != null && controller.CurrentState == RagdollState.Frozen)) return false;
            dragJoint = GetComponent<TargetJoint2D>();
            if (dragJoint == null) dragJoint = gameObject.AddComponent<TargetJoint2D>();
            dragJoint.enabled = false;
            dragJoint.autoConfigureTarget = false;
            dragJoint.anchor = transform.InverseTransformPoint(worldPoint);
            dragJoint.target = worldPoint;
            dragJoint.frequency = dragFrequency * partFrequencyMultiplier;
            dragJoint.dampingRatio = dragDampingRatio;
            dragJoint.maxForce = dragMaxForce * partForceMultiplier;
            dragJoint.enabled = true;
            pendingDragTarget = smoothedDragTarget = worldPoint; targetVelocity = Vector2.zero;
            dragging = true;
            controller?.NotifyExternalDragStarted(body);
            Grabbed?.Invoke(this, worldPoint);
            return true;
        }

        public void UpdateDragTarget(Vector2 worldPoint)
        {
            if (!dragging) return;
            pendingDragTarget = worldPoint;
            Dragged?.Invoke(this, pendingDragTarget);
        }

        public void EndDrag()
        {
            if (!dragging) return;
            Vector2 point = pendingDragTarget;
            dragging = false;
            if (dragJoint != null) dragJoint.enabled = false;
            controller?.NotifyExternalDragEnded(body);
            Released?.Invoke(this, point);
        }

        public void ApplyPointForce(Rigidbody2D hitLimb, Vector2 direction, float force)
        {
            if (hitLimb == null || force <= 0f) return;
            if (elements == null && controller != null) elements = controller.GetComponent<RagdollElementalEffects>();
            Vector2 normalized = direction.sqrMagnitude > 0f ? direction.normalized : Vector2.up;
            Vector2 impulse = normalized * force;
            hitLimb.AddForce(impulse, ForceMode2D.Impulse);
            if (damageManager == null && controller != null) damageManager = controller.GetComponent<RagdollDamageManager>();
            damageManager?.ApplyDirectDamage(hitLimb, force, force, impulse, hitLimb.worldCenterOfMass);
            PointForceApplied?.Invoke(hitLimb, normalized, force);
            if (force >= knockoutForceThreshold) RequestKnockout(knockoutDuration);
        }

        public void ApplyExplosionForce(Vector2 explosionOrigin, float radius, float maxForce)
        {
            if (controller == null || radius <= 0f || maxForce <= 0f) return;
            if (elements == null) elements = controller.GetComponent<RagdollElementalEffects>();
            var parts = controller.Parts;
            for (int i = 0; i < parts.Count; i++)
            {
                Rigidbody2D target = parts[i].Body;
                if (target == null) continue;
                Vector2 offset = target.worldCenterOfMass - explosionOrigin;
                float distance = offset.magnitude;
                if (distance > radius) continue;
                float force = maxForce * (1f - distance / radius);
                Vector2 direction = distance > 0.001f ? offset / distance : Vector2.up;
                Vector2 impulse = direction * force;
                target.AddForceAtPosition(impulse, target.worldCenterOfMass, ForceMode2D.Impulse);
                if (damageManager == null) damageManager = controller.GetComponent<RagdollDamageManager>();
                damageManager?.ApplyDirectDamage(target, force, force, impulse, target.worldCenterOfMass);
            }
            ExplosionApplied?.Invoke(explosionOrigin, radius, maxForce);
            RequestKnockout(explosionKnockoutDuration);
        }

        private void RequestKnockout(float duration)
        {
            KnockoutRequested?.Invoke(duration);
            if (controller != null) controller.Knockout(duration);
        }

        private float ResolveStretchLimit(string partName)
        {
            string value = partName.ToLowerInvariant();
            if (value.Contains("head")) return headStretchLimit;
            if (value.Contains("arm")) return armStretchLimit;
            if (value.Contains("leg")) return legStretchLimit;
            return defaultStretchLimit;
        }

        private void OnDisable()
        {
            if (dragging) controller?.NotifyExternalDragEnded(body);
            dragging = false;
            if (dragJoint != null) dragJoint.enabled = false;
        }

        private void OnValidate()
        {
            headStretchLimit = Mathf.Max(.05f, headStretchLimit); armStretchLimit = Mathf.Max(.05f, armStretchLimit);
            legStretchLimit = Mathf.Max(.05f, legStretchLimit); defaultStretchLimit = Mathf.Max(.05f, defaultStretchLimit);
            targetSmoothTime = Mathf.Clamp(targetSmoothTime, .01f, .25f); maximumTargetSpeed = Mathf.Max(1f, maximumTargetSpeed);
            headForceMultiplier = Mathf.Clamp(headForceMultiplier, 1f, 3f);
            headFrequencyMultiplier = Mathf.Clamp(headFrequencyMultiplier, 1f, 2f);
        }
    }
}
