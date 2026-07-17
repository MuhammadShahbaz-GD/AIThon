#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using KickTheBuddy.Gameplay;
using KickTheBuddy.Haptics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KickTheBuddy.Editor
{
    /// <summary>Idempotent authoring and validation for Splash -> Main Menu -> Gameplay.</summary>
    public static class GameFlowSetupEditor
    {
        private const string SplashPath = "Assets/GameData/Scene/Splash.unity";
        private const string MenuPath = "Assets/GameData/Scene/MainMenu.unity";
        private const string GameplayPath = "Assets/GameData/Scene/RagdollSandbox.unity";
        private const string CatalogPath = "Assets/GameData/Materials/Gameplay/Level Catalog.asset";

        private static readonly Color Background = new Color(.035f, .045f, .085f, 1f);
        private static readonly Color Panel = new Color(.075f, .095f, .16f, .96f);
        private static readonly Color Blue = new Color(.18f, .48f, 1f, 1f);
        private static readonly Color Cyan = new Color(.16f, .92f, .95f, 1f);
        private static readonly Color Muted = new Color(.42f, .48f, .60f, 1f);

        [MenuItem("Tools/Game/Build Full Game Flow")]
        public static void BuildFromMenu() => Build(false);

        public static void BuildFullGameFlowBatch() => Build(true);

        public static void ValidateFullGameFlowBatch()
        {
            try
            {
                ValidateBuildSettings();
                ValidateSplash();
                ValidateMenu();
                ValidateGameplay();
                Debug.Log("FULL_GAME_FLOW_VALIDATION_OK: Splash -> MainMenu -> Gameplay, continue data, settings, and return-to-menu wiring are valid.");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        private static void Build(bool exitWhenDone)
        {
            try
            {
                LevelCatalog catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(CatalogPath);
                if (catalog == null || catalog.Count == 0)
                    throw new InvalidOperationException("Level Catalog is missing or empty. Run the gameplay setup first.");

                BuildSplash(catalog);
                BuildMenu(catalog);
                ConfigureGameplay(catalog);
                EditorBuildSettings.scenes = CreateBuildSceneList(catalog);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                EditorSceneManager.OpenScene(SplashPath, OpenSceneMode.Single);
                Debug.Log("FULL_GAME_FLOW_BUILD_OK: Splash, MainMenu, persistent services, saved Continue flow, and gameplay return path created.");
                if (exitWhenDone && Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (exitWhenDone && Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        private static void BuildSplash(LevelCatalog catalog)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateCamera();
            ConfigureSystems(catalog, false);
            Canvas canvas = CreateCanvas("Splash Canvas");
            CreatePanel(canvas.transform, "Background", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Background);

            Text title = CreateText(canvas.transform, "Title", "RAGDOLL\nSMASH", 92, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            SetRect(title.rectTransform, new Vector2(.08f, .48f), new Vector2(.92f, .72f), Vector2.zero, Vector2.zero);
            Text subtitle = CreateText(canvas.transform, "Subtitle", "PHYSICS  •  CHAOS  •  CANDY", 25, FontStyle.Bold, TextAnchor.MiddleCenter, Cyan);
            SetRect(subtitle.rectTransform, new Vector2(.08f, .40f), new Vector2(.92f, .48f), Vector2.zero, Vector2.zero);

            Image track = CreateImage(canvas.transform, "Loading Track", new Color(1f, 1f, 1f, .12f));
            SetRect(track.rectTransform, new Vector2(.18f, .22f), new Vector2(.82f, .235f), Vector2.zero, Vector2.zero);
            Image fill = CreateImage(track.transform, "Loading Fill", Blue);
            SetRect(fill.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            Text status = CreateText(canvas.transform, "Status", "LOADING", 20, FontStyle.Bold, TextAnchor.MiddleCenter, Muted);
            SetRect(status.rectTransform, new Vector2(.2f, .16f), new Vector2(.8f, .21f), Vector2.zero, Vector2.zero);

            SplashScreenController splash = canvas.gameObject.AddComponent<SplashScreenController>();
            SerializedObject serialized = new SerializedObject(splash);
            serialized.FindProperty("minimumDisplaySeconds").floatValue = 1.6f;
            serialized.FindProperty("progressFill").objectReferenceValue = fill;
            serialized.FindProperty("statusText").objectReferenceValue = status;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene, SplashPath);
        }

        private static void BuildMenu(LevelCatalog catalog)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateCamera();
            ConfigureSystems(catalog, false);
            EnsureEventSystem();
            Canvas canvas = CreateCanvas("Main Menu Canvas");
            CreatePanel(canvas.transform, "Background", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Background);

            MainMenuController menu = canvas.gameObject.AddComponent<MainMenuController>();
            Text title = CreateText(canvas.transform, "Game Title", "RAGDOLL SMASH", 70, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            SetRect(title.rectTransform, new Vector2(.08f, .82f), new Vector2(.92f, .94f), Vector2.zero, Vector2.zero);
            Text subtitle = CreateText(canvas.transform, "Tagline", "MAKE A MESS.  BEAT YOUR BEST.", 22, FontStyle.Bold, TextAnchor.MiddleCenter, Cyan);
            SetRect(subtitle.rectTransform, new Vector2(.08f, .77f), new Vector2(.92f, .82f), Vector2.zero, Vector2.zero);

            GameObject stats = CreatePanel(canvas.transform, "Saved Stats", new Vector2(.08f, .66f), new Vector2(.92f, .75f), Vector2.zero, Vector2.zero, Panel);
            Text totalScore = CreateText(stats.transform, "Total Score", "SCORE  0", 27, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            SetRect(totalScore.rectTransform, new Vector2(.06f, 0f), new Vector2(.52f, 1f), Vector2.zero, Vector2.zero);
            Text coins = CreateText(stats.transform, "Coins", "COINS  0", 27, FontStyle.Bold, TextAnchor.MiddleRight, new Color(1f, .78f, .22f));
            SetRect(coins.rectTransform, new Vector2(.5f, 0f), new Vector2(.94f, 1f), Vector2.zero, Vector2.zero);

            GameObject levelCard = CreatePanel(canvas.transform, "Level Card", new Vector2(.08f, .42f), new Vector2(.92f, .63f), Vector2.zero, Vector2.zero, Panel);
            Text small = CreateText(levelCard.transform, "Level Caption", "SELECTED ROOM", 18, FontStyle.Bold, TextAnchor.MiddleCenter, Muted);
            SetRect(small.rectTransform, new Vector2(.2f, .69f), new Vector2(.8f, .93f), Vector2.zero, Vector2.zero);
            Text levelName = CreateText(levelCard.transform, "Level Name", "TRAINING ROOM", 40, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            SetRect(levelName.rectTransform, new Vector2(.18f, .32f), new Vector2(.82f, .72f), Vector2.zero, Vector2.zero);
            Text best = CreateText(levelCard.transform, "Best Score", "BEST  0", 22, FontStyle.Normal, TextAnchor.MiddleCenter, Cyan);
            SetRect(best.rectTransform, new Vector2(.2f, .08f), new Vector2(.8f, .34f), Vector2.zero, Vector2.zero);
            Button previous = CreateButton(levelCard.transform, "Previous Level", "‹", Muted);
            SetRect(previous.GetComponent<RectTransform>(), new Vector2(.035f, .27f), new Vector2(.18f, .73f), Vector2.zero, Vector2.zero);
            Button next = CreateButton(levelCard.transform, "Next Level", "›", Muted);
            SetRect(next.GetComponent<RectTransform>(), new Vector2(.82f, .27f), new Vector2(.965f, .73f), Vector2.zero, Vector2.zero);

            Button play = CreateButton(canvas.transform, "Play Button", "PLAY", Blue);
            SetRect(play.GetComponent<RectTransform>(), new Vector2(.14f, .27f), new Vector2(.86f, .36f), Vector2.zero, Vector2.zero);
            Text playLabel = play.GetComponentInChildren<Text>();
            Button settings = CreateButton(canvas.transform, "Settings Button", "SETTINGS", new Color(.14f, .17f, .25f));
            SetRect(settings.GetComponent<RectTransform>(), new Vector2(.14f, .18f), new Vector2(.60f, .245f), Vector2.zero, Vector2.zero);
            Button quit = CreateButton(canvas.transform, "Quit Button", "QUIT", new Color(.14f, .17f, .25f));
            SetRect(quit.GetComponent<RectTransform>(), new Vector2(.62f, .18f), new Vector2(.86f, .245f), Vector2.zero, Vector2.zero);
            Text footer = CreateText(canvas.transform, "Footer", "PROGRESS SAVES AUTOMATICALLY", 17, FontStyle.Bold, TextAnchor.MiddleCenter, Muted);
            SetRect(footer.rectTransform, new Vector2(.1f, .08f), new Vector2(.9f, .13f), Vector2.zero, Vector2.zero);

            GameObject settingsPanel = CreatePanel(canvas.transform, "Settings Panel", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(.02f, .025f, .05f, .97f));
            GameObject settingsCard = CreatePanel(settingsPanel.transform, "Settings Card", new Vector2(.1f, .27f), new Vector2(.9f, .73f), Vector2.zero, Vector2.zero, Panel);
            Text settingsTitle = CreateText(settingsCard.transform, "Title", "SETTINGS", 48, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            SetRect(settingsTitle.rectTransform, new Vector2(.08f, .77f), new Vector2(.92f, .95f), Vector2.zero, Vector2.zero);
            Toggle music = CreateToggle(settingsCard.transform, "Music Toggle", "MUSIC", .61f);
            Toggle sound = CreateToggle(settingsCard.transform, "Sound Toggle", "SOUND", .45f);
            Toggle vibration = CreateToggle(settingsCard.transform, "Vibration Toggle", "VIBRATION", .29f);
            Button close = CreateButton(settingsCard.transform, "Close Settings", "DONE", Blue);
            SetRect(close.GetComponent<RectTransform>(), new Vector2(.16f, .06f), new Vector2(.84f, .20f), Vector2.zero, Vector2.zero);

            SerializedObject serialized = new SerializedObject(menu);
            Assign(serialized, "playButtonText", playLabel);
            Assign(serialized, "levelText", levelName);
            Assign(serialized, "bestScoreText", best);
            Assign(serialized, "totalScoreText", totalScore);
            Assign(serialized, "coinsText", coins);
            Assign(serialized, "playButton", play);
            Assign(serialized, "previousLevelButton", previous);
            Assign(serialized, "nextLevelButton", next);
            Assign(serialized, "settingsButton", settings);
            Assign(serialized, "closeSettingsButton", close);
            Assign(serialized, "quitButton", quit);
            Assign(serialized, "settingsPanel", settingsPanel);
            Assign(serialized, "musicToggle", music);
            Assign(serialized, "soundToggle", sound);
            Assign(serialized, "vibrationToggle", vibration);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            settingsPanel.SetActive(false);
            EditorSceneManager.SaveScene(scene, MenuPath);
        }

        private static void ConfigureGameplay(LevelCatalog catalog)
        {
            Scene scene = EditorSceneManager.OpenScene(GameplayPath, OpenSceneMode.Single);
            ConfigureSystems(catalog, true);
            GameplayHUD hud = UnityEngine.Object.FindObjectOfType<GameplayHUD>(true);
            if (hud == null) throw new InvalidOperationException("GameplayHUD is missing from RagdollSandbox.");
            SerializedObject serialized = new SerializedObject(hud);
            GameObject resultPanel = serialized.FindProperty("resultPanel").objectReferenceValue as GameObject;
            if (resultPanel == null) throw new InvalidOperationException("GameplayHUD resultPanel is not assigned.");
            Transform old = resultPanel.transform.Find("Main Menu Button");
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            Button menuButton = CreateButton(resultPanel.transform, "Main Menu Button", "MAIN MENU", new Color(.15f, .18f, .26f));
            RectTransform rect = menuButton.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(420f, 92f);
            rect.anchoredPosition = new Vector2(0f, -235f);
            Assign(serialized, "mainMenuButton", menuButton);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(hud);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static GameObject ConfigureSystems(LevelCatalog catalog, bool directGameplay)
        {
            GameObject root = GameObject.Find("Game Systems") ?? new GameObject("Game Systems");
            Add<GameSaveManager>(root);
            Add<SoundManager>(root);
            LevelsManager levels = Add<LevelsManager>(root);
            Add<GameplayManager>(root);
            GameFlowController flow = Add<GameFlowController>(root);
            GameBootstrapper bootstrap = Add<GameBootstrapper>(root);
            Add<HapticsManager>(root);
            Add<GameplayHapticsAdapter>(root);

            SerializedObject levelsObject = new SerializedObject(levels);
            Assign(levelsObject, "catalog", catalog);
            levelsObject.ApplyModifiedPropertiesWithoutUndo();
            SerializedObject bootstrapObject = new SerializedObject(bootstrap);
            bootstrapObject.FindProperty("startGameplayImmediately").boolValue = directGameplay;
            bootstrapObject.ApplyModifiedPropertiesWithoutUndo();
            SerializedObject flowObject = new SerializedObject(flow);
            flowObject.FindProperty("splashSceneName").stringValue = "Splash";
            flowObject.FindProperty("mainMenuSceneName").stringValue = "MainMenu";
            flowObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(root);
            return root;
        }

        private static void ValidateBuildSettings()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            if (scenes.Length < 3 || scenes[0].path != SplashPath || scenes[1].path != MenuPath)
                throw new InvalidOperationException("Build Settings must start with Splash and MainMenu.");
            LevelCatalog catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(CatalogPath);
            if (catalog == null) throw new InvalidOperationException("Level Catalog is missing.");
            for (int i = 0; i < catalog.Count; i++)
            {
                LevelDefinition level = catalog.Get(i);
                bool found = false;
                for (int j = 2; j < scenes.Length; j++)
                    if (scenes[j].enabled && scenes[j].path == level.ScenePath) { found = true; break; }
                if (!found) throw new InvalidOperationException(level.DisplayName + " is missing from Build Settings.");
            }
        }

        private static EditorBuildSettingsScene[] CreateBuildSceneList(LevelCatalog catalog)
        {
            var result = new List<EditorBuildSettingsScene>(catalog.Count + 2)
            {
                new EditorBuildSettingsScene(SplashPath, true),
                new EditorBuildSettingsScene(MenuPath, true)
            };
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SplashPath, MenuPath };
            for (int i = 0; i < catalog.Count; i++)
            {
                LevelDefinition level = catalog.Get(i);
                if (level == null || string.IsNullOrWhiteSpace(level.ScenePath) || !paths.Add(level.ScenePath)) continue;
                result.Add(new EditorBuildSettingsScene(level.ScenePath, true));
            }
            return result.ToArray();
        }

        private static void ValidateSplash()
        {
            EditorSceneManager.OpenScene(SplashPath, OpenSceneMode.Single);
            RequireSystems();
            SplashScreenController controller = UnityEngine.Object.FindObjectOfType<SplashScreenController>(true);
            if (controller == null) throw new InvalidOperationException("SplashScreenController is missing.");
            SerializedObject serialized = new SerializedObject(controller);
            Require(serialized, "progressFill");
            Require(serialized, "statusText");
        }

        private static void ValidateMenu()
        {
            EditorSceneManager.OpenScene(MenuPath, OpenSceneMode.Single);
            RequireSystems();
            MainMenuController controller = UnityEngine.Object.FindObjectOfType<MainMenuController>(true);
            if (controller == null) throw new InvalidOperationException("MainMenuController is missing.");
            SerializedObject serialized = new SerializedObject(controller);
            string[] required = { "playButton", "playButtonText", "levelText", "bestScoreText", "totalScoreText", "coinsText", "settingsPanel", "musicToggle", "soundToggle", "vibrationToggle" };
            for (int i = 0; i < required.Length; i++) Require(serialized, required[i]);
            if (UnityEngine.Object.FindObjectOfType<EventSystem>(true) == null) throw new InvalidOperationException("MainMenu EventSystem is missing.");
        }

        private static void ValidateGameplay()
        {
            EditorSceneManager.OpenScene(GameplayPath, OpenSceneMode.Single);
            RequireSystems();
            GameplayHUD hud = UnityEngine.Object.FindObjectOfType<GameplayHUD>(true);
            if (hud == null) throw new InvalidOperationException("GameplayHUD is missing.");
            Require(new SerializedObject(hud), "mainMenuButton");
        }

        private static void RequireSystems()
        {
            GameBootstrapper root = UnityEngine.Object.FindObjectOfType<GameBootstrapper>(true);
            if (root == null || root.GetComponent<GameSaveManager>() == null || root.GetComponent<LevelsManager>() == null || root.GetComponent<GameplayManager>() == null || root.GetComponent<SoundManager>() == null || root.GetComponent<GameFlowController>() == null)
                throw new InvalidOperationException("Game Systems composition root is incomplete.");
            LevelsManager levels = root.GetComponent<LevelsManager>();
            Require(new SerializedObject(levels), "catalog");
        }

        private static void Require(SerializedObject serialized, string field)
        {
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null || property.objectReferenceValue == null) throw new InvalidOperationException(serialized.targetObject.name + "." + field + " is not assigned.");
        }

        private static T Add<T>(GameObject root) where T : Component => root.GetComponent<T>() ?? root.AddComponent<T>();
        private static void Assign(SerializedObject serialized, string field, UnityEngine.Object value) => serialized.FindProperty(field).objectReferenceValue = value;

        private static void CreateCamera()
        {
            GameObject go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            Camera camera = go.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Background;
            camera.orthographic = true;
            camera.tag = "MainCamera";
        }

        private static Canvas CreateCanvas(string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;
            return canvas;
        }

        private static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindObjectOfType<EventSystem>(true) != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        private static GameObject CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color)
        {
            Image image = CreateImage(parent, name, color);
            SetRect(image.rectTransform, anchorMin, anchorMax, offsetMin, offsetMax);
            return image.gameObject;
        }

        private static Image CreateImage(Transform parent, string name, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static Text CreateText(Transform parent, string name, string value, int fontSize, FontStyle style, TextAnchor alignment, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            Text text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = color;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = Mathf.Max(12, fontSize / 2);
            text.resizeTextMaxSize = fontSize;
            return text;
        }

        private static Button CreateButton(Transform parent, string name, string label, Color color)
        {
            Image image = CreateImage(parent, name, color);
            Button button = image.gameObject.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.highlightedColor = Color.Lerp(color, Color.white, .18f);
            colors.pressedColor = Color.Lerp(color, Color.black, .18f);
            colors.disabledColor = new Color(color.r, color.g, color.b, .32f);
            button.colors = colors;
            Text text = CreateText(image.transform, "Label", label, 30, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            SetRect(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(18f, 8f), new Vector2(-18f, -8f));
            return button;
        }

        private static Toggle CreateToggle(Transform parent, string name, string label, float centerY)
        {
            GameObject root = new GameObject(name, typeof(RectTransform), typeof(Toggle));
            root.transform.SetParent(parent, false);
            SetRect(root.GetComponent<RectTransform>(), new Vector2(.13f, centerY - .07f), new Vector2(.87f, centerY + .07f), Vector2.zero, Vector2.zero);
            Text text = CreateText(root.transform, "Label", label, 25, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            SetRect(text.rectTransform, new Vector2(0f, 0f), new Vector2(.7f, 1f), Vector2.zero, Vector2.zero);
            Image background = CreateImage(root.transform, "Background", new Color(.18f, .22f, .31f, 1f));
            SetRect(background.rectTransform, new Vector2(.76f, .2f), new Vector2(1f, .8f), Vector2.zero, Vector2.zero);
            Image checkmark = CreateImage(background.transform, "Checkmark", Cyan);
            SetRect(checkmark.rectTransform, new Vector2(.12f, .18f), new Vector2(.88f, .82f), Vector2.zero, Vector2.zero);
            Toggle toggle = root.GetComponent<Toggle>();
            toggle.targetGraphic = background;
            toggle.graphic = checkmark;
            toggle.isOn = true;
            return toggle;
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }
    }
}
#endif
