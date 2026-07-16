#if UNITY_EDITOR
using KickTheBuddy.Physics;
using KickTheBuddy.VFX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KickTheBuddy.Editor
{
    /// <summary>Creates and wires the project's single shared ragdoll hit particle.</summary>
    public static class RagdollVFXSetupEditor
    {
        private const string Root = "Assets/GameData/Prefabs/VFX";
        private const string ProfilePath = "Assets/GameData/Materials/Ragdoll VFX Profile.asset";
        private const string ScenePath = "Assets/GameData/Scene/RagdollSandbox.unity";

        public static void Install()
        {
            EnsureFolder("Assets/GameData/Prefabs", "VFX");
            ParticleSystem hit = CreateHitParticle();

            RagdollVFXProfile profile = AssetDatabase.LoadAssetAtPath<RagdollVFXProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<RagdollVFXProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            SerializedObject profileObject = new SerializedObject(profile);
            profileObject.FindProperty("hitPrefab").objectReferenceValue = hit;
            profileObject.ApplyModifiedPropertiesWithoutUndo();

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            RagdollController controller = Object.FindObjectOfType<RagdollController>();
            if (controller == null)
                throw new System.InvalidOperationException("RagdollController not found in RagdollSandbox.");

            RagdollVFXController vfx = controller.GetComponent<RagdollVFXController>();
            if (vfx == null) vfx = controller.gameObject.AddComponent<RagdollVFXController>();

            SerializedObject vfxObject = new SerializedObject(vfx);
            vfxObject.FindProperty("profile").objectReferenceValue = profile;
            vfxObject.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(controller.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        private static ParticleSystem CreateHitParticle()
        {
            const string path = Root + "/VFX_Ragdoll_Hit.prefab";
            ParticleSystem existing = AssetDatabase.LoadAssetAtPath<ParticleSystem>(path);
            if (existing != null) return existing;

            GameObject go = new GameObject("VFX_Ragdoll_Hit", typeof(ParticleSystem));
            ParticleSystem system = go.GetComponent<ParticleSystem>();

            ParticleSystem.MainModule main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = .15f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.16f, .24f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 2.8f);
            main.startSize = .045f;
            main.maxParticles = 8;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.None;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;

            ParticleSystem.ColorOverLifetimeModule colors = system.colorOverLifetime;
            colors.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(new Color(1f, .3f, .08f), .35f),
                    new GradientColorKey(new Color(.3f, .05f, .01f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(.75f, .5f),
                    new GradientAlphaKey(0f, 1f)
                });
            colors.color = gradient;

            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(
                1f, AnimationCurve.EaseInOut(0f, 1f, 1f, .15f));

            ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.sortingOrder = 100;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab.GetComponent<ParticleSystem>();
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }
    }
}
#endif
