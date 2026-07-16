#if UNITY_EDITOR
using KickTheBuddy.Physics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KickTheBuddy.Editor
{
    public static class RagdollInputSetupEditor
    {
        public static void Install()
        {
            const string path = "Assets/GameData/Scene/RagdollSandbox.unity";
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            RagdollController controller = Object.FindObjectOfType<RagdollController>();
            if (controller == null) throw new System.InvalidOperationException("RagdollController not found.");
            if (controller.GetComponent<RagdollInputManager>() == null) controller.gameObject.AddComponent<RagdollInputManager>();
            EditorUtility.SetDirty(controller.gameObject); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        }
    }
}
#endif
