using UnityEngine;

namespace KickTheBuddy.Physics
{
    /// <summary>Owns category selection and all ScriptableObject profile calculations.</summary>
    [DisallowMultipleComponent]
    public sealed class RagdollProfileController2D : MonoBehaviour
    {
        [SerializeField] private RagdollCategory category = RagdollCategory.SolidRobot;
        [SerializeField] private bool applyCategoryOnInitialize = true;
        [SerializeField] private RagdollProfile activeProfile;

        private RagdollController owner;
        private RagdollRigController2D rig;
        private PhysicsMaterial2D runtimeJellyMaterial;
        private float profileDurabilityMultiplier = 1f;
        private float levelDurabilityMultiplier = 1f;

        public RagdollProfile ActiveProfile => activeProfile;
        public RagdollCategory Category => category;
        public Color DamageParticleColor => activeProfile != null ? activeProfile.DamageParticleColor : Color.white;

        internal void Initialize(RagdollController controller, RagdollRigController2D ragdollRig)
        {
            owner = controller;
            rig = ragdollRig;

            if (applyCategoryOnInitialize)
            {
                RagdollProfile bundled = LoadCategoryProfile(category);
                if (bundled != null) activeProfile = bundled;
            }

            if (activeProfile != null) ApplyProfile(activeProfile);
        }

        public void SelectCategory(RagdollCategory newCategory)
        {
            category = newCategory;
            RagdollProfile profile = LoadCategoryProfile(newCategory);
            if (profile == null)
            {
                Debug.LogWarning("No bundled ragdoll profile was found for " + newCategory + ".", this);
                return;
            }

            ApplyProfile(profile);
        }

        public void ApplyProfile(RagdollProfile profile)
        {
            activeProfile = profile;
            rig.RestoreAuthoredPhysics();

            if (profile == null)
            {
                profileDurabilityMultiplier = 1f;
                rig.SetDurabilityMultiplier(levelDurabilityMultiplier);
                owner?.NotifyProfileApplied(null);
                return;
            }

            float mass = profile.MassMultiplier;
            float linearDrag = profile.LinearDrag;
            float angularDrag = profile.AngularDrag;
            float gravity = profile.UseGravity ? profile.GravityScaleModifier : 0f;

            switch (profile.ProfileType)
            {
                case RagdollProfileType.SolidRobot:
                    // A heavy body needs stronger gravity and low linear resistance;
                    // mass alone does not increase gravitational acceleration in Physics2D.
                    mass = Mathf.Max(4f, mass);
                    gravity = Mathf.Max(2.2f, gravity);
                    linearDrag = Mathf.Min(.65f, linearDrag);
                    angularDrag = Mathf.Min(1.5f, angularDrag);
                    break;
                case RagdollProfileType.Jelly:
                    mass = Mathf.Min(.7f, mass);
                    linearDrag = Mathf.Min(.1f, linearDrag);
                    angularDrag = Mathf.Min(.15f, angularDrag);
                    break;
                case RagdollProfileType.MagicDoll:
                    mass = Mathf.Min(.35f, mass);
                    gravity = Mathf.Min(.08f, gravity);
                    angularDrag = Mathf.Max(4f, angularDrag);
                    break;
                case RagdollProfileType.GalaxySpace:
                    gravity = 0f;
                    linearDrag = Mathf.Min(.01f, linearDrag);
                    angularDrag = Mathf.Min(.05f, angularDrag);
                    break;
            }

            var bodies = rig.Bodies;
            for (int i = 0; i < bodies.Count; i++)
            {
                RagdollRigController2D.BodyRuntime item = bodies[i];
                if (item.Body == null) continue;
                item.Body.mass = Mathf.Max(.01f, item.AuthoredMass * mass);
                item.Body.drag = linearDrag;
                item.Body.angularDrag = angularDrag;
                item.Body.gravityScale = item.AuthoredGravityScale * gravity;
                item.Body.WakeUp();
            }

            PhysicsMaterial2D material = ResolveMaterial(profile);
            var colliders = rig.Colliders;
            for (int i = 0; i < colliders.Count; i++)
            {
                RagdollRigController2D.ColliderRuntime item = colliders[i];
                if (item.Collider != null)
                    item.Collider.sharedMaterial = material != null ? material : item.AuthoredMaterial;
            }

            var joints = rig.Joints;
            for (int i = 0; i < joints.Count; i++)
            {
                HingeJoint2D joint = joints[i].Joint;
                if (joint == null) continue;

                if (profile.ProfileType == RagdollProfileType.SolidRobot)
                {
                    JointAngleLimits2D limits = joint.limits;
                    limits.min = -15f;
                    limits.max = 15f;
                    joint.limits = limits;
                    joint.useLimits = true;
                }
                else if (profile.ProfileType == RagdollProfileType.Jelly ||
                         profile.ProfileType == RagdollProfileType.MagicDoll ||
                         profile.ProfileType == RagdollProfileType.GalaxySpace)
                {
                    joint.useLimits = false;
                }
            }

            profileDurabilityMultiplier = ResolveDurability(profile.ProfileType);
            rig.SetDurabilityMultiplier(profileDurabilityMultiplier * levelDurabilityMultiplier);
            owner?.NotifyProfileApplied(profile);
        }

        internal void SetLevelDurabilityMultiplier(float multiplier)
        {
            levelDurabilityMultiplier = Mathf.Max(.1f, multiplier);
            rig.SetDurabilityMultiplier(profileDurabilityMultiplier * levelDurabilityMultiplier);
        }

        private PhysicsMaterial2D ResolveMaterial(RagdollProfile profile)
        {
            if (profile.CustomPhysicsMaterial != null) return profile.CustomPhysicsMaterial;
            if (profile.ProfileType != RagdollProfileType.Jelly) return null;

            if (runtimeJellyMaterial == null)
            {
                runtimeJellyMaterial = new PhysicsMaterial2D("Runtime Jelly Material")
                {
                    bounciness = .7f,
                    friction = .2f
                };
            }

            return runtimeJellyMaterial;
        }

        private static float ResolveDurability(RagdollProfileType type)
        {
            switch (type)
            {
                case RagdollProfileType.SolidRobot: return 2.25f;
                case RagdollProfileType.Jelly: return .8f;
                case RagdollProfileType.MagicDoll: return .65f;
                case RagdollProfileType.GalaxySpace: return .75f;
                default: return 1f;
            }
        }

        private static RagdollProfile LoadCategoryProfile(RagdollCategory selectedCategory)
        {
            string assetName;
            switch (selectedCategory)
            {
                case RagdollCategory.JellyCharacter: assetName = "Jelly Character"; break;
                case RagdollCategory.GalaxySpace: assetName = "Galaxy Space"; break;
                default: assetName = "Solid Robot"; break;
            }

            return Resources.Load<RagdollProfile>("Ragdoll Profiles/" + assetName);
        }

        private void OnDestroy()
        {
            if (runtimeJellyMaterial != null) Destroy(runtimeJellyMaterial);
        }
    }
}
