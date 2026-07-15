using System;
using UnityEngine;

namespace KickTheBuddy.Physics
{
    /// <summary>Public interaction facade for grabbing, point forces, explosions, and impact routing.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
    public sealed class DamageReceiver2D : MonoBehaviour
    {
        [Header("Drag")]
        [SerializeField] private Camera inputCamera;
        [Min(0f)] [SerializeField] private float dragFrequency = 5f;
        [Range(0f, 1f)] [SerializeField] private float dragDampingRatio = 0.75f;
        [Min(0f)] [SerializeField] private float dragMaxForce = 1500f;
        [Header("Reactions")]
        [Min(0f)] [SerializeField] private float knockoutForceThreshold = 18f;
        [Min(0f)] [SerializeField] private float knockoutDuration = 2f;
        [Min(0f)] [SerializeField] private float explosionKnockoutDuration = 4f;

        private Rigidbody2D body;
        private RagdollController controller;
        private DismemberableLimb dismemberable;
        private RagdollElementalEffects elements;
        private TargetJoint2D dragJoint;
        private Vector2 pendingDragTarget;
        private bool dragging;

        public event Action<DamageReceiver2D, Vector2> Grabbed;
        public event Action<DamageReceiver2D, Vector2> Dragged;
        public event Action<DamageReceiver2D, Vector2> Released;
        public event Action<Rigidbody2D, Vector2, float> PointForceApplied;
        public event Action<Vector2, float, float> ExplosionApplied;
        public event Action<float> KnockoutRequested;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            controller = GetComponentInParent<RagdollController>();
            dismemberable = GetComponent<DismemberableLimb>();
            elements = controller != null ? controller.GetComponent<RagdollElementalEffects>() : null;
            if (inputCamera == null) inputCamera = Camera.main;
        }

        private void FixedUpdate()
        {
            if (dragging && dragJoint != null && dragJoint.enabled) dragJoint.target = pendingDragTarget;
        }

        private void OnMouseDown()
        {
            if (inputCamera == null || body == null) return;
            Vector2 point = ScreenToWorld(Input.mousePosition);
            dragJoint = GetComponent<TargetJoint2D>();
            if (dragJoint == null) dragJoint = gameObject.AddComponent<TargetJoint2D>();
            dragJoint.enabled = false;
            dragJoint.autoConfigureTarget = false;
            dragJoint.anchor = transform.InverseTransformPoint(point);
            dragJoint.target = point;
            dragJoint.frequency = dragFrequency;
            dragJoint.dampingRatio = dragDampingRatio;
            dragJoint.maxForce = dragMaxForce;
            dragJoint.enabled = true;
            pendingDragTarget = point;
            dragging = true;
            Grabbed?.Invoke(this, point);
        }

        private void OnMouseDrag()
        {
            if (!dragging || inputCamera == null) return;
            pendingDragTarget = ScreenToWorld(Input.mousePosition);
            Dragged?.Invoke(this, pendingDragTarget);
        }

        private void OnMouseUp()
        {
            if (!dragging) return;
            Vector2 point = pendingDragTarget;
            dragging = false;
            if (dragJoint != null) dragJoint.enabled = false;
            Released?.Invoke(this, point);
        }

        public void ApplyPointForce(Rigidbody2D hitLimb, Vector2 direction, float force)
        {
            if (hitLimb == null || force <= 0f) return;
            if (elements == null && controller != null) elements = controller.GetComponent<RagdollElementalEffects>();
            Vector2 normalized = direction.sqrMagnitude > 0f ? direction.normalized : Vector2.up;
            Vector2 impulse = normalized * force;
            hitLimb.AddForce(impulse, ForceMode2D.Impulse);
            DismemberableLimb target = hitLimb.GetComponent<DismemberableLimb>();
            target?.TakeDamage(force, impulse, hitLimb.worldCenterOfMass);
            elements?.NotifyImpact(target, force, hitLimb.worldCenterOfMass);
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
                DismemberableLimb targetLimb = target.GetComponent<DismemberableLimb>();
                targetLimb?.TakeDamage(force, impulse, target.worldCenterOfMass);
                elements?.NotifyImpact(targetLimb, force, target.worldCenterOfMass);
            }
            ExplosionApplied?.Invoke(explosionOrigin, radius, maxForce);
            RequestKnockout(explosionKnockoutDuration);
        }

        private void RequestKnockout(float duration)
        {
            KnockoutRequested?.Invoke(duration);
            if (controller != null) controller.Knockout(duration);
        }

        private Vector2 ScreenToWorld(Vector2 screen)
        {
            Vector3 world = inputCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -inputCamera.transform.position.z));
            return new Vector2(world.x, world.y);
        }

        private void OnDisable()
        {
            dragging = false;
            if (dragJoint != null) dragJoint.enabled = false;
        }
    }
}
