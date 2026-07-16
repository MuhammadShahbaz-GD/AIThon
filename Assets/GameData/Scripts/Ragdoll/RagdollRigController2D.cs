using System;
using System.Collections.Generic;
using UnityEngine;

namespace KickTheBuddy.Physics
{
    [DisallowMultipleComponent]
    public sealed class RagdollRigController2D : MonoBehaviour
    {
        public enum JointRole
        {
            Other,
            Head,
            Arm,
            Leg
        }

        public sealed class JointRuntime
        {
            public HingeJoint2D Joint;
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
            public RigidbodyConstraints2D AuthoredConstraints;
            public float AuthoredMass;
            public float AuthoredDrag;
            public float AuthoredAngularDrag;
            public float AuthoredGravityScale;
        }

        public sealed class ColliderRuntime
        {
            public Collider2D Collider;
            public PhysicsMaterial2D AuthoredMaterial;
        }

        [Header("Limb Structure")]
        [SerializeField] private bool enableLimbBreaking = true;
        [Min(1f)] [SerializeField] private float fallbackLimbHealth = 45f;
        [Min(0f)] [SerializeField] private float jointBreakStress = 450f;
        [Min(0f)] [SerializeField] private float jointStressDamageRate = .02f;

        private readonly List<RagdollController.RagdollPart> parts = new List<RagdollController.RagdollPart>();
        private readonly List<JointRuntime> joints = new List<JointRuntime>();
        private readonly List<BodyRuntime> bodies = new List<BodyRuntime>();
        private readonly List<ColliderRuntime> colliders = new List<ColliderRuntime>();
        private readonly List<DismemberableLimb> breakableLimbs = new List<DismemberableLimb>();
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

            Rigidbody2D[] discoveredBodies = GetComponentsInChildren<Rigidbody2D>(true);
            for (int i = 0; i < discoveredBodies.Length; i++)
            {
                Rigidbody2D body = discoveredBodies[i];
                if (body == null) continue;

                RagdollController.RagdollPart part = new RagdollController.RagdollPart(body);
                parts.Add(part);
                if (torso == null && body.name.IndexOf("torso", StringComparison.OrdinalIgnoreCase) >= 0)
                    torso = body;
                if (head == null && body.name.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0)
                    head = body;

                bodies.Add(new BodyRuntime
                {
                    Body = body,
                    AuthoredConstraints = body.constraints,
                    AuthoredMass = body.mass,
                    AuthoredDrag = body.drag,
                    AuthoredAngularDrag = body.angularDrag,
                    AuthoredGravityScale = body.gravityScale
                });

                for (int c = 0; c < part.Colliders.Length; c++)
                {
                    Collider2D collider = part.Colliders[c];
                    if (collider != null)
                        colliders.Add(new ColliderRuntime
                        {
                            Collider = collider,
                            AuthoredMaterial = collider.sharedMaterial
                        });
                }

                WirePart(part);
            }
        }

        private void WirePart(RagdollController.RagdollPart part)
        {
            Rigidbody2D body = part.Body;
            RagdollLimb relay = body.GetComponent<RagdollLimb>();
            if (relay == null) relay = body.gameObject.AddComponent<RagdollLimb>();
            relay.Initialize(damageManager, body);

            if (part.Hinges.Length > 0)
            {
                ActiveRagdollLimb activeLimb = body.GetComponent<ActiveRagdollLimb>();
                if (activeLimb == null) activeLimb = body.gameObject.AddComponent<ActiveRagdollLimb>();
                HingeJoint2D firstHinge = part.Hinges[0];
                if (firstHinge != null) activeLimb.Configure(firstHinge.jointAngle, 8f, 3f, 95f);
                activeLimb.SetExternallyDriven(true);

                if (enableLimbBreaking)
                {
                    DismemberableLimb breakable = body.GetComponent<DismemberableLimb>();
                    if (breakable == null) breakable = body.gameObject.AddComponent<DismemberableLimb>();
                    breakable.Initialize(owner, fallbackLimbHealth, jointBreakStress, jointStressDamageRate);
                    breakableLimbs.Add(breakable);
                }
            }

            if (part.Colliders.Length > 0 && body.GetComponent<DamageReceiver2D>() == null)
                body.gameObject.AddComponent<DamageReceiver2D>();

            RagdollPartHealth partHealth = body.GetComponent<RagdollPartHealth>();
            bool createdHealth = partHealth == null;
            if (createdHealth) partHealth = body.gameObject.AddComponent<RagdollPartHealth>();
            if (createdHealth) ConfigurePartDefaults(partHealth, body.name);
            partHealth.Initialize(fallbackLimbHealth);

            for (int i = 0; i < part.Hinges.Length; i++)
            {
                HingeJoint2D hinge = part.Hinges[i];
                if (hinge == null) continue;
                joints.Add(new JointRuntime
                {
                    Joint = hinge,
                    AuthoredUseLimits = hinge.useLimits,
                    AuthoredLimits = hinge.limits,
                    AuthoredUseMotor = hinge.useMotor,
                    AuthoredMotor = hinge.motor,
                    RestAngle = hinge.jointAngle,
                    Role = ResolveJointRole(body.name),
                    IsUpperLimb = body.name.IndexOf("upper", StringComparison.OrdinalIgnoreCase) >= 0
                });
            }
        }

        public void RestoreAuthoredPhysics()
        {
            for (int i = 0; i < bodies.Count; i++)
            {
                BodyRuntime item = bodies[i];
                if (item.Body == null) continue;
                item.Body.constraints = item.AuthoredConstraints;
                item.Body.mass = item.AuthoredMass;
                item.Body.drag = item.AuthoredDrag;
                item.Body.angularDrag = item.AuthoredAngularDrag;
                item.Body.gravityScale = item.AuthoredGravityScale;
            }

            for (int i = 0; i < colliders.Count; i++)
                if (colliders[i].Collider != null)
                    colliders[i].Collider.sharedMaterial = colliders[i].AuthoredMaterial;

            for (int i = 0; i < joints.Count; i++)
            {
                JointRuntime item = joints[i];
                if (item.Joint == null) continue;
                item.Joint.limits = item.AuthoredLimits;
                item.Joint.motor = item.AuthoredMotor;
                item.Joint.useLimits = item.AuthoredUseLimits;
                item.Joint.useMotor = item.AuthoredUseMotor;
            }
        }

        internal void ApplyLimpPhysics(bool limp, float activeGravity, float limpGravity, float drag, float angularDrag)
        {
            for (int i = 0; i < joints.Count; i++)
                if (joints[i].Joint != null)
                    joints[i].Joint.useMotor = limp ? false : joints[i].AuthoredUseMotor;

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
                if (joints[i].Joint != null)
                    joints[i].Joint.useLimits = enabled ? joints[i].AuthoredUseLimits : false;
        }

        internal void SetDurabilityMultiplier(float multiplier)
        {
            for (int i = 0; i < breakableLimbs.Count; i++)
                if (breakableLimbs[i] != null)
                    breakableLimbs[i].SetDurabilityMultiplier(fallbackLimbHealth, multiplier);
        }

        private static JointRole ResolveJointRole(string partName)
        {
            if (partName.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0) return JointRole.Head;
            if (partName.IndexOf("arm", StringComparison.OrdinalIgnoreCase) >= 0) return JointRole.Arm;
            if (partName.IndexOf("leg", StringComparison.OrdinalIgnoreCase) >= 0) return JointRole.Leg;
            return JointRole.Other;
        }
        private static void ConfigurePartDefaults(RagdollPartHealth part, string partName)
        {
            string value = partName.ToLowerInvariant();
            if (value.Contains("head"))
                part.Configure(RagdollPartType.Head, 40f, 2f, 1.25f, .9f, 1.4f, true);
            else if (value.Contains("torso"))
                part.Configure(RagdollPartType.Torso, 100f, 2f, 1f, 1f, 1f, false);
            else if (value.Contains("arm"))
                part.Configure(RagdollPartType.Arm, 45f, 1f, .9f, 1.2f, .85f, false);
            else if (value.Contains("leg"))
                part.Configure(RagdollPartType.Leg, 60f, 1f, .85f, .9f, .8f, false);
            else
                part.Configure(RagdollPartType.Other, 50f, 1f, 1f, 1f, 1f, false);
        }

        private void OnValidate()
        {
            fallbackLimbHealth = Mathf.Max(1f, fallbackLimbHealth);
            jointBreakStress = Mathf.Max(0f, jointBreakStress);
            jointStressDamageRate = Mathf.Max(0f, jointStressDamageRate);
        }
    }
}
