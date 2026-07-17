using System;
using KickTheBuddy.Physics;
using UnityEngine;

namespace KickTheBuddy.Gameplay
{
    public enum SandboxToolKind
    {
        Lollipop,
        Jelly
    }

    /// <summary>
    /// Physics and interaction contract for a reusable level tool. Raw pointer input remains in
    /// SandboxToolInput2D, while damage remains owned by RagdollAttackManager2D/RagdollDamageManager.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D), typeof(TargetJoint2D))]
    public sealed class SandboxTool2D : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private SandboxToolKind kind;

        [Header("Authored References")]
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private TargetJoint2D dragJoint;
        [SerializeField] private FixedJoint2D stickyJoint;
        [SerializeField] private RagdollAttackManager2D attack;
        [SerializeField] private Transform visual;

        [Header("Drag Feel")]
        [Range(.5f, 12f)] [SerializeField] private float dragFrequency = 7.5f;
        [Range(0f, 1f)] [SerializeField] private float dragDamping = .92f;
        [Min(1f)] [SerializeField] private float dragMaximumForce = 1450f;
        [Range(.01f, .2f)] [SerializeField] private float targetSmoothTime = .025f;
        [Min(1f)] [SerializeField] private float maximumTargetSpeed = 65f;

        [Header("Jelly Stick And Slide")]
        [Min(0f)] [SerializeField] private float minimumStickSpeed = 2.5f;
        [Min(0f)] [SerializeField] private float stickDuration = .65f;
        [Tooltip("Prevents overlapping limb contacts from immediately pinning Jelly again after release.")]
        [Min(0f)] [SerializeField] private float reattachCooldown = .85f;
        [Min(0f)] [SerializeField] private float slideDrag = .45f;
        [Min(0f)] [SerializeField] private float slideDragDuration = 1.25f;
        [Range(0f, .45f)] [SerializeField] private float squashAmount = .24f;
        [Min(.1f)] [SerializeField] private float squashRecoverySpeed = 7f;

        private Vector2 pendingTarget;
        private Vector2 smoothedTarget;
        private Vector2 targetVelocity;
        private Vector3 visualRestScale;
        private Vector2 spawnPosition;
        private float spawnRotation;
        private float defaultDrag;
        private float stickRemaining;
        private float reattachCooldownRemaining;
        private float slideDragRemaining;
        private bool dragging;

        public SandboxToolKind Kind => kind;
        public Rigidbody2D Body => body;
        public RagdollAttackManager2D Attack => attack;
        public bool IsDragging => dragging;
        public bool IsStuck => stickyJoint != null && stickyJoint.enabled;

        public event Action<SandboxTool2D, Vector2> Grabbed;
        public event Action<SandboxTool2D, Vector2> Dragged;
        public event Action<SandboxTool2D, Vector2> Released;
        public event Action<SandboxTool2D, Rigidbody2D, float, Vector2> Impacted;
        public event Action<SandboxTool2D, Rigidbody2D, Vector2> Stuck;
        public event Action<SandboxTool2D> Detached;

        private void Awake()
        {
            if (body == null) body = GetComponent<Rigidbody2D>();
            if (dragJoint == null) dragJoint = GetComponent<TargetJoint2D>();
            if (attack == null) attack = GetComponent<RagdollAttackManager2D>();
            if (visual == null) visual = transform;

            spawnPosition = body != null ? body.position : (Vector2)transform.position;
            spawnRotation = body != null ? body.rotation : transform.eulerAngles.z;
            defaultDrag = body != null ? body.drag : 0f;
            visualRestScale = visual.localScale;

            if (dragJoint != null)
            {
                dragJoint.autoConfigureTarget = false;
                dragJoint.enabled = false;
            }
            if (stickyJoint != null) stickyJoint.enabled = false;
        }

        private void Update()
        {
            if (visual == null || visual.localScale == visualRestScale) return;
            visual.localScale = Vector3.Lerp(visual.localScale, visualRestScale,
                1f - Mathf.Exp(-squashRecoverySpeed * Time.deltaTime));
            if ((visual.localScale - visualRestScale).sqrMagnitude < .00001f)
                visual.localScale = visualRestScale;
        }

        private void FixedUpdate()
        {
            if (dragging && dragJoint != null && dragJoint.enabled)
            {
                smoothedTarget = Vector2.SmoothDamp(smoothedTarget, pendingTarget, ref targetVelocity,
                    targetSmoothTime, maximumTargetSpeed, Time.fixedDeltaTime);
                dragJoint.target = smoothedTarget;
            }

            if (stickRemaining > 0f)
            {
                stickRemaining -= Time.fixedDeltaTime;
                if (stickRemaining <= 0f) ReleaseStick(true);
            }

            if (reattachCooldownRemaining > 0f)
                reattachCooldownRemaining -= Time.fixedDeltaTime;

            if (slideDragRemaining <= 0f || body == null) return;
            slideDragRemaining -= Time.fixedDeltaTime;
            if (slideDragRemaining <= 0f) body.drag = defaultDrag;
        }

        public bool BeginDrag(Vector2 worldPoint)
        {
            if (body == null || dragJoint == null || dragging) return false;
            ReleaseStick(false);
            slideDragRemaining = 0f;
            reattachCooldownRemaining = 0f;
            body.drag = defaultDrag;
            body.WakeUp();
            dragJoint.enabled = false;
            dragJoint.anchor = transform.InverseTransformPoint(worldPoint);
            dragJoint.target = worldPoint;
            dragJoint.frequency = dragFrequency;
            dragJoint.dampingRatio = dragDamping;
            dragJoint.maxForce = dragMaximumForce;
            dragJoint.enabled = true;
            pendingTarget = smoothedTarget = worldPoint;
            targetVelocity = Vector2.zero;
            dragging = true;
            Grabbed?.Invoke(this, worldPoint);
            return true;
        }

        public void UpdateDragTarget(Vector2 worldPoint)
        {
            if (!dragging) return;
            pendingTarget = worldPoint;
            Dragged?.Invoke(this, worldPoint);
        }

        public void EndDrag()
        {
            if (!dragging) return;
            dragging = false;
            if (dragJoint != null) dragJoint.enabled = false;
            Released?.Invoke(this, pendingTarget);
        }

        public bool OwnsCollider(Collider2D candidate) =>
            candidate != null && body != null && candidate.attachedRigidbody == body;

        public void ResetToSpawn()
        {
            EndDrag();
            ReleaseStick(false);
            if (body != null)
            {
                body.simulated = true;
                body.position = spawnPosition;
                body.rotation = spawnRotation;
                body.velocity = Vector2.zero;
                body.angularVelocity = 0f;
                body.drag = defaultDrag;
                body.WakeUp();
            }
            slideDragRemaining = 0f;
            if (visual != null) visual.localScale = visualRestScale;
        }

        /// <summary>Attempts the jelly-only temporary attachment used by collisions and deterministic tests.</summary>
        public bool TryStickTo(Rigidbody2D target, Vector2 worldPoint, float impactSpeed)
        {
            if (kind != SandboxToolKind.Jelly || dragging || IsStuck || reattachCooldownRemaining > 0f ||
                stickyJoint == null || target == null ||
                impactSpeed < minimumStickSpeed || target.GetComponent<RagdollPartHealth>() == null) return false;

            ReleaseStick(false);
            stickyJoint.enabled = false;
            stickyJoint.connectedBody = target;
            stickyJoint.autoConfigureConnectedAnchor = false;
            stickyJoint.anchor = transform.InverseTransformPoint(worldPoint);
            stickyJoint.connectedAnchor = target.transform.InverseTransformPoint(worldPoint);
            stickyJoint.enabled = true;
            stickRemaining = stickDuration;
            ApplySquash();
            Stuck?.Invoke(this, target, worldPoint);
            return true;
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            Rigidbody2D target = collision.collider != null ? collision.collider.attachedRigidbody : null;
            float speed = collision.relativeVelocity.magnitude;
            Vector2 point = collision.contactCount > 0 ? collision.GetContact(0).point :
                (body != null ? body.worldCenterOfMass : (Vector2)transform.position);
            Impacted?.Invoke(this, target, speed, point);
            if (kind == SandboxToolKind.Jelly)
            {
                ApplySquash();
                TryStickTo(target, point, speed);
            }
        }

        private void ApplySquash()
        {
            if (visual == null || squashAmount <= 0f) return;
            visual.localScale = new Vector3(
                visualRestScale.x * (1f + squashAmount),
                visualRestScale.y * (1f - squashAmount),
                visualRestScale.z);
        }

        private void ReleaseStick(bool beginSlide)
        {
            bool wasStuck = stickyJoint != null && stickyJoint.enabled;
            stickRemaining = 0f;
            if (stickyJoint != null)
            {
                stickyJoint.enabled = false;
                stickyJoint.connectedBody = null;
            }
            if (!wasStuck) return;

            if (beginSlide && body != null)
            {
                reattachCooldownRemaining = reattachCooldown;
                body.drag = slideDrag;
                slideDragRemaining = slideDragDuration;
                body.WakeUp();
            }
            Detached?.Invoke(this);
        }

        private void OnDisable()
        {
            EndDrag();
            ReleaseStick(false);
        }

        private void OnValidate()
        {
            dragFrequency = Mathf.Clamp(dragFrequency, .5f, 12f);
            dragDamping = Mathf.Clamp01(dragDamping);
            dragMaximumForce = Mathf.Max(1f, dragMaximumForce);
            targetSmoothTime = Mathf.Clamp(targetSmoothTime, .01f, .2f);
            maximumTargetSpeed = Mathf.Max(1f, maximumTargetSpeed);
            minimumStickSpeed = Mathf.Max(0f, minimumStickSpeed);
            stickDuration = Mathf.Max(0f, stickDuration);
            reattachCooldown = Mathf.Max(0f, reattachCooldown);
            slideDrag = Mathf.Max(0f, slideDrag);
            slideDragDuration = Mathf.Max(0f, slideDragDuration);
            squashAmount = Mathf.Clamp(squashAmount, 0f, .45f);
            squashRecoverySpeed = Mathf.Max(.1f, squashRecoverySpeed);
        }
    }
}
