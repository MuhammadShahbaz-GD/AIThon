using System;
using System.Collections.Generic;
using UnityEngine;

namespace KickTheBuddy.Physics
{
    [DisallowMultipleComponent]
    public sealed class RagdollRigController2D : MonoBehaviour
    {
        public enum JointRole { Other, Head, Arm, Leg }

        public sealed class JointRuntime
        {
            public HingeJoint2D Joint;
            public bool AuthoredEnabled;
            public bool AuthoredUseLimits;
            public JointAngleLimits2D AuthoredLimits;
            public bool AuthoredUseMotor;
            public JointMotor2D AuthoredMotor;
            public float RestAngle;
            public JointRole Role;
            public bool IsUpperLimb;
        }

        public sealed class BodyRuntime
        {
            public Rigidbody2D Body;
            public bool AuthoredSimulated;
            public RigidbodyConstraints2D AuthoredConstraints;
            public float AuthoredMass;
            public float AuthoredDrag;
            public float AuthoredAngularDrag;
            public float AuthoredGravityScale;
        }

        public sealed class ColliderRuntime
        {
            public Collider2D Collider;
            public bool AuthoredEnabled;
            public PhysicsMaterial2D AuthoredMaterial;
        }

        [Header("Explicit Main Parts")]
        [Tooltip("Authored Head, Belly/Torso, Left/Right Arm, and Left/Right Leg only. Runtime hierarchy discovery is not used.")]
        [SerializeField] private RagdollController.RagdollPart[] authoredParts = Array.Empty<RagdollController.RagdollPart>();

        [Header("Limb Structure")]
        [SerializeField] private bool enableLimbBreaking = true;
        [Min(1f)] [SerializeField] private float fallbackLimbHealth = 45f;
        [Min(0f)] [SerializeField] private float jointBreakStress = 450f;
        [Min(0f)] [SerializeField] private float jointStressDamageRate = .02f;

        private readonly List<RagdollController.RagdollPart> parts = new List<RagdollController.RagdollPart>(6);
        private readonly List<JointRuntime> joints = new List<JointRuntime>(8);
        private readonly List<BodyRuntime> bodies = new List<BodyRuntime>(6);
        private readonly List<ColliderRuntime> colliders = new List<ColliderRuntime>(8);
        private readonly List<DismemberableLimb> breakableLimbs = new List<DismemberableLimb>(6);
        private RagdollController owner;
        private RagdollDamageManager damageManager;
        private Rigidbody2D torso;
        private Rigidbody2D head;

        public IReadOnlyList<RagdollController.RagdollPart> Parts => parts;
        public IReadOnlyList<JointRuntime> Joints => joints;
        public IReadOnlyList<BodyRuntime> Bodies => bodies;
        public IReadOnlyList<ColliderRuntime> Colliders => colliders;
        public Rigidbody2D Torso => torso;
        public Rigidbody2D Head => head;
        public float BaseLimbHealth => fallbackLimbHealth;

        internal void Initialize(RagdollController controller, RagdollDamageManager damage)
        {
            owner = controller;
            damageManager = damage;
            Rebuild();
        }

        public void Rebuild()
        {
            parts.Clear();
            joints.Clear();
            bodies.Clear();
            colliders.Clear();
            breakableLimbs.Clear();
            torso = null;
            head = null;

            if (authoredParts == null || authoredParts.Length == 0)
            {
                Debug.LogError("RagdollRigController2D has no authored main parts. Run the explicit ragdoll setup tool.", this);
                return;
            }

            for (int i = 0; i < authoredParts.Length; i++)
            {
                RagdollController.RagdollPart part = authoredParts[i];
                if (part == null || !part.IsConfigured)
                {
                    Debug.LogError("Ragdoll main-part reference " + i + " is incomplete.", this);
                    continue;
                }
                if (ContainsBody(part.Body))
                {
                    Debug.LogWarning("Duplicate ragdoll body reference ignored: " + part.Body.name, this);
                    continue;
                }

                parts.Add(part);
                if (part.PartType == RagdollPartType.Torso) torso = part.Body;
                else if (part.PartType == RagdollPartType.Head) head = part.Body;

                bodies.Add(new BodyRuntime
                {
                    Body = part.Body,
                    AuthoredSimulated = part.Body.simulated,
                    AuthoredConstraints = part.Body.constraints,
                    AuthoredMass = part.Body.mass,
                    AuthoredDrag = part.Body.drag,
                    AuthoredAngularDrag = part.Body.angularDrag,
                    AuthoredGravityScale = part.Body.gravityScale
                });

                Collider2D[] authoredColliders = part.Colliders;
                for (int c = 0; c < authoredColliders.Length; c++)
                {
                    Collider2D collider = authoredColliders[c];
                    if (collider != null)
                        colliders.Add(new ColliderRuntime { Collider = collider, AuthoredEnabled = collider.enabled, AuthoredMaterial = collider.sharedMaterial });
                }

                InitializePart(part);
                CacheJoints(part);
            }

            if (torso == null) Debug.LogError("Explicit ragdoll parts do not contain a Belly/Torso reference.", this);
            if (head == null) Debug.LogError("Explicit ragdoll parts do not contain a Head reference.", this);
        }

        private bool ContainsBody(Rigidbody2D candidate)
        {
            for (int i = 0; i < parts.Count; i++)
                if (parts[i].Body == candidate) return true;
            return false;
        }

        private void InitializePart(RagdollController.RagdollPart part)
        {
            part.CollisionRelay.Initialize(damageManager, part.Body);
            part.Health.Initialize(fallbackLimbHealth, part.DismemberableLimb);

            if (part.DragJoint != null)
            {
                part.DragJoint.autoConfigureTarget = false;
                part.DragJoint.enabled = false;
            }

            part.DamageReceiver.Initialize(
                part.Body,
                owner,
                part.DismemberableLimb,
                damageManager,
                owner != null ? owner.ElementalEffects : null,
                part.Health,
                part.DragJoint);

            ActiveRagdollLimb activeLimb = part.ActiveLimb;
            if (activeLimb != null)
            {
                HingeJoint2D firstHinge = part.Hinges.Length > 0 ? part.Hinges[0] : null;
                activeLimb.Initialize(part.Body, firstHinge);
                if (firstHinge != null) activeLimb.Configure(firstHinge.jointAngle, 8f, 3f, 95f);
                activeLimb.SetExternallyDriven(true);
            }

            DismemberableLimb breakable = part.DismemberableLimb;
            if (breakable != null)
            {
                HingeJoint2D parentJoint = part.Hinges.Length > 0 ? part.Hinges[0] : null;
                breakable.CanBeSevered = enableLimbBreaking;
                breakable.Initialize(owner, part.Body, parentJoint, fallbackLimbHealth, jointBreakStress, jointStressDamageRate);
                breakableLimbs.Add(breakable);
            }
        }

        private void CacheJoints(RagdollController.RagdollPart part)
        {
            HingeJoint2D[] authoredHinges = part.Hinges;
            for (int i = 0; i < authoredHinges.Length; i++)
            {
                HingeJoint2D hinge = authoredHinges[i];
                if (hinge == null) continue;
                joints.Add(new JointRuntime
                {
                    Joint = hinge,
                    AuthoredEnabled = hinge.enabled,
                    AuthoredUseLimits = hinge.useLimits,
                    AuthoredLimits = hinge.limits,
                    AuthoredUseMotor = hinge.useMotor,
                    AuthoredMotor = hinge.motor,
                    RestAngle = hinge.jointAngle,
                    Role = ResolveJointRole(part.PartType),
                    IsUpperLimb = part.IsUpperLimb
                });
            }
        }

        public void RestoreAuthoredPhysics()
        {
            for (int i = 0; i < bodies.Count; i++)
            {
                BodyRuntime item = bodies[i];
                if (item.Body == null) continue;
                item.Body.simulated = item.AuthoredSimulated;
                item.Body.constraints = item.AuthoredConstraints;
                item.Body.mass = item.AuthoredMass;
                item.Body.drag = item.AuthoredDrag;
                item.Body.angularDrag = item.AuthoredAngularDrag;
                item.Body.gravityScale = item.AuthoredGravityScale;
            }

            for (int i = 0; i < colliders.Count; i++)
                if (colliders[i].Collider != null) colliders[i].Collider.sharedMaterial = colliders[i].AuthoredMaterial;

            for (int i = 0; i < joints.Count; i++)
            {
                JointRuntime item = joints[i];
                if (item.Joint == null) continue;
                item.Joint.enabled = item.AuthoredEnabled;
                item.Joint.limits = item.AuthoredLimits;
                item.Joint.motor = item.AuthoredMotor;
                item.Joint.useLimits = item.AuthoredUseLimits;
                item.Joint.useMotor = item.AuthoredUseMotor;
            }
        }

        internal void ApplyLimpPhysics(bool limp, float activeGravity, float limpGravity, float drag, float angularDrag)
        {
            for (int i = 0; i < joints.Count; i++)
                if (joints[i].Joint != null) joints[i].Joint.useMotor = limp ? false : joints[i].AuthoredUseMotor;
            for (int i = 0; i < bodies.Count; i++)
            {
                Rigidbody2D body = bodies[i].Body;
                if (body == null) continue;
                body.gravityScale = limp ? limpGravity : activeGravity;
                body.drag = drag;
                body.angularDrag = angularDrag;
                body.WakeUp();
            }
        }

        internal void EnterDeathState()
        {
            for (int i = 0; i < joints.Count; i++)
                if (joints[i].Joint != null)
                {
                    joints[i].Joint.useMotor = false;
                    joints[i].Joint.enabled = false;
                }
            for (int i = 0; i < colliders.Count; i++)
                if (colliders[i].Collider != null) colliders[i].Collider.enabled = false;
            for (int i = 0; i < bodies.Count; i++)
            {
                Rigidbody2D body = bodies[i].Body;
                if (body == null) continue;
                body.velocity = Vector2.zero;
                body.angularVelocity = 0f;
                body.simulated = false;
            }
            for (int i = 0; i < parts.Count; i++)
                if (parts[i].Visual != null) parts[i].Visual.enabled = false;
        }

        internal void RestoreDeathVisuals()
        {
            for (int i = 0; i < parts.Count; i++)
                if (parts[i].Visual != null) parts[i].Visual.enabled = true;
        }
        internal void FreezeAll()
        {
            for (int i = 0; i < bodies.Count; i++)
            {
                Rigidbody2D body = bodies[i].Body;
                if (body == null) continue;
                body.velocity = Vector2.zero;
                body.angularVelocity = 0f;
                body.constraints = RigidbodyConstraints2D.FreezeAll;
            }
        }

        internal void SetAllMotors(bool enabled)
        {
            for (int i = 0; i < joints.Count; i++)
                if (joints[i].Joint != null) joints[i].Joint.useMotor = enabled;
        }

        internal void SetLimitsEnabled(bool enabled)
        {
            for (int i = 0; i < joints.Count; i++)
                if (joints[i].Joint != null) joints[i].Joint.useLimits = enabled && joints[i].AuthoredUseLimits;
        }

        internal void SetDurabilityMultiplier(float multiplier)
        {
            for (int i = 0; i < breakableLimbs.Count; i++)
                if (breakableLimbs[i] != null)
                    breakableLimbs[i].SetDurabilityMultiplier(fallbackLimbHealth, multiplier);
        }

        private static JointRole ResolveJointRole(RagdollPartType type)
        {
            switch (type)
            {
                case RagdollPartType.Head: return JointRole.Head;
                case RagdollPartType.Arm: return JointRole.Arm;
                case RagdollPartType.Leg: return JointRole.Leg;
                default: return JointRole.Other;
            }
        }

        private void OnValidate()
        {
            fallbackLimbHealth = Mathf.Max(1f, fallbackLimbHealth);
            jointBreakStress = Mathf.Max(0f, jointBreakStress);
            jointStressDamageRate = Mathf.Max(0f, jointStressDamageRate);
        }
    }
}

