#if UNITY_EDITOR
using KickTheBuddy.Physics;
using UnityEditor;
using UnityEngine;

namespace KickTheBuddy.Editor
{
    public static class RagdollProfilePresetFactory
    {
        private const string Root = "Assets/GameData/Materials/Resources/Ragdoll Profiles";

        [MenuItem("Tools/Ragdoll/Create Physics Profile Presets")]
        public static void CreatePresets()
        {
            EnsureFolder("Assets/GameData/Materials", "Resources");
            EnsureFolder("Assets/GameData/Materials/Resources", "Ragdoll Profiles");
            Create("Solid Robot", RagdollProfileType.SolidRobot, 4f, 2.5f, 3.5f, true, 1.1f, 180f, 8f,
                new Color(1f, 0.72f, 0.12f), CreateMaterial("Robot Metal", 0.05f, 0.75f));
            Create("Jelly Character", RagdollProfileType.Jelly, 0.55f, 0.04f, 0.08f, true, 0.9f, 42f, 0.8f,
                new Color(0.35f, 1f, 0.65f), CreateMaterial("Jelly Bounce", 0.72f, 0.18f));
            Create("Magic Doll", RagdollProfileType.MagicDoll, 0.25f, 0.12f, 5f, true, 0.06f, 0f, 6f,
                new Color(0.72f, 0.28f, 1f), CreateMaterial("Magic Cloth", 0.08f, 0.45f));
            Create("Galaxy Space", RagdollProfileType.GalaxySpace, 0.8f, 0.005f, 0.03f, false, 0f, 0f, 0.05f,
                new Color(0.25f, 0.85f, 1f), CreateMaterial("Space Drift", 0.45f, 0.05f));
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Created/updated ragdoll physics profiles in " + Root);
        }

        private static void Create(string displayName, RagdollProfileType type, float mass, float drag,
            float angularDrag, bool gravity, float gravityModifier, float spring, float damping,
            Color damageColor, PhysicsMaterial2D material)
        {
            string path = $"{Root}/{displayName}.asset";
            RagdollProfile profile = AssetDatabase.LoadAssetAtPath<RagdollProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<RagdollProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            SerializedObject data = new SerializedObject(profile);
            data.FindProperty("profileName").stringValue = displayName;
            data.FindProperty("profileType").enumValueIndex = (int)type;
            data.FindProperty("massMultiplier").floatValue = mass;
            data.FindProperty("linearDrag").floatValue = drag;
            data.FindProperty("angularDrag").floatValue = angularDrag;
            data.FindProperty("useGravity").boolValue = gravity;
            data.FindProperty("gravityScaleModifier").floatValue = gravityModifier;
            data.FindProperty("jointSpringForce").floatValue = spring;
            data.FindProperty("jointDamping").floatValue = damping;
            data.FindProperty("damageParticleColor").colorValue = damageColor;
            data.FindProperty("customPhysicsMaterial").objectReferenceValue = material;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(profile);
        }

        private static PhysicsMaterial2D CreateMaterial(string name, float bounce, float friction)
        {
            string path = $"{Root}/{name}.physicsMaterial2D";
            PhysicsMaterial2D material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(path);
            if (material == null)
            {
                material = new PhysicsMaterial2D(name);
                AssetDatabase.CreateAsset(material, path);
            }
            material.bounciness = bounce;
            material.friction = friction;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child);
        }
    }
}
#endif
