using System;
using UnityEngine;

namespace KickTheBuddy.Physics
{
    /// <summary>Public interaction facade for grabbing, point forces, explosions, and impact routing.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
    public sealed class DamageReceiver2D : MonoBehaviour
    {
        // Common drag feel is owned and authored by RagdollInputManager.
        private float dragFrequency = 5f;
        private float dragDampingRatio = .9f;
        private float dragMaxForce = 1900f;
        private float targetSmoothTime = .03f;
        private float maximumTargetSpeed = 75f;
        private float headStretchLimit = .55f;
        private float armStretchLimit = 1.1f;
        private float legStretchLimit = .85f;
        private float defaultStretchLimit = .65f;
        private float stretchedFrequencyMultiplier = .9f;
        private float headForceMultiplier = 1.8f;
        private float headFrequencyMultiplier = 1.3f;
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

        internal void ApplyDragConfiguration(RagdollInputManager.DragConfiguration settings)
        {
            if (settings == null) return;

            dragFrequency = settings.Frequency;
            dragDampingRatio = settings.DampingRatio;
            dragMaxForce = settings.MaximumForce;
            targetSmoothTime = settings.TargetSmoothTime;
            maximumTargetSpeed = settings.MaximumTargetSpeed;
            headStretchLimit = settings.HeadStretchLimit;
            armStretchLimit = settings.ArmStretchLimit;
            legStretchLimit = settings.LegStretchLimit;
            defaultStretchLimit = settings.DefaultStretchLimit;
            stretchedFrequencyMultiplier = settings.StretchedFrequencyMultiplier;
            headForceMultiplier = settings.HeadForceMultiplier;
            headFrequencyMultiplier = settings.HeadFrequencyMultiplier;
            RefreshPartDragMultipliers();
        }

        private void RefreshPartDragMultipliers()
        {
            stretchLimit = ResolveStretchLimit(name);
            bool isHead = name.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0;
            partForceMultiplier = isHead ? headForceMultiplier : 1f;
            partFrequencyMultiplier = isHead ? headFrequencyMultiplier : 1f;
            RagdollPartHealth partHealth = GetComponent<RagdollPartHealth>();
            if (partHealth != null) partFrequencyMultiplier *= partHealth.Flexibility;
        }

        public Rigidbody2D Body => body;
        public RagdollPartHealth PartHealth => body != null ? body.GetComponent<RagdollPartHealth>() : null;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            controller = GetComponentInParent<RagdollController>();
            dismemberable = GetComponent<DismemberableLimb>();
            RefreshPartDragMultipliers();
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


    }
}
