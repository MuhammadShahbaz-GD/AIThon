using System;
using System.Collections.Generic;
using UnityEngine;

namespace KickTheBuddy.Physics
{
    /// <summary>Public interaction facade for one explicitly authored ragdoll main part.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D), typeof(TargetJoint2D))]
    public sealed class DamageReceiver2D : MonoBehaviour
    {
        private float dragFrequency = 5f;
        private float dragDampingRatio = .9f;
        private float dragMaxForce = 1900f;
        private bool directPointerTracking = true;
        private float directTrackingFrequency = 22f;
        private float directTrackingForcePerMass = 4000f;
        private float targetSmoothTime = .03f;
        private float maximumTargetSpeed = 75f;
        private float headStretchLimit = .55f;
        private float armStretchLimit = 1.1f;
        private float legStretchLimit = .85f;
        private float defaultStretchLimit = .65f;
        private float stretchedFrequencyMultiplier = .9f;
        private float headForceMultiplier = 1.8f;
        private float headFrequencyMultiplier = 1.3f;
        private float headDampingMultiplier = .55f;

        [Header("Authored References")]
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private RagdollController controller;
        [SerializeField] private DismemberableLimb dismemberable;
        [SerializeField] private RagdollElementalEffects elements;
        [SerializeField] private RagdollDamageManager damageManager;
        [SerializeField] private RagdollPartHealth partHealth;
        [SerializeField] private TargetJoint2D dragJoint;

        [Header("Reactions")]
        [Min(0f)] [SerializeField] private float knockoutForceThreshold = 18f;
        [Min(0f)] [SerializeField] private float knockoutDuration = 2f;
        [Min(0f)] [SerializeField] private float explosionKnockoutDuration = 4f;

        private Vector2 pendingDragTarget;
        private Vector2 smoothedDragTarget;
        private Vector2 targetVelocity;
        private bool dragging;
        private float stretchLimit;
        private float partForceMultiplier = 1f;
        private float partFrequencyMultiplier = 1f;
        private float partDampingMultiplier = 1f;

        public event Action<DamageReceiver2D, Vector2> Grabbed;
        public event Action<DamageReceiver2D, Vector2> Dragged;
        public event Action<DamageReceiver2D, Vector2> Released;
        public event Action<Rigidbody2D, Vector2, float> PointForceApplied;
        public event Action<Vector2, float, float> ExplosionApplied;
        public event Action<float> KnockoutRequested;

        public Rigidbody2D Body => body;
        public RagdollPartHealth PartHealth => partHealth;

        internal void Initialize(Rigidbody2D authoredBody, RagdollController owner,
            DismemberableLimb structuralLimb, RagdollDamageManager damage, RagdollElementalEffects elemental,
            RagdollPartHealth health, TargetJoint2D authoredDragJoint)
        {
            body = authoredBody;
            controller = owner;
            dismemberable = structuralLimb;
            damageManager = damage;
            partHealth = health;
            dragJoint = authoredDragJoint;
            elements = elemental;
            if (dragJoint != null)
            {
                dragJoint.autoConfigureTarget = false;
                dragJoint.enabled = false;
            }
            RefreshPartDragMultipliers();
            enabled = false;
        }

        internal void SetElementalEffects(RagdollElementalEffects value) => elements = value;

        internal void ApplyDragConfiguration(RagdollInputManager.DragConfiguration settings)
        {
            if (settings == null) return;
            dragFrequency = settings.Frequency;
            dragDampingRatio = settings.DampingRatio;
            dragMaxForce = settings.MaximumForce;
            directPointerTracking = settings.DirectPointerTracking;
            directTrackingFrequency = settings.DirectTrackingFrequency;
            directTrackingForcePerMass = settings.DirectTrackingForcePerMass;
            targetSmoothTime = settings.TargetSmoothTime;
            maximumTargetSpeed = settings.MaximumTargetSpeed;
            headStretchLimit = settings.HeadStretchLimit;
            armStretchLimit = settings.ArmStretchLimit;
            legStretchLimit = settings.LegStretchLimit;
            defaultStretchLimit = settings.DefaultStretchLimit;
            stretchedFrequencyMultiplier = settings.StretchedFrequencyMultiplier;
            headForceMultiplier = settings.HeadForceMultiplier;
            headFrequencyMultiplier = settings.HeadFrequencyMultiplier;
            headDampingMultiplier = settings.HeadDampingMultiplier;
            RefreshPartDragMultipliers();
        }

        private void RefreshPartDragMultipliers()
        {
            stretchLimit = ResolveStretchLimit(name);
            bool isHead = partHealth != null ? partHealth.PartType == RagdollPartType.Head :
                name.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0;
            partForceMultiplier = isHead ? headForceMultiplier : 1f;
            partFrequencyMultiplier = isHead ? headFrequencyMultiplier : 1f;
            partDampingMultiplier = isHead ? headDampingMultiplier : 1f;
            if (partHealth != null) partFrequencyMultiplier *= partHealth.Flexibility;
        }

        private void FixedUpdate()
        {
            if (!dragging || dragJoint == null || !dragJoint.enabled || body == null) return;
            if (directPointerTracking)
            {
                smoothedDragTarget = pendingDragTarget;
                targetVelocity = Vector2.zero;
            }
            else
            {
                smoothedDragTarget = Vector2.SmoothDamp(smoothedDragTarget, pendingDragTarget, ref targetVelocity,
                    targetSmoothTime, maximumTargetSpeed, Time.fixedDeltaTime);
            }

            Vector2 grabbedPoint = body.transform.TransformPoint(dragJoint.anchor);
            float followDistance = Vector2.Distance(grabbedPoint, smoothedDragTarget);
            float stretch = Mathf.Clamp01((followDistance - stretchLimit) / stretchLimit);
            float followFrequency = directPointerTracking
                ? Mathf.Max(dragFrequency, directTrackingFrequency)
                : dragFrequency;
            dragJoint.frequency = followFrequency * partFrequencyMultiplier *
                                  Mathf.Lerp(1f, stretchedFrequencyMultiplier, stretch);
            dragJoint.target = smoothedDragTarget;
        }

        public bool BeginDrag(Vector2 worldPoint)
        {
            if (body == null || dragJoint == null || dragging ||
                (controller != null && controller.CurrentState == RagdollState.Frozen)) return false;

            enabled = true;
            dragJoint.enabled = false;
            dragJoint.anchor = transform.InverseTransformPoint(worldPoint);
            dragJoint.target = worldPoint;
            float followFrequency = directPointerTracking
                ? Mathf.Max(dragFrequency, directTrackingFrequency)
                : dragFrequency;
            float followForce = directPointerTracking
                ? Mathf.Max(dragMaxForce, body.mass * directTrackingForcePerMass)
                : dragMaxForce;
            dragJoint.frequency = followFrequency * partFrequencyMultiplier;
            dragJoint.dampingRatio = dragDampingRatio * partDampingMultiplier;
            dragJoint.maxForce = followForce * partForceMultiplier;
            dragJoint.enabled = true;
            pendingDragTarget = smoothedDragTarget = worldPoint;
            targetVelocity = Vector2.zero;
            dragging = true;
            controller?.NotifyExternalDragStarted(body);
            Grabbed?.Invoke(this, worldPoint);
            return true;
        }

        public void UpdateDragTarget(Vector2 worldPoint)
        {
            if (!dragging) return;
            pendingDragTarget = worldPoint;
            if (directPointerTracking && dragJoint != null && dragJoint.enabled)
                dragJoint.target = worldPoint;
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
            enabled = false;
        }

        public void ApplyPointForce(Rigidbody2D hitLimb, Vector2 direction, float force)
        {
            if (hitLimb == null || force <= 0f) return;
            Vector2 normalized = direction.sqrMagnitude > 0f ? direction.normalized : Vector2.up;
            Vector2 impulse = normalized * force;
            hitLimb.AddForce(impulse, ForceMode2D.Impulse);
            damageManager?.ApplyDirectDamage(hitLimb, force, force, impulse, hitLimb.worldCenterOfMass);
            PointForceApplied?.Invoke(hitLimb, normalized, force);
            if (force >= knockoutForceThreshold) RequestKnockout(knockoutDuration);
        }

        public void ApplyExplosionForce(Vector2 explosionOrigin, float radius, float maxForce)
        {
            if (controller == null || radius <= 0f || maxForce <= 0f) return;
            IReadOnlyList<RagdollController.RagdollPart> parts = controller.Parts;
            for (int i = 0; i < parts.Count; i++)
            {
                Rigidbody2D target = parts[i].Body;
                if (target == null) continue;
                Vector2 offset = target.worldCenterOfMass - explosionOrigin;
                float distance = offset.magnitude;
                if (distance > radius) continue;
                float force = maxForce * (1f - distance / radius);
                Vector2 direction = distance > .001f ? offset / distance : Vector2.up;
                Vector2 impulse = direction * force;
                target.AddForceAtPosition(impulse, target.worldCenterOfMass, ForceMode2D.Impulse);
                damageManager?.ApplyDirectDamage(target, force, force, impulse, target.worldCenterOfMass);
            }
            ExplosionApplied?.Invoke(explosionOrigin, radius, maxForce);
            RequestKnockout(explosionKnockoutDuration);
        }

        private void RequestKnockout(float duration)
        {
            KnockoutRequested?.Invoke(duration);
            controller?.Knockout(duration);
        }

        private float ResolveStretchLimit(string partName)
        {
            if (partHealth != null)
            {
                if (partHealth.PartType == RagdollPartType.Head) return headStretchLimit;
                if (partHealth.PartType == RagdollPartType.Arm) return armStretchLimit;
                if (partHealth.PartType == RagdollPartType.Leg) return legStretchLimit;
                return defaultStretchLimit;
            }
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
