using System;
using UnityEngine;

namespace KickTheBuddy.Gameplay
{
    /// <summary>
    /// Procedurally drives a held rod or stick through a readable wind-up/strike/recovery cycle.
    /// The TargetJoint2D still owns translation at the authored base grip; this component only
    /// applies rotational torque, so impacts remain genuine Physics2D collisions.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SandboxTool2D), typeof(Rigidbody2D))]
    public sealed class SandboxMeleeTool2D : MonoBehaviour
    {
        [Header("Authored References")]
        [SerializeField] private SandboxTool2D tool;
        [SerializeField] private Rigidbody2D body;
        [Tooltip("Usually the ragdoll torso. The strike direction is continuously aimed toward it.")]
        [SerializeField] private Transform attackTarget;

        [Header("Tool Orientation")]
        [Tooltip("Local direction from the grip toward the striking tip. Use Right for rods and Up for lollipops.")]
        [SerializeField] private Vector2 localTipAxis = Vector2.right;
        [SerializeField, Range(-180f, 180f)] private float aimOffsetDegrees;

        [Header("Beating Animation")]
        [Tooltip("Disabled keeps the tool fully physics-driven while held. Release throwing still works.")]
        [SerializeField] private bool enableBeatingAnimation;
        [Min(.25f)] [SerializeField] private float strikesPerSecond = 2.25f;
        [Tooltip("Maximum readable rotation on either side of the aim direction.")]
        [Range(5f, 60f)] [SerializeField] private float windUpAngle = 30f;
        [Range(0f, 60f)] [SerializeField] private float followThroughAngle = 30f;
        [Range(.15f, .7f)] [SerializeField] private float strikePhase = .36f;
        [Min(.1f)] [SerializeField] private float rotationDrive = 26f;
        [Min(0f)] [SerializeField] private float rotationDamping = 2.2f;
        [Min(1f)] [SerializeField] private float maximumTorque = 220f;
        [Min(30f)] [SerializeField] private float maximumAngularSpeed = 720f;
        [Tooltip("Minimum driven rotation while the tool is meaningfully away from its current swing target.")]
        [Min(0f)] [SerializeField] private float minimumSwingSpeed = 180f;
        [Tooltip("How quickly the swing motor recovers its requested speed after ragdoll contacts slow it down.")]
        [Min(30f)] [SerializeField] private float angularAcceleration = 3600f;
        [Tooltip("Angles inside this tolerance are allowed to settle without anti-stall drive.")]
        [Range(.1f, 5f)] [SerializeField] private float settledAngle = 1.5f;

        [Header("Release Throw")]
        [Tooltip("Ballistic impulse applied toward the ragdoll when this melee tool is released.")]
        [Min(1f)] [SerializeField] private float releaseThrowImpulse = 26f;
        [Range(-1f, 1f)] [SerializeField] private float releaseUpwardBias = .18f;
        [Min(0f)] [SerializeField] private float releaseSpin = 720f;

        private bool swinging;
        private float swingStartedAt;
        private int lastStrikeCycle = -1;

        public bool IsSwinging => swinging;
        public Transform AttackTarget => attackTarget;
        public event Action<SandboxMeleeTool2D> SwingStarted;
        public event Action<SandboxMeleeTool2D, Vector2> Strike;
        public event Action<SandboxMeleeTool2D> SwingStopped;

        private void Awake()
        {
            if (tool == null) tool = GetComponent<SandboxTool2D>();
            if (body == null) body = GetComponent<Rigidbody2D>();
        }

        private void OnEnable()
        {
            if (tool == null) tool = GetComponent<SandboxTool2D>();
            if (body == null) body = GetComponent<Rigidbody2D>();
            if (tool == null) return;
            tool.Grabbed += HandleGrabbed;
            tool.Released += HandleReleased;
        }

        private void OnDisable()
        {
            if (tool != null)
            {
                tool.Grabbed -= HandleGrabbed;
                tool.Released -= HandleReleased;
            }
            StopSwing();
        }

        private void FixedUpdate()
        {
            if (!enableBeatingAnimation || !swinging || tool == null || !tool.IsDragging || body == null) return;

            float elapsed = Mathf.Max(0f, Time.fixedTime - swingStartedAt);
            float cyclePosition = elapsed * strikesPerSecond;
            int cycle = Mathf.FloorToInt(cyclePosition);
            float phase = cyclePosition - cycle;

            if (phase >= strikePhase && lastStrikeCycle != cycle)
            {
                lastStrikeCycle = cycle;
                Strike?.Invoke(this, tool.GripWorldPosition);
            }

            Vector2 aim = attackTarget != null
                ? (Vector2)attackTarget.position - tool.GripWorldPosition
                : (Vector2)transform.TransformDirection(localTipAxis);
            if (aim.sqrMagnitude < .0001f) aim = Vector2.right;

            float aimAngle = Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg -
                             Mathf.Atan2(localTipAxis.y, localTipAxis.x) * Mathf.Rad2Deg +
                             aimOffsetDegrees;
            float swingOffset = ResolveSwingOffset(phase);
            float error = Mathf.DeltaAngle(body.rotation, aimAngle + swingOffset);

            // Torque alone can be completely cancelled when a long tool is touching several
            // ragdoll colliders. Keep the physical torque response, then servo angular velocity
            // toward the active swing target so every recovery and strike can escape contact.
            float torque = Mathf.Clamp(
                error * rotationDrive - body.angularVelocity * rotationDamping,
                -maximumTorque,
                maximumTorque);
            body.AddTorque(torque, ForceMode2D.Force);

            float requestedSpeed = Mathf.Clamp(error * rotationDrive, -maximumAngularSpeed, maximumAngularSpeed);
            if (Mathf.Abs(error) > settledAngle && Mathf.Abs(requestedSpeed) < minimumSwingSpeed)
                requestedSpeed = Mathf.Sign(error) * minimumSwingSpeed;
            body.angularVelocity = Mathf.MoveTowards(
                body.angularVelocity,
                requestedSpeed,
                angularAcceleration * Time.fixedDeltaTime);
            body.angularVelocity = Mathf.Clamp(body.angularVelocity, -maximumAngularSpeed, maximumAngularSpeed);
            body.WakeUp();
        }

        private float ResolveSwingOffset(float phase)
        {
            if (phase <= strikePhase)
            {
                float normalized = phase / Mathf.Max(.001f, strikePhase);
                float snap = 1f - Mathf.Pow(1f - normalized, 3f);
                return Mathf.Lerp(windUpAngle, -followThroughAngle, snap);
            }

            float recovery = (phase - strikePhase) / Mathf.Max(.001f, 1f - strikePhase);
            recovery = recovery * recovery * (3f - 2f * recovery);
            return Mathf.Lerp(-followThroughAngle, windUpAngle, recovery);
        }

        private void HandleGrabbed(SandboxTool2D selected, Vector2 point)
        {
            if (selected != tool || !enableBeatingAnimation) return;
            swinging = true;
            swingStartedAt = Time.fixedTime;
            lastStrikeCycle = -1;
            if (body != null) body.WakeUp();
            SwingStarted?.Invoke(this);
        }

        private void HandleReleased(SandboxTool2D selected, Vector2 point)
        {
            if (selected != tool) return;
            StopSwing();
            if (!tool.ThrewOnLastRelease)
                tool.ThrowAt(attackTarget, releaseThrowImpulse, releaseUpwardBias, releaseSpin);
        }

        private void StopSwing()
        {
            if (!swinging) return;
            swinging = false;
            SwingStopped?.Invoke(this);
        }

        private void OnValidate()
        {
            if (localTipAxis.sqrMagnitude < .001f) localTipAxis = Vector2.right;
            localTipAxis.Normalize();
            strikesPerSecond = Mathf.Max(.25f, strikesPerSecond);
            windUpAngle = Mathf.Clamp(windUpAngle, 5f, 60f);
            followThroughAngle = Mathf.Clamp(followThroughAngle, 0f, 60f);
            strikePhase = Mathf.Clamp(strikePhase, .15f, .7f);
            rotationDrive = Mathf.Max(.1f, rotationDrive);
            rotationDamping = Mathf.Max(0f, rotationDamping);
            maximumTorque = Mathf.Max(1f, maximumTorque);
            maximumAngularSpeed = Mathf.Max(30f, maximumAngularSpeed);
            minimumSwingSpeed = Mathf.Clamp(minimumSwingSpeed, 0f, maximumAngularSpeed);
            angularAcceleration = Mathf.Max(30f, angularAcceleration);
            settledAngle = Mathf.Clamp(settledAngle, .1f, 5f);
            releaseThrowImpulse = Mathf.Max(1f, releaseThrowImpulse);
            releaseUpwardBias = Mathf.Clamp(releaseUpwardBias, -1f, 1f);
            releaseSpin = Mathf.Max(0f, releaseSpin);
        }
    }
}
