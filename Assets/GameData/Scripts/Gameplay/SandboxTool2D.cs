using System;
using KickTheBuddy.Physics;
using UnityEngine;

namespace KickTheBuddy.Gameplay
{
    public enum SandboxToolKind
    {
        Lollipop,
        Jelly,
        CandyStick,
        ChocolateBar,
        GummyBear,
        LooseCandy,
        CandyJar,
        CandyGun
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
        [Tooltip("Optional authored pickup point. Melee tools use the bottom/base instead of the touched pixel.")]
        [SerializeField] private Transform dragGrip;

        [Header("Drag Feel")]
        [Range(.5f, 12f)] [SerializeField] private float dragFrequency = 7.5f;
        [Range(0f, 1f)] [SerializeField] private float dragDamping = .92f;
        [Min(1f)] [SerializeField] private float dragMaximumForce = 1450f;
        [Range(.01f, .2f)] [SerializeField] private float targetSmoothTime = .025f;
        [Min(1f)] [SerializeField] private float maximumTargetSpeed = 65f;

        [Header("Tap Auto Throw")]
        [Tooltip("Tap releases and launches this prop directly toward the authored ragdoll target.")]
        [SerializeField] private bool autoThrowOnTap;
        [SerializeField] private Transform throwTarget;
        [Min(0f)] [SerializeField] private float tapThrowImpulse = 9f;
        [Tooltip("Guarantees a destructive release throw even when older scene values use a weaker tap impulse.")]
        [Min(0f)] [SerializeField] private float minimumReleaseThrowImpulse = 22f;
        [Range(-1f, 1f)] [SerializeField] private float tapThrowUpwardBias = .12f;
        [Min(0f)] [SerializeField] private float tapThrowSpin = 360f;

        [Header("Ballistic Ragdoll Impact")]
        [Tooltip("Direct impulse applied at the first ragdoll limb hit after a release throw.")]
        [Min(0f)] [SerializeField] private float ballisticImpactImpulse = 45f;
        [Tooltip("Velocity change transferred to every connected ragdoll body so the complete character is launched.")]
        [Min(0f)] [SerializeField] private float ballisticWholeBodyVelocityChange = 8f;
        [Tooltip("Maximum time after release that the object retains its destructive ballistic hit.")]
        [Min(.1f)] [SerializeField] private float ballisticAttackDuration = 3f;
        [Tooltip("Guaranteed release speed for every tool, independent of Rigidbody mass.")]
        [Min(1f)] [SerializeField] private float minimumBallisticSpeed = 38f;
        [Tooltip("Safety cap for release speed.")]
        [Min(1f)] [SerializeField] private float maximumBallisticSpeed = 55f;
        [Tooltip("Guaranteed damage added to a valid ballistic hit.")]
        [Min(0f)] [SerializeField] private float ballisticBaseDamage = 20f;
        [Tooltip("Additional ballistic damage per unit of collision speed.")]
        [Min(0f)] [SerializeField] private float ballisticDamagePerSpeed = 4f;
        [Tooltip("Maximum raw damage allowed from one released-object hit.")]
        [Min(0f)] [SerializeField] private float ballisticMaximumDamage = 100f;

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
        private bool threwOnLastRelease;
        private bool ballisticAttackActive;
        private float ballisticAttackExpiresAt;
        private Vector2 ballisticDirection;

        public SandboxToolKind Kind => kind;
        public Rigidbody2D Body => body;
        public RagdollAttackManager2D Attack => attack;
        public bool IsDragging => dragging;
        public bool IsStuck => stickyJoint != null && stickyJoint.enabled;
        public bool HasAuthoredGrip => dragGrip != null;
        public bool AutoThrowOnTap => autoThrowOnTap;
        public bool ThrewOnLastRelease => threwOnLastRelease;
        public Transform ThrowTarget => throwTarget;
        public Vector2 CurrentDragTarget => pendingTarget;
        public Vector2 GripWorldPosition => dragGrip != null
            ? (Vector2)dragGrip.position
            : body != null ? body.worldCenterOfMass : (Vector2)transform.position;

        public event Action<SandboxTool2D, Vector2> Grabbed;
        public event Action<SandboxTool2D, Vector2> Dragged;
        public event Action<SandboxTool2D, Vector2> Released;
        public event Action<SandboxTool2D, Vector2> Tapped;
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
            dragJoint.anchor = dragGrip != null
                ? transform.InverseTransformPoint(dragGrip.position)
                : transform.InverseTransformPoint(worldPoint);
            dragJoint.target = worldPoint;
            dragJoint.frequency = dragFrequency;
            dragJoint.dampingRatio = dragDamping;
            dragJoint.maxForce = dragMaximumForce;
            dragJoint.enabled = true;
            pendingTarget = smoothedTarget = worldPoint;
            targetVelocity = Vector2.zero;
            dragging = true;
            threwOnLastRelease = false;
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
            threwOnLastRelease = TryAutoThrow();
            Released?.Invoke(this, pendingTarget);
        }

        /// <summary>
        /// Raised by the shared input owner when a press ends without a meaningful drag.
        /// Tap-capable tools such as the candy gun subscribe without polling input themselves.
        /// </summary>
        public void NotifyTap(Vector2 worldPoint)
        {
            Tapped?.Invoke(this, worldPoint);
            if (!threwOnLastRelease) TryAutoThrow();
        }

        public bool TryAutoThrow()
        {
            if (!autoThrowOnTap || body == null || throwTarget == null || tapThrowImpulse <= 0f)
                return false;
            ReleaseStick(false);
            Transform resolvedTarget = ResolveRagdollHead(throwTarget);
            Vector2 direction = (Vector2)resolvedTarget.position - body.worldCenterOfMass;
            direction.y += tapThrowUpwardBias * Mathf.Max(1f, direction.magnitude);
            if (direction.sqrMagnitude < .0001f) return false;
            float impulse = Mathf.Max(tapThrowImpulse, minimumReleaseThrowImpulse);
            LaunchBallistically(direction.normalized, impulse, tapThrowSpin);
            return true;
        }

        public bool ThrowAt(
            Transform target,
            float impulse,
            float upwardBias = .18f,
            float spin = 720f)
        {
            if (target == null || body == null || impulse <= 0f) return false;
            Transform resolvedTarget = ResolveRagdollHead(target);
            Vector2 direction = (Vector2)resolvedTarget.position - body.worldCenterOfMass;
            direction.y += Mathf.Clamp(upwardBias, -1f, 1f) * Mathf.Max(1f, direction.magnitude);
            if (direction.sqrMagnitude < .0001f) return false;
            ReleaseStick(false);
            LaunchBallistically(direction.normalized, impulse, spin);
            return true;
        }

        private static Transform ResolveRagdollHead(Transform requestedTarget)
        {
            if (requestedTarget == null) return null;
            RagdollController ragdoll = requestedTarget.GetComponentInParent<RagdollController>();
            if (ragdoll == null) return requestedTarget;

            for (int i = 0; i < ragdoll.Parts.Count; i++)
            {
                RagdollController.RagdollPart part = ragdoll.Parts[i];
                if (part != null && part.PartType == RagdollPartType.Head && part.Body != null)
                    return part.Body.transform;
            }
            return requestedTarget;
        }

        private void LaunchBallistically(Vector2 direction, float impulse, float spin)
        {
            body.velocity = Vector2.zero;
            // Tools translate ballistically without any authored spin animation. Contacts may
            // still rotate them naturally through Physics2D after impact.
            body.angularVelocity = 0f;
            float massAdjustedSpeed = impulse / Mathf.Max(.01f, body.mass);
            float launchSpeed = Mathf.Clamp(
                Mathf.Max(minimumBallisticSpeed, massAdjustedSpeed),
                minimumBallisticSpeed,
                Mathf.Max(minimumBallisticSpeed, maximumBallisticSpeed));
            body.velocity = direction * launchSpeed;
            if (attack != null && attack.AttackType != RagdollAttackType.Jelly)
            {
                attack.Configure(
                    attack.AttackType,
                    ballisticBaseDamage,
                    ballisticDamagePerSpeed,
                    0f,
                    ballisticMaximumDamage);
            }
            ballisticDirection = direction;
            ballisticAttackExpiresAt = Time.time + ballisticAttackDuration;
            ballisticAttackActive = true;
            body.WakeUp();
        }

        public void ConfigureAutoThrow(
            Transform target,
            float impulse,
            float upwardBias = .12f,
            float spin = 360f)
        {
            throwTarget = target;
            tapThrowImpulse = Mathf.Max(0f, impulse);
            tapThrowUpwardBias = Mathf.Clamp(upwardBias, -1f, 1f);
            tapThrowSpin = Mathf.Max(0f, spin);
            autoThrowOnTap = throwTarget != null && tapThrowImpulse > 0f;
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
            TryApplyBallisticRagdollImpact(target, point);
            if (kind == SandboxToolKind.Jelly)
            {
                ApplySquash();
                TryStickTo(target, point, speed);
            }
        }

        private void TryApplyBallisticRagdollImpact(Rigidbody2D hitBody, Vector2 point)
        {
            if (!ballisticAttackActive || Time.time > ballisticAttackExpiresAt)
            {
                ballisticAttackActive = false;
                return;
            }
            if (hitBody == null || hitBody.GetComponent<RagdollPartHealth>() == null) return;

            RagdollController ragdoll = hitBody.GetComponentInParent<RagdollController>();
            if (ragdoll == null) return;
            ballisticAttackActive = false;

            Vector2 direction = ballisticDirection.sqrMagnitude > .0001f
                ? ballisticDirection.normalized
                : body != null && body.velocity.sqrMagnitude > .0001f
                    ? body.velocity.normalized
                    : Vector2.up;
            hitBody.AddForceAtPosition(direction * ballisticImpactImpulse, point, ForceMode2D.Impulse);

            for (int i = 0; i < ragdoll.Parts.Count; i++)
            {
                RagdollController.RagdollPart part = ragdoll.Parts[i];
                if (part?.Body == null) continue;
                part.Body.AddForce(
                    direction * part.Body.mass * ballisticWholeBodyVelocityChange,
                    ForceMode2D.Impulse);
                part.Body.WakeUp();
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
            tapThrowImpulse = Mathf.Max(0f, tapThrowImpulse);
            minimumReleaseThrowImpulse = Mathf.Max(0f, minimumReleaseThrowImpulse);
            tapThrowUpwardBias = Mathf.Clamp(tapThrowUpwardBias, -1f, 1f);
            tapThrowSpin = Mathf.Max(0f, tapThrowSpin);
            ballisticImpactImpulse = Mathf.Max(0f, ballisticImpactImpulse);
            ballisticWholeBodyVelocityChange = Mathf.Max(0f, ballisticWholeBodyVelocityChange);
            ballisticAttackDuration = Mathf.Max(.1f, ballisticAttackDuration);
            minimumBallisticSpeed = Mathf.Max(1f, minimumBallisticSpeed);
            maximumBallisticSpeed = Mathf.Max(minimumBallisticSpeed, maximumBallisticSpeed);
            ballisticBaseDamage = Mathf.Max(0f, ballisticBaseDamage);
            ballisticDamagePerSpeed = Mathf.Max(0f, ballisticDamagePerSpeed);
            ballisticMaximumDamage = Mathf.Max(ballisticBaseDamage, ballisticMaximumDamage);
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
