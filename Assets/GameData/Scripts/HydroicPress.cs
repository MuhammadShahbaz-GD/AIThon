using System;
using KickTheBuddy.Physics;
using KickTheBuddy.VFX;
using UnityEngine;
using UnityEngine.EventSystems;

namespace KickTheBuddy.Gameplay
{
    /// <summary>Reusable tap-activated hydraulic press with a deterministic Physics2D crush cycle.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(SpriteRenderer))]
    public sealed class HydroicPress : MonoBehaviour
    {
        private enum PressState { Idle, Descending, Holding, Returning, Delay }

        [Header("Authored References")]
        [SerializeField] private Rigidbody2D movingBody;
        [Tooltip("Root moved by player dragging. Defaults to this press head's parent machine root.")]
        [SerializeField] private Transform movableRoot;
        [SerializeField] private SpriteRenderer tapSurface;
        [SerializeField] private Collider2D strikeCollider;
        [SerializeField] private RagdollAttackManager2D attack;
        [SerializeField] private ParticleSystem impactParticles;
        [SerializeField] private Camera inputCamera;
        [SerializeField] private CameraShake2D cameraShake;

        [Header("Press Cycle")]
        [Tooltip("Master multiplier for every phase of the press cycle.")]
        [Range(.25f, 4f)] [SerializeField] private float pressIterationSpeed = 1f;
        [Tooltip("Maximum downward travel from the authored top position.")]
        [Min(.1f)] [SerializeField] private float maximumBottomOffset = 2.9f;
        [Min(.1f)] [SerializeField] private float downwardSpeed = 18f;
        [Min(.1f)] [SerializeField] private float returnSpeed = 2.2f;
        [Min(0f)] [SerializeField] private float bottomHoldTime = .22f;
        [Min(0f)] [SerializeField] private float iterationDelay = .32f;
        [Tooltip("One tap starts continuous crushing. Disable for exactly one crush per tap.")]
        [SerializeField] private bool loopAfterActivation = true;

        [Header("Free Repositioning")]
        [SerializeField] private bool allowDragReposition = true;
        [Min(0f)] [SerializeField] private float dragThresholdPixels = 12f;

        [Header("Crush Impact")]
        [Min(0f)] [SerializeField] private float fixedDamage = 220f;
        [Min(0f)] [SerializeField] private float limbDownwardImpulse = 34f;
        [Min(0f)] [SerializeField] private float wholeBodyDownwardVelocity = 7f;
        [Min(0f)] [SerializeField] private float knockoutDuration = .55f;
        [Min(0f)] [SerializeField] private float shakeAmplitude = .16f;
        [Min(0f)] [SerializeField] private float shakeDuration = .22f;

        private PressState state;
        private Vector2 topPosition;
        private float phaseTimer;
        private bool inputEnabled;
        private bool loopActive;
        private int impactedRagdollId;
        private bool pointerHeld;
        private bool pointerDragging;
        private int activePointerId = -1;
        private Vector2 pointerDownScreen;
        private Vector3 dragWorldOffset;

        public float PressIterationSpeed => pressIterationSpeed;
        public float MaximumBottomOffset => maximumBottomOffset;
        public Vector2 MachinePosition => movableRoot != null ? movableRoot.position : transform.position;
        public bool IsActivated => loopActive || state != PressState.Idle;
        public bool IsDescending => state == PressState.Descending;
        public event Action Activated;
        public event Action<RagdollController, Vector2> CrushLanded;

        private void Awake()
        {
            if (movingBody == null) movingBody = GetComponent<Rigidbody2D>();
            if (movableRoot == null) movableRoot = transform.parent != null ? transform.parent : transform;
            if (tapSurface == null) tapSurface = GetComponent<SpriteRenderer>();
            if (inputCamera == null) inputCamera = Camera.main;
            if (cameraShake == null && inputCamera != null) cameraShake = inputCamera.GetComponent<CameraShake2D>();
            topPosition = movingBody != null ? movingBody.position : (Vector2)transform.position;
            ConfigureBody();
            attack?.Configure(RagdollAttackType.Custom, fixedDamage, 0f, 0f, fixedDamage);
            attack?.SetDamageEnabled(false);
            if (impactParticles != null)
                impactParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void OnEnable()
        {
            if (movingBody != null) topPosition = movingBody.position;
            ResetPress();

            Activate();
        }

        private void Update()
        {
            if (!inputEnabled) return;
            if (Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    Touch touch = Input.GetTouch(i);
                    if (touch.phase == TouchPhase.Began && !pointerHeld && !IsPointerOverUI(touch.fingerId))
                        BeginPointer(touch.position, touch.fingerId);
                    else if (pointerHeld && touch.fingerId == activePointerId)
                    {
                        if (touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary)
                            ContinuePointer(touch.position);
                        else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                            EndPointer(touch.phase == TouchPhase.Ended);
                    }
                }
                return;
            }
            if (Input.GetMouseButtonDown(0) && !IsPointerOverUI(-1)) BeginPointer(Input.mousePosition, -1);
            if (pointerHeld && activePointerId == -1 && Input.GetMouseButton(0)) ContinuePointer(Input.mousePosition);
            if (pointerHeld && activePointerId == -1 && Input.GetMouseButtonUp(0)) EndPointer(true);
        }

        private void FixedUpdate()
        {
            if (movingBody == null || state == PressState.Idle) return;
            float speedScale = Mathf.Max(.25f, pressIterationSpeed);
            switch (state)
            {
                case PressState.Descending:
                    MoveTowards(topPosition + Vector2.down * maximumBottomOffset, downwardSpeed * speedScale,
                        PressState.Holding, bottomHoldTime / speedScale);
                    break;
                case PressState.Holding:
                    TickTimer(PressState.Returning);
                    break;
                case PressState.Returning:
                    MoveTowards(topPosition, returnSpeed * speedScale, PressState.Delay, iterationDelay / speedScale);
                    break;
                case PressState.Delay:
                    if (TickTimer(loopActive ? PressState.Descending : PressState.Idle) && loopActive)
                        BeginDescent();
                    break;
            }
        }

        public void SetInputEnabled(bool value)
        {
            inputEnabled = value;
            enabled = value;
            if (!value) ResetPress();
        }

        public void Activate()
        {
            if (!inputEnabled || state != PressState.Idle) return;
            loopActive = loopAfterActivation;
            BeginDescent();
            Activated?.Invoke();
        }

        public void StopAndReset()
        {
            loopActive = false;
            ResetPress();
        }

        public void SetMachineWorldPosition(Vector2 worldPosition)
        {
            if (movableRoot == null) return;
            Vector3 oldPosition = movableRoot.position;
            Vector3 targetPosition = new Vector3(worldPosition.x, worldPosition.y, oldPosition.z);
            movableRoot.position = targetPosition;
            topPosition += (Vector2)(targetPosition - oldPosition);
        }

        private void BeginPointer(Vector2 screenPoint, int pointerId)
        {
            if (tapSurface == null || inputCamera == null || movableRoot == null) return;
            Vector3 world = ScreenToMachinePlane(screenPoint);
            if (!tapSurface.bounds.Contains(new Vector3(world.x, world.y, tapSurface.bounds.center.z))) return;
            pointerHeld = true;
            pointerDragging = false;
            activePointerId = pointerId;
            pointerDownScreen = screenPoint;
            dragWorldOffset = movableRoot.position - world;
        }

        private void ContinuePointer(Vector2 screenPoint)
        {
            if (!pointerHeld || !allowDragReposition || movableRoot == null) return;
            float threshold = dragThresholdPixels * dragThresholdPixels;
            if (!pointerDragging && (screenPoint - pointerDownScreen).sqrMagnitude < threshold) return;
            pointerDragging = true;
            Vector3 targetPosition = ScreenToMachinePlane(screenPoint) + dragWorldOffset;
            SetMachineWorldPosition(targetPosition);
        }

        private void EndPointer(bool allowTap)
        {
            bool shouldActivate = allowTap && !pointerDragging;
            CancelPointer();
            if (shouldActivate) Activate();
        }

        private void CancelPointer()
        {
            pointerHeld = false;
            pointerDragging = false;
            activePointerId = -1;
        }

        private Vector3 ScreenToMachinePlane(Vector2 screenPoint)
        {
            float depth = Mathf.Abs(inputCamera.transform.position.z - movableRoot.position.z);
            return inputCamera.ScreenToWorldPoint(new Vector3(screenPoint.x, screenPoint.y, depth));
        }

        private void BeginDescent()
        {
            impactedRagdollId = 0;
            state = PressState.Descending;
            attack?.Configure(RagdollAttackType.Custom, fixedDamage, 0f, 0f, fixedDamage);
            attack?.SetDamageEnabled(true);
        }

        private void MoveTowards(Vector2 target, float speed, PressState next, float nextTimer)
        {
            Vector2 position = Vector2.MoveTowards(movingBody.position, target, speed * Time.fixedDeltaTime);
            movingBody.MovePosition(position);
            if ((position - target).sqrMagnitude > .0001f) return;
            state = next;
            phaseTimer = Mathf.Max(0f, nextTimer);
            if (next != PressState.Descending) attack?.SetDamageEnabled(false);
        }

        private bool TickTimer(PressState next)
        {
            phaseTimer -= Time.fixedDeltaTime;
            if (phaseTimer > 0f) return false;
            state = next;
            return true;
        }

        private void OnCollisionEnter2D(Collision2D collision) => HandleCrushContact(collision);
        private void OnCollisionStay2D(Collision2D collision) => HandleCrushContact(collision);

        private void HandleCrushContact(Collision2D collision)
        {
            if (state != PressState.Descending || collision == null || collision.collider == null) return;
            Rigidbody2D hitBody = collision.collider.attachedRigidbody;
            RagdollController ragdoll = hitBody != null ? hitBody.GetComponentInParent<RagdollController>() : null;
            if (ragdoll == null || ragdoll.GetInstanceID() == impactedRagdollId) return;
            impactedRagdollId = ragdoll.GetInstanceID();

            Vector2 point = collision.contactCount > 0 ? collision.GetContact(0).point : hitBody.worldCenterOfMass;
            hitBody.AddForceAtPosition(Vector2.down * limbDownwardImpulse, point, ForceMode2D.Impulse);
            for (int i = 0; i < ragdoll.Parts.Count; i++)
            {
                Rigidbody2D partBody = ragdoll.Parts[i]?.Body;
                if (partBody == null || !partBody.simulated) continue;
                partBody.AddForce(Vector2.down * partBody.mass * wholeBodyDownwardVelocity, ForceMode2D.Impulse);
                partBody.WakeUp();
            }
            if (knockoutDuration > 0f) ragdoll.Knockout(knockoutDuration);
            if (impactParticles != null)
            {
                impactParticles.transform.position = point;
                impactParticles.Play(true);
            }
            cameraShake?.StartShake(shakeAmplitude, shakeDuration, 34f);
            CrushLanded?.Invoke(ragdoll, point);
        }

        private void ResetPress()
        {
            state = PressState.Idle;
            phaseTimer = 0f;
            loopActive = false;
            impactedRagdollId = 0;
            CancelPointer();
            attack?.SetDamageEnabled(false);
            if (movingBody != null)
            {
                movingBody.position = topPosition;
                movingBody.velocity = Vector2.zero;
                movingBody.angularVelocity = 0f;
            }
        }

        private void ConfigureBody()
        {
            if (movingBody == null) return;
            movingBody.bodyType = RigidbodyType2D.Kinematic;
            movingBody.gravityScale = 0f;
            movingBody.interpolation = RigidbodyInterpolation2D.Interpolate;
            movingBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            movingBody.useFullKinematicContacts = true;
            movingBody.constraints = RigidbodyConstraints2D.FreezeRotation;
        }

        private static bool IsPointerOverUI(int pointerId)
        {
            EventSystem current = EventSystem.current;
            return current != null && (pointerId >= 0
                ? current.IsPointerOverGameObject(pointerId)
                : current.IsPointerOverGameObject());
        }

        private void OnDisable() => ResetPress();

        private void OnValidate()
        {
            pressIterationSpeed = Mathf.Clamp(pressIterationSpeed, .25f, 4f);
            maximumBottomOffset = Mathf.Max(.1f, maximumBottomOffset);
            downwardSpeed = Mathf.Max(.1f, downwardSpeed);
            returnSpeed = Mathf.Max(.1f, returnSpeed);
            bottomHoldTime = Mathf.Max(0f, bottomHoldTime);
            iterationDelay = Mathf.Max(0f, iterationDelay);
            dragThresholdPixels = Mathf.Max(0f, dragThresholdPixels);
            fixedDamage = Mathf.Max(0f, fixedDamage);
            limbDownwardImpulse = Mathf.Max(0f, limbDownwardImpulse);
            wholeBodyDownwardVelocity = Mathf.Max(0f, wholeBodyDownwardVelocity);
            knockoutDuration = Mathf.Max(0f, knockoutDuration);
            shakeAmplitude = Mathf.Max(0f, shakeAmplitude);
            shakeDuration = Mathf.Max(0f, shakeDuration);
        }
    }
}
