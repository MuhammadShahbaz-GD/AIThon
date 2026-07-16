#if UNITY_EDITOR
using KickTheBuddy.Physics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KickTheBuddy.Editor
{
    /// <summary>Serializes the focused ragdoll modules on the existing Buddy facade.</summary>
    public static class ModularRagdollSetupEditor
    {
        private const string ScenePath = "Assets/GameData/Scene/RagdollSandbox.unity";

        [MenuItem("Tools/Ragdoll/Setup Modular Controller")]
        public static void Install()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            RagdollController controller = FindSceneController(scene);
            if (controller == null)
                throw new System.InvalidOperationException("RagdollController was not found.");

            controller.gameObject.SetActive(true);
            RagdollRigController2D rig = GetOrAdd<RagdollRigController2D>(controller.gameObject);
            RagdollPoseController2D pose = GetOrAdd<RagdollPoseController2D>(controller.gameObject);
            RagdollProfileController2D profiles = GetOrAdd<RagdollProfileController2D>(controller.gameObject);
            RagdollStateController2D state = GetOrAdd<RagdollStateController2D>(controller.gameObject);
            RagdollDamageManager damage = GetOrAdd<RagdollDamageManager>(controller.gameObject);

            SerializedObject serializedController = new SerializedObject(controller);
            serializedController.FindProperty("rig").objectReferenceValue = rig;
            serializedController.FindProperty("pose").objectReferenceValue = pose;
            serializedController.FindProperty("state").objectReferenceValue = state;
            serializedController.FindProperty("profiles").objectReferenceValue = profiles;
            serializedController.FindProperty("damageManager").objectReferenceValue = damage;
            serializedController.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(controller.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Configured modular ragdoll facade, rig, profile, pose, state, and damage components.");
        }

        private static RagdollController FindSceneController(UnityEngine.SceneManagement.Scene scene)
        {
            RagdollController[] controllers = Resources.FindObjectsOfTypeAll<RagdollController>();
            for (int i = 0; i < controllers.Length; i++)
                if (controllers[i].gameObject.scene == scene)
                    return controllers[i];
            return null;
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : Undo.AddComponent<T>(target);
        }
    }
}
#endif
