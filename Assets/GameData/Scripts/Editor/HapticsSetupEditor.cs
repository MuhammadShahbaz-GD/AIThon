#if UNITY_EDITOR
using KickTheBuddy.Haptics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KickTheBuddy.Editor
{
    public static class HapticsSetupEditor
    {
        public static void Install()
        {
            const string scenePath = "Assets/GameData/Scene/RagdollSandbox.unity";
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            GameObject systems = GameObject.Find("Game Systems");
            if (systems == null) throw new System.InvalidOperationException("Game Systems was not found. Build gameplay first.");
            if (systems.GetComponent<HapticsManager>() == null) systems.AddComponent<HapticsManager>();
            if (systems.GetComponent<GameplayHapticsAdapter>() == null) systems.AddComponent<GameplayHapticsAdapter>();
            EditorUtility.SetDirty(systems); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        }
    }

    [CustomEditor(typeof(HapticsManager))]
    public sealed class HapticsManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("SOLID haptics facade. Gameplay talks to IHapticsService; Android/iOS details stay inside the platform driver.", MessageType.Info);
            DrawDefaultInspector();
            HapticsManager manager = (HapticsManager)target;
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Test Light Impact")) manager.Impact(HapticImpact.Light);
                if (GUILayout.Button("Test Heavy Impact")) manager.Impact(HapticImpact.Heavy);
                if (GUILayout.Button("Test Success")) manager.Notification(HapticNotification.Success);
            }
        }
    }
}
#endif
