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
        private struct BodyDefaults
        {
            public Rigidbody2D Body; public RigidbodyConstraints2D Constraints; public float Mass;
            public float Drag; public float AngularDrag; public float GravityScale;
        }
        private struct ColliderDefaults { public Collider2D Collider; public PhysicsMaterial2D Material; }

        [Header("Structure")]
        [SerializeField] private bool discoverPartsOnAwake = true;
        [SerializeField] private RagdollPart[] parts = Array.Empty<RagdollPart>();
        [Header("Physics Profile")]
        [Tooltip("Select one of the three production ragdoll variations.")]
        [SerializeField] private RagdollCategory ragdollCategory = RagdollCategory.SolidRobot;
        [SerializeField] private bool applyCategoryOnAwake = true;
        [Tooltip("Optional reusable profile. ApplyProfile can replace this asset at runtime.")]
        [SerializeField] private RagdollProfile activeProfile;
        [Header("Drag Spring")]
        [SerializeField] private Camera inputCamera;
        [Tooltip("Keep disabled when DamageReceiver2D is installed on limbs to avoid duplicate mouse joints. Touch input remains centralized.")]
        [SerializeField] private bool useCentralizedMouseInput;
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
        [Header("Breakable Limbs")]
        [SerializeField] private bool enableLimbBreaking = true;
        [Min(1f)] [SerializeField] private float limbHealth = 45f;
        [Tooltip("Joint reaction force required before sustained pulling starts damaging a limb.")]
        [Min(0f)] [SerializeField] private float jointBreakStress = 450f;
        [Tooltip("Damage per second for each unit of reaction force above the threshold.")]
        [Min(0f)] [SerializeField] private float jointStressDamageRate = 0.02f;
        [Header("Recovery")]
        [Min(0f)] [SerializeField] private float reviveDelay = 4f;
        [Header("Limp Physics")]
        [SerializeField] private float gravityScaleDefault = 0.6f;
        [SerializeField] private float gravityScaleKnockedOut = 1.2f;
        [Min(0f)] [SerializeField] private float dragDefault = 1f;
        [Min(0f)] [SerializeField] private float angularDragDefault = 1.5f;
        [Header("Standing and Get Up")]
        [Tooltip("Seconds the healthy ragdoll may remain fallen before the get-up assist starts.")]
        [Min(0f)] [SerializeField] private float standUpDelay = 2f;
        [Tooltip("Multiplies get-up motor speed, torque, balance, and lift. Higher values recover faster.")]
        [Range(0.25f, 5f)] [SerializeField] private float standUpSpeed = 2f;
        [Range(10f, 80f)] [SerializeField] private float fallenAngle = 45f;
        [Range(2f, 35f)] [SerializeField] private float standingAngle = 16f;
        [Min(0f)] [SerializeField] private float standingMotorTorque = 95f;
        [Min(0f)] [SerializeField] private float getUpMotorTorque = 220f;
        [Min(0f)] [SerializeField] private float poseSpeedGain = 8f;
        [Min(0f)] [SerializeField] private float maximumMotorSpeed = 220f;
        [Min(0f)] [SerializeField] private float standingBalanceTorque = 20f;
        [Min(0f)] [SerializeField] private float getUpBalanceTorque = 65f;
        [Min(0f)] [SerializeField] private float balanceDamping = 3f;
        [Tooltip("Angular error ignored while standing, preventing constant micro-corrections.")]
        [Range(0f, 10f)] [SerializeField] private float idleAngleDeadZone = 2f;
        [Tooltip("Angular speed ignored inside the idle pose.")]
        [Range(0f, 30f)] [SerializeField] private float idleAngularVelocityDeadZone = 5f;
        [Min(0f)] [SerializeField] private float getUpLiftForce = 52f;
        [Min(0f)] [SerializeField] private float maximumGetUpVelocity = 2.5f;
        [SerializeField] private float currentHealth;
        [SerializeField] private int currentCombo;
        [SerializeField] private RagdollState currentState = RagdollState.Active;

        private readonly Dictionary<Rigidbody2D, RagdollPart> partByBody = new Dictionary<Rigidbody2D, RagdollPart>();
        private readonly List<HingeDefaults> hingeDefaults = new List<HingeDefaults>();
        private readonly List<BodyDefaults> bodyDefaults = new List<BodyDefaults>();
        private readonly List<ColliderDefaults> colliderDefaults = new List<ColliderDefaults>();
        private readonly Dictionary<Rigidbody2D, DismemberableLimb> breakableByBody = new Dictionary<Rigidbody2D, DismemberableLimb>();
        private TargetJoint2D activeDragJoint;
        private Vector2 pendingTarget;
        private bool pointerHeld;
        private int activeTouchId = -1;
        private float lastDamageTime = float.NegativeInfinity;
        private float reviveAt = float.PositiveInfinity;
        private Rigidbody2D torsoBody;
        private float fallenTime;
        private bool isGettingUp;
        private PhysicsMaterial2D runtimeProfileMaterial;
        private bool isLimp;

        public event Action<float, Vector2> OnDamageTaken;
        public event Action OnCharacterKO;
        public event Action OnCharacterRevived;
        public event Action<RagdollProfile> OnProfileApplied;
        public event Action<RagdollProfileType, Color, Vector2> OnProfileDamageEffect;
        public event Action<Rigidbody2D, float, float, Vector2> OnLimbDamaged;
        public event Action<Rigidbody2D, Vector2> OnLimbBroken;
        public event Action<bool> OnLimpStateChanged;
        public event Action<float> OnKnockoutStarted;
        public IReadOnlyList<RagdollPart> Parts => parts;
        public float CurrentHealth => currentHealth;
        public float MaximumHealth => maximumHealth;
        public int CurrentCombo => currentCombo;
        public RagdollState CurrentState => currentState;
        public bool IsGettingUp => isGettingUp;
        public float StandUpDelay => standUpDelay;
        public RagdollProfile ActiveProfile => activeProfile;
        public RagdollCategory Category => ragdollCategory;
        public bool IsLimp => isLimp;
        public Color DamageParticleColor => activeProfile != null ? activeProfile.DamageParticleColor : Color.white;
        internal float BaseLimbHealth => limbHealth;

        private void Awake()
        {
            if (inputCamera == null) inputCamera = Camera.main;
            InitializeParts();
            if (applyCategoryOnAwake) activeProfile = LoadCategoryProfile(ragdollCategory);
            if (activeProfile != null) ApplyProfile(activeProfile);
            if (GetComponent<RagdollLifeVisuals>() == null)
                gameObject.AddComponent<RagdollLifeVisuals>();
            if (GetComponent<RagdollElementalEffects>() == null)
                gameObject.AddComponent<RagdollElementalEffects>();
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
            partByBody.Clear(); hingeDefaults.Clear(); bodyDefaults.Clear(); colliderDefaults.Clear(); breakableByBody.Clear(); torsoBody = null;
            foreach (RagdollPart part in parts)
            {
                if (part == null || part.Body == null || partByBody.ContainsKey(part.Body)) continue;
                partByBody.Add(part.Body, part);
                if (torsoBody == null && part.Body.name.IndexOf("torso", StringComparison.OrdinalIgnoreCase) >= 0)
                    torsoBody = part.Body;
                bodyDefaults.Add(new BodyDefaults { Body = part.Body, Constraints = part.Body.constraints,
                    Mass = part.Body.mass, Drag = part.Body.drag, AngularDrag = part.Body.angularDrag,
                    GravityScale = part.Body.gravityScale });
                foreach (Collider2D collider in part.Colliders)
                    if (collider != null) colliderDefaults.Add(new ColliderDefaults { Collider = collider, Material = collider.sharedMaterial });
                RagdollLimb relay = part.Body.GetComponent<RagdollLimb>();
                if (relay == null) relay = part.Body.gameObject.AddComponent<RagdollLimb>();
                relay.Initialize(this, part.Body);
                if (part.Hinges.Length > 0)
                {
                    ActiveRagdollLimb activeLimb = part.Body.GetComponent<ActiveRagdollLimb>();
                    if (activeLimb == null) activeLimb = part.Body.gameObject.AddComponent<ActiveRagdollLimb>();
                    if (part.Hinges[0] != null)
                        activeLimb.Configure(part.Hinges[0].jointAngle, poseSpeedGain, balanceDamping, standingMotorTorque);
                    activeLimb.SetExternallyDriven(true);
                    if (enableLimbBreaking)
                    {
                        DismemberableLimb breakable = part.Body.GetComponent<DismemberableLimb>();
                        if (breakable == null) breakable = part.Body.gameObject.AddComponent<DismemberableLimb>();
                        breakable.Initialize(this, limbHealth, jointBreakStress, jointStressDamageRate);
                        breakableByBody.Add(part.Body, breakable);
                    }
                }
                if (part.Body.GetComponent<DamageReceiver2D>() == null && part.Colliders.Length > 0)
                    part.Body.gameObject.AddComponent<DamageReceiver2D>();
                foreach (HingeJoint2D hinge in part.Hinges)
                {
                    if (hinge == null) continue;
                    hingeDefaults.Add(new HingeDefaults { Joint = hinge, UseLimits = hinge.useLimits,
                        Limits = hinge.limits, UseMotor = hinge.useMotor, Motor = hinge.motor,
                        RestAngle = hinge.jointAngle });
                }
            }
        }

        /// <summary>
        /// Applies a reusable physics personality from the original authored rig values. Calling
        /// this repeatedly is safe: values never compound when switching between profiles.
        /// </summary>
        public void ApplyProfile(RagdollProfile newProfile)
        {
            activeProfile = newProfile;
            RestoreProfileBaseline();
            if (newProfile == null)
            {
                OnProfileApplied?.Invoke(null);
                return;
            }

            float mass = newProfile.MassMultiplier;
            float drag = newProfile.LinearDrag;
            float angularDrag = newProfile.AngularDrag;
            float gravityModifier = newProfile.UseGravity ? newProfile.GravityScaleModifier : 0f;

            switch (newProfile.ProfileType)
            {
                case RagdollProfileType.SolidRobot:
                    mass = Mathf.Max(3f, mass); drag = Mathf.Max(2f, drag); angularDrag = Mathf.Max(3f, angularDrag);
                    break;
                case RagdollProfileType.Jelly:
                    mass = Mathf.Min(0.7f, mass); drag = Mathf.Min(0.1f, drag); angularDrag = Mathf.Min(0.15f, angularDrag);
                    break;
                case RagdollProfileType.MagicDoll:
                    mass = Mathf.Min(0.35f, mass); gravityModifier = Mathf.Min(0.08f, gravityModifier);
                    angularDrag = Mathf.Max(4f, angularDrag);
                    break;
                case RagdollProfileType.GalaxySpace:
                    gravityModifier = 0f; drag = Mathf.Min(0.01f, drag); angularDrag = Mathf.Min(0.05f, angularDrag);
                    break;
            }

            for (int i = 0; i < bodyDefaults.Count; i++)
            {
                BodyDefaults item = bodyDefaults[i];
                if (item.Body == null) continue;
                item.Body.mass = Mathf.Max(0.01f, item.Mass * mass);
                item.Body.drag = drag;
                item.Body.angularDrag = angularDrag;
                item.Body.gravityScale = item.GravityScale * gravityModifier;
                item.Body.WakeUp();
            }

            PhysicsMaterial2D material = ResolveProfileMaterial(newProfile);
            for (int i = 0; i < colliderDefaults.Count; i++)
                if (colliderDefaults[i].Collider != null) colliderDefaults[i].Collider.sharedMaterial = material ?? colliderDefaults[i].Material;

            for (int i = 0; i < hingeDefaults.Count; i++)
            {
                HingeDefaults item = hingeDefaults[i];
                if (item.Joint == null) continue;
                if (newProfile.ProfileType == RagdollProfileType.SolidRobot)
                {
                    JointAngleLimits2D limits = item.Joint.limits; limits.min = -15f; limits.max = 15f;
                    item.Joint.limits = limits; item.Joint.useLimits = true;
                }
                else if (newProfile.ProfileType == RagdollProfileType.Jelly ||
                         newProfile.ProfileType == RagdollProfileType.MagicDoll ||
                         newProfile.ProfileType == RagdollProfileType.GalaxySpace)
                {
                    item.Joint.useLimits = false;
                }
            }

            OnProfileApplied?.Invoke(newProfile);
            ApplyProfileDurability(newProfile);
        }

        private void ApplyProfileDurability(RagdollProfile profile)
        {
            float multiplier = 1f;
            switch (profile.ProfileType)
            {
                case RagdollProfileType.SolidRobot: multiplier = 2.25f; break;
                case RagdollProfileType.Jelly: multiplier = 0.8f; break;
                case RagdollProfileType.MagicDoll: multiplier = 0.65f; break;
                case RagdollProfileType.GalaxySpace: multiplier = 0.75f; break;
            }
            foreach (DismemberableLimb breakable in breakableByBody.Values)
                if (breakable != null) breakable.SetDurabilityMultiplier(limbHealth, multiplier);
        }

        /// <summary>Changes category and applies its bundled profile immediately.</summary>
        public void SelectCategory(RagdollCategory category)
        {
            ragdollCategory = category;
            RagdollProfile profile = LoadCategoryProfile(category);
            if (profile == null)
            {
                Debug.LogWarning($"No bundled profile was found for {category}.", this);
                return;
            }
            ApplyProfile(profile);
        }

        private static RagdollProfile LoadCategoryProfile(RagdollCategory category)
        {
            string assetName;
            switch (category)
            {
                case RagdollCategory.JellyCharacter: assetName = "Jelly Character"; break;
                case RagdollCategory.GalaxySpace: assetName = "Galaxy Space"; break;
                default: assetName = "Solid Robot"; break;
            }
            return Resources.Load<RagdollProfile>("Ragdoll Profiles/" + assetName);
        }

        private PhysicsMaterial2D ResolveProfileMaterial(RagdollProfile profile)
        {
            if (profile.CustomPhysicsMaterial != null) return profile.CustomPhysicsMaterial;
            if (profile.ProfileType != RagdollProfileType.Jelly) return null;
            if (runtimeProfileMaterial == null)
            {
                runtimeProfileMaterial = new PhysicsMaterial2D("Runtime Jelly Material")
                {
                    bounciness = 0.7f,
                    friction = 0.2f
                };
            }
            return runtimeProfileMaterial;
        }

        private void RestoreProfileBaseline()
        {
            for (int i = 0; i < bodyDefaults.Count; i++)
            {
                BodyDefaults item = bodyDefaults[i];
                if (item.Body == null) continue;
                item.Body.mass = item.Mass; item.Body.drag = item.Drag;
                item.Body.angularDrag = item.AngularDrag; item.Body.gravityScale = item.GravityScale;
            }
            for (int i = 0; i < colliderDefaults.Count; i++)
                if (colliderDefaults[i].Collider != null) colliderDefaults[i].Collider.sharedMaterial = colliderDefaults[i].Material;
            for (int i = 0; i < hingeDefaults.Count; i++)
            {
                HingeDefaults item = hingeDefaults[i];
                if (item.Joint == null) continue;
                item.Joint.limits = item.Limits; item.Joint.motor = item.Motor;
                item.Joint.useLimits = item.UseLimits; item.Joint.useMotor = item.UseMotor;
            }
        }

        private void UpdateStandingAssist()
        {
            bool canStand = currentState == RagdollState.Active || currentState == RagdollState.Burning;
            if (activeProfile != null && (activeProfile.ProfileType == RagdollProfileType.MagicDoll ||
                activeProfile.ProfileType == RagdollProfileType.GalaxySpace || activeProfile.JointSpringForce <= 0f))
                canStand = false;
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
                float liftVelocityLimit = maximumGetUpVelocity * standUpSpeed;
                if (torsoBody.velocity.y < liftVelocityLimit)
                    torsoBody.AddForce(Vector2.up * getUpLiftForce * standUpSpeed, ForceMode2D.Force);

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
            if (activeProfile != null) torque = activeProfile.JointSpringForce * (isGettingUp ? 1.75f : 1f);
            float speedMultiplier = isGettingUp ? standUpSpeed : 1f;
            torque *= speedMultiplier;
            for (int i = 0; i < hingeDefaults.Count; i++)
            {
                HingeDefaults item = hingeDefaults[i];
                if (item.Joint == null) continue;
                if (activeDragJoint != null && item.Joint.attachedRigidbody == activeDragJoint.attachedRigidbody) continue;
                float error = Mathf.DeltaAngle(item.Joint.jointAngle, item.RestAngle);
                JointMotor2D motor = item.Joint.motor;
                bool settled = !isGettingUp && Mathf.Abs(error) <= idleAngleDeadZone &&
                    Mathf.Abs(item.Joint.attachedRigidbody.angularVelocity) <= idleAngularVelocityDeadZone;
                motor.motorSpeed = settled ? 0f : Mathf.Clamp(error * poseSpeedGain * speedMultiplier,
                    -maximumMotorSpeed * speedMultiplier, maximumMotorSpeed * speedMultiplier);
                motor.maxMotorTorque = torque;
                item.Joint.motor = motor;
                item.Joint.useMotor = true;
            }
        }

        private void ApplyBalance(float torque)
        {
            float error = Mathf.DeltaAngle(torsoBody.rotation, 0f);
            float damping = activeProfile != null ? activeProfile.JointDamping : balanceDamping;
            if (!isGettingUp && Mathf.Abs(error) <= idleAngleDeadZone &&
                Mathf.Abs(torsoBody.angularVelocity) <= idleAngularVelocityDeadZone)
                return;
            float multiplier = isGettingUp ? standUpSpeed : 1f;
            torsoBody.AddTorque((error * torque * multiplier) -
                (torsoBody.angularVelocity * damping * multiplier), ForceMode2D.Force);
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
            if (activeProfile != null)
                OnProfileDamageEffect?.Invoke(activeProfile.ProfileType, activeProfile.DamageParticleColor, point);
            RagdollElementalEffects elements = GetComponent<RagdollElementalEffects>();
            if (breakableByBody.TryGetValue(source, out DismemberableLimb structuralLimb))
            {
                structuralLimb.TakeDamage(damage, collision.relativeVelocity, point);
                if (elements != null) elements.NotifyImpact(structuralLimb, speed, point);
            }
            if (currentHealth <= 0f && currentState != RagdollState.KnockedOut) KnockOut();
        }

        internal void NotifyLimbDamaged(DismemberableLimb limb, float damage, Vector2 point)
        {
            if (limb != null) OnLimbDamaged?.Invoke(limb.Body, damage, limb.JointHealth, point);
        }

        internal void NotifyLimbBroken(DismemberableLimb limb, Vector2 point)
        {
            if (limb == null) return;
            if (activeDragJoint != null && activeDragJoint.attachedRigidbody == limb.Body) ReleaseDrag();
            OnLimbBroken?.Invoke(limb.Body, point);
        }

        // Compatibility hooks for ragdolls authored with the previous durability component.
        internal void NotifyLimbDamaged(RagdollBreakableLimb limb, float damage, Vector2 point)
        {
            if (limb != null) OnLimbDamaged?.Invoke(limb.Body, damage, limb.CurrentHealth, point);
        }

        internal void NotifyLimbBroken(RagdollBreakableLimb limb, Vector2 point)
        {
            if (limb == null) return;
            if (activeDragJoint != null && activeDragJoint.attachedRigidbody == limb.Body) ReleaseDrag();
            OnLimbBroken?.Invoke(limb.Body, point);
        }

        void IRagdollCollisionReceiver.ReportCollision(Rigidbody2D source, Collision2D collision)
        {
            ReportCollision(source, collision);
        }

        public void SetState(RagdollState state)
        {
            if (state == RagdollState.KnockedOut) { KnockOut(); return; }
            RestorePhysics();
            if (activeProfile != null) ApplyProfile(activeProfile);
            currentState = state;
            if (state != RagdollState.Frozen) return;
            ReleaseDrag();
            foreach (BodyDefaults item in bodyDefaults)
            {
                if (item.Body == null) continue;
                item.Body.velocity = Vector2.zero; item.Body.angularVelocity = 0f;
                item.Body.constraints = RigidbodyConstraints2D.FreezeAll;
            }
        }
        /// <summary>Enables or disables passive physics across the complete body.</summary>
        public void SetLimpState(bool makeLimp)
        {
            if (isLimp == makeLimp) return;
            isLimp = makeLimp;
            ReleaseDrag();
            for (int i = 0; i < hingeDefaults.Count; i++)
            {
                HingeDefaults item = hingeDefaults[i];
                if (item.Joint == null) continue;
                item.Joint.useMotor = makeLimp ? false : item.UseMotor;
            }
            for (int i = 0; i < bodyDefaults.Count; i++)
            {
                Rigidbody2D body = bodyDefaults[i].Body;
                if (body == null) continue;
                body.gravityScale = makeLimp ? gravityScaleKnockedOut : gravityScaleDefault;
                body.drag = dragDefault;
                body.angularDrag = angularDragDefault;
                body.WakeUp();
            }
            OnLimpStateChanged?.Invoke(makeLimp);
        }

        /// <summary>Makes the ragdoll passive and schedules automatic recovery.</summary>
        public void Knockout(float duration)
        {
            duration = Mathf.Max(0f, duration);
            ReleaseDrag(); isGettingUp = false; fallenTime = 0f;
            currentState = RagdollState.KnockedOut;
            reviveAt = Time.time + duration;
            SetLimpState(true);
            for (int i = 0; i < hingeDefaults.Count; i++)
            {
                HingeDefaults item = hingeDefaults[i];
                if (item.Joint != null) item.Joint.useLimits = false;
            }
            OnKnockoutStarted?.Invoke(duration);
            OnCharacterKO?.Invoke();
        }

        private void KnockOut()
        {
            Knockout(reviveDelay);
        }
        public void Revive()
        {
            RestorePhysics();
            if (activeProfile != null) ApplyProfile(activeProfile);
            currentHealth = maximumHealth; currentCombo = 0;
            lastDamageTime = float.NegativeInfinity; reviveAt = float.PositiveInfinity;
            currentState = RagdollState.Active;
            isLimp = false;
            OnLimpStateChanged?.Invoke(false);
            OnCharacterRevived?.Invoke();
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
            if (!useCentralizedMouseInput) return;
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
            ConfigureDragJoint(joint);
            joint.enabled = true; activeDragJoint = joint; pendingTarget = world; pointerHeld = true;
        }

        private void ConfigureDragJoint(TargetJoint2D joint)
        {
            joint.maxForce = dragMaxForce;
            joint.dampingRatio = dragDampingRatio;
            joint.frequency = dragFrequency;
            if (activeProfile == null) return;

            switch (activeProfile.ProfileType)
            {
                case RagdollProfileType.SolidRobot:
                    joint.maxForce = dragMaxForce * 0.65f; joint.frequency = dragFrequency * 0.65f; joint.dampingRatio = 1f;
                    break;
                case RagdollProfileType.Jelly:
                    joint.maxForce = dragMaxForce * 0.55f; joint.frequency = dragFrequency * 0.35f; joint.dampingRatio = 0.2f;
                    break;
                case RagdollProfileType.MagicDoll:
                    joint.maxForce = dragMaxForce * 0.75f; joint.frequency = dragFrequency * 0.5f; joint.dampingRatio = 0.65f;
                    break;
                case RagdollProfileType.GalaxySpace:
                    joint.maxForce = dragMaxForce; joint.frequency = dragFrequency * 0.8f; joint.dampingRatio = 0.35f;
                    break;
                default:
                    joint.maxForce = dragMaxForce / Mathf.Sqrt(Mathf.Max(0.25f, activeProfile.MassMultiplier));
                    joint.dampingRatio = Mathf.Clamp01(activeProfile.JointDamping / 5f);
                    break;
            }
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
            standUpSpeed = Mathf.Clamp(standUpSpeed, 0.25f, 5f);
            standingMotorTorque = Mathf.Max(0f, standingMotorTorque); getUpMotorTorque = Mathf.Max(0f, getUpMotorTorque);
            poseSpeedGain = Mathf.Max(0f, poseSpeedGain); maximumMotorSpeed = Mathf.Max(0f, maximumMotorSpeed);
            standingBalanceTorque = Mathf.Max(0f, standingBalanceTorque); getUpBalanceTorque = Mathf.Max(0f, getUpBalanceTorque);
            balanceDamping = Mathf.Max(0f, balanceDamping); getUpLiftForce = Mathf.Max(0f, getUpLiftForce);
            idleAngleDeadZone = Mathf.Clamp(idleAngleDeadZone, 0f, 10f);
            idleAngularVelocityDeadZone = Mathf.Clamp(idleAngularVelocityDeadZone, 0f, 30f);
            maximumGetUpVelocity = Mathf.Max(0f, maximumGetUpVelocity);
            limbHealth = Mathf.Max(1f, limbHealth); jointBreakStress = Mathf.Max(0f, jointBreakStress);
            jointStressDamageRate = Mathf.Max(0f, jointStressDamageRate);
            gravityScaleDefault = Mathf.Max(0f, gravityScaleDefault);
            gravityScaleKnockedOut = Mathf.Max(0f, gravityScaleKnockedOut);
            dragDefault = Mathf.Max(0f, dragDefault); angularDragDefault = Mathf.Max(0f, angularDragDefault);
        }

        private void OnDestroy()
        {
            if (runtimeProfileMaterial != null) Destroy(runtimeProfileMaterial);
        }
    }
}
