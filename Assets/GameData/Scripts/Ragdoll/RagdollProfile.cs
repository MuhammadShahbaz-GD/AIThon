using UnityEngine;

namespace KickTheBuddy.Physics
{
    /// <summary>The three production character categories exposed by RagdollController.</summary>
    public enum RagdollCategory
    {
        SolidRobot,
        JellyCharacter,
        GalaxySpace
    }

    public enum RagdollProfileType
    {
        Custom,
        SolidRobot,
        Jelly,
        MagicDoll,
        GalaxySpace
    }

    /// <summary>
    /// Reusable, immutable-at-runtime physics tuning for a ragdoll character.
    /// Create profiles through Assets/Create/Ragdoll/Physics Profile, select a preset type,
    /// then assign the asset to RagdollController or call ApplyProfile at runtime.
    /// </summary>
    [CreateAssetMenu(fileName = "RagdollProfile", menuName = "Ragdoll/Physics Profile")]
    public sealed class RagdollProfile : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string profileName = "Custom Ragdoll";
        [SerializeField] private RagdollProfileType profileType = RagdollProfileType.Custom;
        [SerializeField] private Color damageParticleColor = Color.white;

        [Header("Body Physics")]
        [Min(0.01f)] [SerializeField] private float massMultiplier = 1f;
        [Min(0f)] [SerializeField] private float linearDrag = 0.15f;
        [Min(0f)] [SerializeField] private float angularDrag = 0.2f;
        [SerializeField] private bool useGravity = true;
        [Min(0f)] [SerializeField] private float gravityScaleModifier = 1f;

        [Header("Joint Drive / Spring")]
        [Min(0f)] [SerializeField] private float jointSpringForce = 95f;
        [Min(0f)] [SerializeField] private float jointDamping = 3f;

        [Header("Surface")]
        [SerializeField] private PhysicsMaterial2D customPhysicsMaterial;

        public string ProfileName => profileName;
        public RagdollProfileType ProfileType => profileType;
        public Color DamageParticleColor => damageParticleColor;
        public float MassMultiplier => massMultiplier;
        public float LinearDrag => linearDrag;
        public float AngularDrag => angularDrag;
        public bool UseGravity => useGravity;
        public float GravityScaleModifier => gravityScaleModifier;
        public float JointSpringForce => jointSpringForce;
        public float JointDamping => jointDamping;
        public PhysicsMaterial2D CustomPhysicsMaterial => customPhysicsMaterial;

        private void OnValidate()
        {
            massMultiplier = Mathf.Max(0.01f, massMultiplier);
            linearDrag = Mathf.Max(0f, linearDrag);
            angularDrag = Mathf.Max(0f, angularDrag);
            gravityScaleModifier = Mathf.Max(0f, gravityScaleModifier);
            jointSpringForce = Mathf.Max(0f, jointSpringForce);
            jointDamping = Mathf.Max(0f, jointDamping);
        }
    }
}
