#if UNITY_EDITOR
using KickTheBuddy.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KickTheBuddy.Editor
{
    public static class GameSetupEditor
    {
        public static void BuildPlayableGame()
        {
            const string scenePath = "Assets/GameData/Scene/RagdollSandbox.unity";
            const string dataFolder = "Assets/GameData/Materials/Gameplay";
            EnsureFolder("Assets/GameData/Materials", "Gameplay");
            LevelDefinition level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(dataFolder + "/Level_01.asset");
            if (level == null) { level = ScriptableObject.CreateInstance<LevelDefinition>(); AssetDatabase.CreateAsset(level, dataFolder + "/Level_01.asset"); }
            LevelCatalog catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(dataFolder + "/Level Catalog.asset");
            if (catalog == null) { catalog = ScriptableObject.CreateInstance<LevelCatalog>(); AssetDatabase.CreateAsset(catalog, dataFolder + "/Level Catalog.asset"); }
            SerializedObject catalogObject = new SerializedObject(catalog); SerializedProperty list = catalogObject.FindProperty("levels"); list.arraySize = 1; list.GetArrayElementAtIndex(0).objectReferenceValue = level; catalogObject.ApplyModifiedPropertiesWithoutUndo();

            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            GameObject root = GameObject.Find("Game Systems"); if (root == null) root = new GameObject("Game Systems");
            Add<GameSaveManager>(root); Add<SoundManager>(root); LevelsManager levels = Add<LevelsManager>(root); Add<GameplayManager>(root); Add<GameBootstrapper>(root);
            SerializedObject levelsObject = new SerializedObject(levels); levelsObject.FindProperty("catalog").objectReferenceValue = catalog; levelsObject.ApplyModifiedPropertiesWithoutUndo();
            if (Object.FindObjectOfType<GameplayHUD>() == null) CreateHUD();
            EditorUtility.SetDirty(root); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) }; AssetDatabase.SaveAssets();
        }
        private static T Add<T>(GameObject root) where T : Component { T value = root.GetComponent<T>(); return value != null ? value : root.AddComponent<T>(); }
        private static void EnsureFolder(string parent, string child) { string path = parent + "/" + child; if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child); }
        private static void CreateHUD()
        {
            GameObject canvasObject = new GameObject("Gameplay Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(GameplayHUD)); Canvas canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1080, 1920); scaler.matchWidthOrHeight = .5f;
            GameObject panel = new GameObject("Gameplay Panel", typeof(RectTransform)); panel.transform.SetParent(canvasObject.transform, false); Stretch(panel.GetComponent<RectTransform>());
            Text level = Label(panel.transform, "Level", new Vector2(0, -55), 38, TextAnchor.UpperCenter); Text objective = Label(panel.transform, "Objective", new Vector2(0, -110), 26, TextAnchor.UpperCenter); Text score = Label(panel.transform, "Score", new Vector2(-360, -55), 34, TextAnchor.UpperLeft);
            SerializedObject hud = new SerializedObject(canvasObject.GetComponent<GameplayHUD>()); hud.FindProperty("levelText").objectReferenceValue = level; hud.FindProperty("objectiveText").objectReferenceValue = objective; hud.FindProperty("scoreText").objectReferenceValue = score; hud.FindProperty("gameplayPanel").objectReferenceValue = panel; hud.ApplyModifiedPropertiesWithoutUndo();
        }
        private static Text Label(Transform parent, string name, Vector2 position, int size, TextAnchor alignment) { GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false); RectTransform rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f); rect.sizeDelta = new Vector2(700, 60); rect.anchoredPosition = position; Text text = go.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.fontSize = size; text.alignment = alignment; text.color = Color.white; text.text = name; return text; }
        private static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    }
}
#endif
