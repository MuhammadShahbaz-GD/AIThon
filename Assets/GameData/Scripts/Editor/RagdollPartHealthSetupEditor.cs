#if UNITY_EDITOR
using KickTheBuddy.Physics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KickTheBuddy.Editor
{
    /// <summary>Authors per-part health properties and the shared reaction controller.</summary>
    public static class RagdollPartHealthSetupEditor
    {
        private const string ScenePath = "Assets/GameData/Scene/RagdollSandbox.unity";

        [MenuItem("Tools/Ragdoll/Setup Part Health And Reactions")]
        public static void Install()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            RagdollController controller = null;
            RagdollController[] controllers = Resources.FindObjectsOfTypeAll<RagdollController>();
            for (int i = 0; i < controllers.Length; i++)
            {
                if (controllers[i].gameObject.scene == scene) { controller = controllers[i]; break; }
            }
            if (controller == null)
                throw new System.InvalidOperationException("RagdollController was not found.");

            controller.gameObject.SetActive(true);

            Rigidbody2D[] bodies = controller.GetComponentsInChildren<Rigidbody2D>(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                Rigidbody2D body = bodies[i];
                RagdollPartHealth health = body.GetComponent<RagdollPartHealth>();
                if (health == null) health = Undo.AddComponent<RagdollPartHealth>(body.gameObject);
                ConfigureDefaults(health, body.name);
                EditorUtility.SetDirty(body.gameObject);
            }

            if (controller.GetComponent<RagdollDamageManager>() == null)
                Undo.AddComponent<RagdollDamageManager>(controller.gameObject);
            if (controller.GetComponent<RagdollAnimationController>() == null)
                Undo.AddComponent<RagdollAnimationController>(controller.gameObject);

            RagdollLifeVisuals legacy = controller.GetComponent<RagdollLifeVisuals>();
            if (legacy != null) legacy.enabled = false;

            EditorUtility.SetDirty(controller.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Configured per-part health, critical head death, flexibility, and ragdoll reactions.");
        }

        private static void ConfigureDefaults(RagdollPartHealth part, string partName)
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
            EditorUtility.SetDirty(part);
        }
    }
}
#endif
