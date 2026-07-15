using System;
using System.Collections.Generic;
using UnityEngine;

namespace KickTheBuddy.Physics
{
    /// <summary>Attach to the common parent of every 2D ragdoll body.</summary>
    [DisallowMultipleComponent]
    public sealed class RagdollController : MonoBehaviour, IRagdollCollisionReceiver
    {
        [Serializable]
        public sealed class RagdollPart
        {
            [SerializeField] private string displayName;
            [SerializeField] private Rigidbody2D body;
            [SerializeField] private Collider2D[] colliders;
            [SerializeField] private HingeJoint2D[] hinges;
            public string DisplayName => displayName;
            public Rigidbody2D Body => body;
            public Collider2D[] Colliders => colliders;
            public HingeJoint2D[] Hinges => hinges;
            internal RagdollPart(Rigidbody2D value)
            {
                displayName = value.name; body = value;
                colliders = value.GetComponents<Collider2D>();
                hinges = value.GetComponents<HingeJoint2D>();
            }
        }

        private struct HingeDefaults
        {
            public HingeJoint2D Joint; public bool UseLimits; public JointAngleLimits2D Limits;
            public bool UseMotor; public JointMotor2D Motor; public float RestAngle;
        }
        private struct BodyDefaults { public Rigidbody2D Body; public RigidbodyConstraints2D Constraints; }

        [Header("Structure")]
        [SerializeField] private bool discoverPartsOnAwake = true;
        [SerializeField] private RagdollPart[] parts = Array.Empty<RagdollPart>();
        [Header("Drag Spring")]
        [SerializeField] private Camera inputCamera;
        [SerializeField] private LayerMask grabbableLayers = ~0;
        [Min(0f)] [SerializeField] private float dragMaxForce = 1500f;
        [Range(0f, 1f)] [SerializeField] private float dragDampingRatio = 0.75f;
        [Min(0.01f)] [SerializeField] private float dragFrequency = 5f;
        [Header("Damage and Combo")]
        [Min(1f)] [SerializeField] private float maximumHealth = 100f;
        [Min(0f)] [SerializeField] private float minimumImpactSpeed = 3.5f;
        [Min(0f)] [SerializeField] private float damagePerSpeed = 2.5f;
        [Min(0f)] [SerializeField] private float maximumDamagePerImpact = 35f;
        [Min(0f)] [SerializeField] private float comboTimeout = 1.25f;
        [Header("Recovery")]
        [Min(0f)] [SerializeField] private float reviveDelay = 4f;
        [Header("Standing and Get Up")]
        [Tooltip("Seconds the healthy ragdoll may remain fallen before the get-up assist starts.")]
        [Min(0f)] [SerializeField] private float standUpDelay = 2f;
        [Range(10f, 80f)] [SerializeField] private float fallenAngle = 45f;
        [Range(2f, 35f)] [SerializeField] private float standingAngle = 16f;
        [Min(0f)] [SerializeField] private float standingMotorTorque = 95f;
        [Min(0f)] [SerializeField] private float getUpMotorTorque = 220f;
        [Min(0f)] [SerializeField] private float poseSpeedGain = 8f;
        [Min(0f)] [SerializeField] private float maximumMotorSpeed = 220f;
        [Min(0f)] [SerializeField] private float standingBalanceTorque = 20f;
        [Min(0f)] [SerializeField] private float getUpBalanceTorque = 65f;
        [Min(0f)] [SerializeField] private float balanceDamping = 3f;
        [Min(0f)] [SerializeField] private float getUpLiftForce = 52f;
        [Min(0f)] [SerializeField] private float maximumGetUpVelocity = 2.5f;
        [SerializeField] private float currentHealth;
        [SerializeField] private int currentCombo;
        [SerializeField] private RagdollState currentState = RagdollState.Active;

        private readonly Dictionary<Rigidbody2D, RagdollPart> partByBody = new Dictionary<Rigidbody2D, RagdollPart>();
        private readonly List<HingeDefaults> hingeDefaults = new List<HingeDefaults>();
        private readonly List<BodyDefaults> bodyDefaults = new List<BodyDefaults>();
        private TargetJoint2D activeDragJoint;
        private Vector2 pendingTarget;
        private bool pointerHeld;
        private int activeTouchId = -1;
        private float lastDamageTime = float.NegativeInfinity;
        private float reviveAt = float.PositiveInfinity;
        private Rigidbody2D torsoBody;
        private float fallenTime;
        private bool isGettingUp;

        public event Action<float, Vector2> OnDamageTaken;
        public event Action OnCharacterKO;
        public event Action OnCharacterRevived;
        public IReadOnlyList<RagdollPart> Parts => parts;
        public float CurrentHealth => currentHealth;
        public float MaximumHealth => maximumHealth;
        public int CurrentCombo => currentCombo;
        public RagdollState CurrentState => currentState;
        public bool IsGettingUp => isGettingUp;
        public float StandUpDelay => standUpDelay;

        private void Awake()
        {
            if (inputCamera == null) inputCamera = Camera.main;
            InitializeParts();
            if (GetComponent<RagdollLifeVisuals>() == null)
                gameObject.AddComponent<RagdollLifeVisuals>();
            currentHealth = maximumHealth;
        }
        private void OnDisable() { ReleaseDrag(); }
        private void Update()
        {
            ReadPointerInput();
            if (currentCombo > 0 && Time.time - lastDamageTime >= comboTimeout) currentCombo = 0;
            if (currentState == RagdollState.KnockedOut && Time.time >= reviveAt) Revive();
        }
        private void FixedUpdate()
        {
            // Commit the sampled target on physics ticks so spring forces remain timestep-stable.
            if (activeDragJoint != null && activeDragJoint.enabled && pointerHeld)
                activeDragJoint.target = pendingTarget;
            UpdateStandingAssist();

        }

        public void InitializeParts()
        {
            if (discoverPartsOnAwake || parts == null || parts.Length == 0)
            {
                Rigidbody2D[] bodies = GetComponentsInChildren<Rigidbody2D>(true);
                parts = new RagdollPart[bodies.Length];
                for (int i = 0; i < bodies.Length; i++) parts[i] = new RagdollPart(bodies[i]);
            }
            partByBody.Clear(); hingeDefaults.Clear(); bodyDefaults.Clear(); torsoBody = null;
            foreach (RagdollPart part in parts)
            {
                if (part == null || part.Body == null || partByBody.ContainsKey(part.Body)) continue;
                partByBody.Add(part.Body, part);
                if (torsoBody == null && part.Body.name.IndexOf("torso", StringComparison.OrdinalIgnoreCase) >= 0)
                    torsoBody = part.Body;
                bodyDefaults.Add(new BodyDefaults { Body = part.Body, Constraints = part.Body.constraints });
                RagdollLimb relay = part.Body.GetComponent<RagdollLimb>();
                if (relay == null) relay = part.Body.gameObject.AddComponent<RagdollLimb>();
                relay.Initialize(this, part.Body);
                foreach (HingeJoint2D hinge in part.Hinges)
                {
                    if (hinge == null) continue;
                    hingeDefaults.Add(new HingeDefaults { Joint = hinge, UseLimits = hinge.useLimits,
                        Limits = hinge.limits, UseMotor = hinge.useMotor, Motor = hinge.motor,
                        RestAngle = hinge.jointAngle });
                }
            }
        }

        private void UpdateStandingAssist()
        {
            bool canStand = currentState == RagdollState.Active || currentState == RagdollState.Burning;
            if (!canStand || currentHealth <= 0f || torsoBody == null || pointerHeld)
            {
                fallenTime = 0f;
                isGettingUp = false;
                DisablePoseMotors();
                return;
            }

            float tilt = Mathf.Abs(Mathf.DeltaAngle(torsoBody.rotation, 0f));
            if (!isGettingUp && tilt > fallenAngle)
            {
                fallenTime += Time.fixedDeltaTime;
                DisablePoseMotors(); // Let the impact settle naturally before recovery begins.
                if (fallenTime < standUpDelay) return;
                isGettingUp = true;
            }

            if (isGettingUp)
            {
                ApplyPoseMotors(getUpMotorTorque);
                ApplyBalance(getUpBalanceTorque);
                if (torsoBody.velocity.y < maximumGetUpVelocity)
                    torsoBody.AddForce(Vector2.up * getUpLiftForce, ForceMode2D.Force);

                if (tilt <= standingAngle && Mathf.Abs(torsoBody.angularVelocity) < 45f)
                {
                    isGettingUp = false;
                    fallenTime = 0f;
                }
                return;
            }

            fallenTime = 0f;
            ApplyPoseMotors(standingMotorTorque);
            ApplyBalance(standingBalanceTorque);
        }

        private void ApplyPoseMotors(float torque)
        {
            for (int i = 0; i < hingeDefaults.Count; i++)
            {
                HingeDefaults item = hingeDefaults[i];
                if (item.Joint == null) continue;
                if (activeDragJoint != null && item.Joint.attachedRigidbody == activeDragJoint.attachedRigidbody) continue;
                float error = Mathf.DeltaAngle(item.Joint.jointAngle, item.RestAngle);
                JointMotor2D motor = item.Joint.motor;
                motor.motorSpeed = Mathf.Clamp(error * poseSpeedGain, -maximumMotorSpeed, maximumMotorSpeed);
                motor.maxMotorTorque = torque;
                item.Joint.motor = motor;
                item.Joint.useMotor = true;
            }
        }

        private void ApplyBalance(float torque)
        {
            float error = Mathf.DeltaAngle(torsoBody.rotation, 0f);
            torsoBody.AddTorque((error * torque) - (torsoBody.angularVelocity * balanceDamping), ForceMode2D.Force);
        }

        private void DisablePoseMotors()
        {
            for (int i = 0; i < hingeDefaults.Count; i++)
            {
                HingeDefaults item = hingeDefaults[i];
                if (item.Joint != null) item.Joint.useMotor = item.UseMotor;
            }
        }

        internal void ReportCollision(Rigidbody2D source, Collision2D collision)
        {
            if (currentState == RagdollState.Frozen || source == null || collision == null) return;
            if (collision.rigidbody != null && partByBody.ContainsKey(collision.rigidbody)) return;
            float speed = collision.relativeVelocity.magnitude;
            if (speed < minimumImpactSpeed) return;
            // Only excess velocity becomes damage, eliminating resting-contact and minor-scuff damage.
            float damage = Mathf.Min((speed - minimumImpactSpeed) * damagePerSpeed, maximumDamagePerImpact);
            if (damage <= 0f) return;
            Vector2 point = collision.contactCount > 0 ? collision.GetContact(0).point : source.worldCenterOfMass;
            currentHealth = Mathf.Max(0f, currentHealth - damage);
            currentCombo++; lastDamageTime = Time.time;
            OnDamageTaken?.Invoke(damage, point);
            if (currentHealth <= 0f && currentState != RagdollState.KnockedOut) KnockOut();
        }

        void IRagdollCollisionReceiver.ReportCollision(Rigidbody2D source, Collision2D collision)
        {
            ReportCollision(source, collision);
        }

        public void SetState(RagdollState state)
        {
            if (state == RagdollState.KnockedOut) { KnockOut(); return; }
            RestorePhysics(); currentState = state;
            if (state != RagdollState.Frozen) return;
            ReleaseDrag();
            foreach (BodyDefaults item in bodyDefaults)
            {
                if (item.Body == null) continue;
                item.Body.velocity = Vector2.zero; item.Body.angularVelocity = 0f;
                item.Body.constraints = RigidbodyConstraints2D.FreezeAll;
            }
        }
        private void KnockOut()
        {
            ReleaseDrag(); isGettingUp = false; fallenTime = 0f;
            currentState = RagdollState.KnockedOut; reviveAt = Time.time + reviveDelay;
            foreach (HingeDefaults item in hingeDefaults)
            {
                if (item.Joint == null) continue;
                item.Joint.useLimits = false; item.Joint.useMotor = false;
            }
            OnCharacterKO?.Invoke();
        }
        public void Revive()
        {
            RestorePhysics(); currentHealth = maximumHealth; currentCombo = 0;
            lastDamageTime = float.NegativeInfinity; reviveAt = float.PositiveInfinity;
            currentState = RagdollState.Active; OnCharacterRevived?.Invoke();
        }
        private void RestorePhysics()
        {
            foreach (BodyDefaults item in bodyDefaults)
                if (item.Body != null) item.Body.constraints = item.Constraints;
            foreach (HingeDefaults item in hingeDefaults)
            {
                if (item.Joint == null) continue;
                item.Joint.limits = item.Limits; item.Joint.motor = item.Motor;
                item.Joint.useLimits = item.UseLimits; item.Joint.useMotor = item.UseMotor;
            }
        }

        private void ReadPointerInput()
        {
            if (inputCamera == null || currentState == RagdollState.Frozen) return;
            if (Input.touchCount > 0) { ReadTouches(); return; }
            if (Input.GetMouseButtonDown(0)) BeginDrag(ScreenToWorld(Input.mousePosition));
            if (Input.GetMouseButton(0) && pointerHeld) pendingTarget = ScreenToWorld(Input.mousePosition);
            if (Input.GetMouseButtonUp(0)) ReleaseDrag();
        }
        private void ReadTouches()
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (activeTouchId >= 0 && touch.fingerId != activeTouchId) continue;
                Vector2 world = ScreenToWorld(touch.position);
                if (touch.phase == TouchPhase.Began) { activeTouchId = touch.fingerId; BeginDrag(world); }
                else if (touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary) pendingTarget = world;
                else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled) ReleaseDrag();
            }
        }
        private void BeginDrag(Vector2 world)
        {
            Collider2D hit = Physics2D.OverlapPoint(world, grabbableLayers);
            Rigidbody2D body = hit == null ? null : hit.attachedRigidbody;
            if (body == null || !partByBody.ContainsKey(body)) { activeTouchId = -1; return; }
            TargetJoint2D joint = body.GetComponent<TargetJoint2D>();
            if (joint == null) joint = body.gameObject.AddComponent<TargetJoint2D>();
            joint.enabled = false; joint.autoConfigureTarget = false;
            joint.anchor = body.transform.InverseTransformPoint(world); joint.target = world;
            joint.maxForce = dragMaxForce; joint.dampingRatio = dragDampingRatio; joint.frequency = dragFrequency;
            joint.enabled = true; activeDragJoint = joint; pendingTarget = world; pointerHeld = true;
        }
        private void ReleaseDrag()
        {
            // Disabling the spring preserves the body's accumulated fling velocity.
            if (activeDragJoint != null) activeDragJoint.enabled = false;
            activeDragJoint = null; pointerHeld = false; activeTouchId = -1;
        }
        private Vector2 ScreenToWorld(Vector2 screen)
        {
            Vector3 world = inputCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -inputCamera.transform.position.z));
            return new Vector2(world.x, world.y);
        }
        private void OnValidate()
        {
            maximumHealth = Mathf.Max(1f, maximumHealth);
            currentHealth = Mathf.Clamp(currentHealth, 0f, maximumHealth);
            minimumImpactSpeed = Mathf.Max(0f, minimumImpactSpeed);
            damagePerSpeed = Mathf.Max(0f, damagePerSpeed);
            maximumDamagePerImpact = Mathf.Max(0f, maximumDamagePerImpact);
            comboTimeout = Mathf.Max(0f, comboTimeout); reviveDelay = Mathf.Max(0f, reviveDelay);
            standUpDelay = Mathf.Max(0f, standUpDelay);
            standingMotorTorque = Mathf.Max(0f, standingMotorTorque); getUpMotorTorque = Mathf.Max(0f, getUpMotorTorque);
            poseSpeedGain = Mathf.Max(0f, poseSpeedGain); maximumMotorSpeed = Mathf.Max(0f, maximumMotorSpeed);
            standingBalanceTorque = Mathf.Max(0f, standingBalanceTorque); getUpBalanceTorque = Mathf.Max(0f, getUpBalanceTorque);
            balanceDamping = Mathf.Max(0f, balanceDamping); getUpLiftForce = Mathf.Max(0f, getUpLiftForce);
            maximumGetUpVelocity = Mathf.Max(0f, maximumGetUpVelocity);
        }
    }
}
