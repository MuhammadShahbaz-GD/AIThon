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
        [Min(.25f)] [SerializeField] private float strikesPerSecond = 2.25f;
        [Tooltip("Maximum readable rotation on either side of the aim direction.")]
        [Range(5f, 60f)] [SerializeField] private float windUpAngle = 30f;
        [Range(0f, 60f)] [SerializeField] private float followThroughAngle = 30f;
        [Range(.15f, .7f)] [SerializeField] private float strikePhase = .36f;
        [Min(.1f)] [SerializeField] private float rotationDrive = 26f;
        [Min(0f)] [SerializeField] private float rotationDamping = 2.2f;
        [Min(1f)] [SerializeField] private float maximumTorque = 220f;
        [Min(30f)] [SerializeField] private float maximumAngularSpeed = 720f;

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
            if (!swinging || tool == null || !tool.IsDragging || body == null) return;

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
            float torque = Mathf.Clamp(
                error * rotationDrive - body.angularVelocity * rotationDamping,
                -maximumTorque,
                maximumTorque);
            body.AddTorque(torque, ForceMode2D.Force);
            body.angularVelocity = Mathf.Clamp(body.angularVelocity, -maximumAngularSpeed, maximumAngularSpeed);
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
            if (selected != tool) return;
            swinging = true;
            swingStartedAt = Time.fixedTime;
            lastStrikeCycle = -1;
            if (body != null) body.WakeUp();
            SwingStarted?.Invoke(this);
        }

        private void HandleReleased(SandboxTool2D selected, Vector2 point)
        {
            if (selected == tool) StopSwing();
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
        }
    }
}
