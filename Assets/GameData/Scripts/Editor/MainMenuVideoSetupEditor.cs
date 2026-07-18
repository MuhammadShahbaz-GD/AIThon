#if UNITY_EDITOR
using System;
using KickTheBuddy.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

namespace KickTheBuddy.Editor
{
    /// <summary>Authors and validates the video-first Main Menu and saved-game Play command.</summary>
    public static class MainMenuVideoSetupEditor
    {
        private const string ScenePath = "Assets/GameData/Scene/MainMenu.unity";
        private const string SplashScenePath = "Assets/GameData/Scene/Splash.unity";
        private const string ArtRoot = "Assets/GameData/UI/MainMenu/";
        private const string VideoPath = ArtRoot + "Ragdoll Video.mp4";
        private const string PosterPath = ArtRoot + "Sequence 06.00_00_19_23.Still003.png";
        private const string PlayPath = ArtRoot + "play button homescreen.png";

        [MenuItem("Tools/Gameplay/UI/Apply Video Main Menu")]
        public static void SetupBatch()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureSprite(PosterPath, false);
            ConfigureSprite(PlayPath, true);
            BuildScene();
            AssetDatabase.SaveAssets();
            Validate();
            Debug.Log("VIDEO_MAIN_MENU_OK: intro video, static poster, delayed Play button, and saved-level Continue command are authored.");
        }

        [MenuItem("Tools/Gameplay/UI/Make Splash And Menu Videos Responsive")]
        public static void MakeVideoScreensResponsive()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before changing video presentation.");

            Scene originalScene = SceneManager.GetActiveScene();
            string originalPath = originalScene.path;
            ConfigureResponsiveScene(SplashScenePath, false);
            ConfigureResponsiveScene(ScenePath, true);
            AssetDatabase.SaveAssets();
            ValidateResponsivePresentation();
            if (!string.IsNullOrEmpty(originalPath))
                EditorSceneManager.OpenScene(originalPath, OpenSceneMode.Single);
            Debug.Log("RESPONSIVE_VIDEO_SCREENS_OK: splash video, menu video, and menu poster now preserve 16:9 framing and cover every display without stretching.");
        }

        private static void ConfigureSprite(string path, bool alpha)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Missing Main Menu artwork: " + path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = alpha;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 2048;
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = 2048;
            android.format = alpha ? TextureImporterFormat.ASTC_6x6 : TextureImporterFormat.ASTC_8x8;
            android.compressionQuality = 70;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
        }

        private static void BuildScene()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            MainMenuController menu = UnityEngine.Object.FindObjectOfType<MainMenuController>(true);
            if (menu == null) throw new InvalidOperationException("MainMenuController is missing.");
            Canvas canvas = menu.GetComponent<Canvas>();
            if (canvas == null) throw new InvalidOperationException("Main Menu controller has no Canvas.");

            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = .5f;
                EditorUtility.SetDirty(scaler);
            }

            ClearChildren(canvas.transform);
            GameObject staticObject = new GameObject("Static Home Screen", typeof(RectTransform), typeof(CanvasGroup));
            Undo.RegisterCreatedObjectUndo(staticObject, "Create Static Home Screen");
            staticObject.layer = LayerMask.NameToLayer("UI");
            staticObject.transform.SetParent(canvas.transform, false);
            RectTransform staticRect = staticObject.GetComponent<RectTransform>();
            Stretch(staticRect);
            CanvasGroup staticGroup = staticObject.GetComponent<CanvasGroup>();

            Image poster = CreateImage(staticObject.transform, "Home Poster", RequireSprite(PosterPath));
            Stretch(poster.rectTransform);
            ConfigureCoverImage(poster);
            poster.raycastTarget = false;

            Button playButton = CreateButton(staticObject.transform, RequireSprite(PlayPath));
            SetRect(playButton.GetComponent<RectTransform>(), new Vector2(.27f, .43f),
                new Vector2(230f, 230f), Vector2.zero);

            Text playLabel = CreateText(staticObject.transform, "Play Label", "PLAY", 38);
            SetRect(playLabel.rectTransform, new Vector2(.27f, .43f),
                new Vector2(260f, 58f), new Vector2(0f, -155f));
            playLabel.color = Color.white;
            playLabel.fontStyle = FontStyle.Bold;
            playLabel.alignment = TextAnchor.MiddleCenter;
            Outline outline = playLabel.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(.18f, .3f, .02f, .95f);
            outline.effectDistance = new Vector2(3f, -3f);

            VideoClip clip = AssetDatabase.LoadAssetAtPath<VideoClip>(VideoPath);
            if (clip == null) throw new InvalidOperationException("Main Menu VideoClip did not import: " + VideoPath);
            Camera camera = UnityEngine.Object.FindObjectOfType<Camera>(true);
            if (camera == null) throw new InvalidOperationException("Main Menu camera is missing.");
            camera.backgroundColor = Color.black;
            VideoPlayer player = canvas.GetComponent<VideoPlayer>();
            if (player == null) player = Undo.AddComponent<VideoPlayer>(canvas.gameObject);
            player.source = VideoSource.VideoClip;
            player.clip = clip;
            player.playOnAwake = false;
            player.waitForFirstFrame = true;
            player.skipOnDrop = true;
            player.isLooping = false;
            player.renderMode = VideoRenderMode.CameraNearPlane;
            player.targetCamera = camera;
            player.targetCameraAlpha = 1f;
            player.aspectRatio = VideoAspectRatio.FitOutside;
            player.audioOutputMode = VideoAudioOutputMode.Direct;
            player.controlledAudioTrackCount = 1;
            player.EnableAudioTrack(0, true);

            MainMenuVideoSequenceController sequence = canvas.GetComponent<MainMenuVideoSequenceController>();
            if (sequence == null) sequence = Undo.AddComponent<MainMenuVideoSequenceController>(canvas.gameObject);
            SerializedObject sequenceData = new SerializedObject(sequence);
            sequenceData.FindProperty("videoPlayer").objectReferenceValue = player;
            sequenceData.FindProperty("staticScreen").objectReferenceValue = staticGroup;
            sequenceData.FindProperty("playButton").objectReferenceValue = playButton;
            sequenceData.FindProperty("preparationTimeout").floatValue = 6f;
            sequenceData.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject menuData = new SerializedObject(menu);
            string[] clearFields = { "playButtonText", "levelText", "bestScoreText", "totalScoreText", "coinsText",
                "previousLevelButton", "nextLevelButton", "settingsButton", "closeSettingsButton", "quitButton",
                "settingsPanel", "musicToggle", "soundToggle", "vibrationToggle" };
            for (int i = 0; i < clearFields.Length; i++)
                menuData.FindProperty(clearFields[i]).objectReferenceValue = null;
            menuData.FindProperty("playButton").objectReferenceValue = playButton;
            menuData.ApplyModifiedPropertiesWithoutUndo();

            SceneUIEntranceAnimator entrance = canvas.GetComponent<SceneUIEntranceAnimator>();
            if (entrance != null)
            {
                SerializedObject entranceData = new SerializedObject(entrance);
                entranceData.FindProperty("targets").arraySize = 0;
                entranceData.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorUtility.SetDirty(menu);
            EditorUtility.SetDirty(sequence);
            EditorUtility.SetDirty(player);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Could not save the video Main Menu.");
        }

        private static Button CreateButton(Transform parent, Sprite sprite)
        {
            GameObject go = new GameObject("Play Saved Level", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            Undo.RegisterCreatedObjectUndo(go, "Create Play Saved Level");
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1f, 1f, .82f, 1f);
            colors.pressedColor = new Color(.82f, .92f, .65f, 1f);
            colors.fadeDuration = .08f;
            button.colors = colors;
            return button;
        }

        private static void ConfigureResponsiveScene(string scenePath, bool configurePoster)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            VideoPlayer[] players = UnityEngine.Object.FindObjectsOfType<VideoPlayer>(true);
            if (players.Length == 0)
                throw new InvalidOperationException("No VideoPlayer found in " + scenePath);
            for (int i = 0; i < players.Length; i++)
            {
                Undo.RecordObject(players[i], "Configure Responsive Video");
                players[i].aspectRatio = VideoAspectRatio.FitOutside;
                EditorUtility.SetDirty(players[i]);
            }

            Canvas canvas = UnityEngine.Object.FindObjectOfType<Canvas>(true);
            CanvasScaler scaler = canvas != null ? canvas.GetComponent<CanvasScaler>() : null;
            if (scaler != null)
            {
                Undo.RecordObject(scaler, "Configure Responsive Canvas");
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = .5f;
                EditorUtility.SetDirty(scaler);
            }

            if (configurePoster)
            {
                Image poster = GameObject.Find("Home Poster")?.GetComponent<Image>();
                if (poster == null) throw new InvalidOperationException("Main Menu Home Poster is missing.");
                ConfigureCoverImage(poster);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Could not save responsive scene: " + scenePath);
        }

        private static void ConfigureCoverImage(Image image)
        {
            Undo.RecordObject(image, "Configure Cover Image");
            image.preserveAspect = true;
            RectTransform rect = image.rectTransform;
            Undo.RecordObject(rect, "Center Cover Image");
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            AspectRatioFitter fitter = image.GetComponent<AspectRatioFitter>();
            if (fitter == null) fitter = Undo.AddComponent<AspectRatioFitter>(image.gameObject);
            Undo.RecordObject(fitter, "Configure Cover Aspect");
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            Sprite sprite = image.sprite;
            fitter.aspectRatio = sprite != null && sprite.rect.height > 0f
                ? sprite.rect.width / sprite.rect.height
                : 16f / 9f;
            EditorUtility.SetDirty(image);
            EditorUtility.SetDirty(fitter);
        }

        private static void ValidateResponsivePresentation()
        {
            EditorSceneManager.OpenScene(SplashScenePath, OpenSceneMode.Single);
            VideoPlayer splashPlayer = UnityEngine.Object.FindObjectOfType<VideoPlayer>(true);
            if (splashPlayer == null || splashPlayer.aspectRatio != VideoAspectRatio.FitOutside)
                throw new InvalidOperationException("Splash video is not configured to cover the display.");

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            VideoPlayer menuPlayer = UnityEngine.Object.FindObjectOfType<VideoPlayer>(true);
            Image poster = GameObject.Find("Home Poster")?.GetComponent<Image>();
            AspectRatioFitter fitter = poster != null ? poster.GetComponent<AspectRatioFitter>() : null;
            if (menuPlayer == null || menuPlayer.aspectRatio != VideoAspectRatio.FitOutside ||
                poster == null || !poster.preserveAspect || fitter == null ||
                fitter.aspectMode != AspectRatioFitter.AspectMode.EnvelopeParent)
                throw new InvalidOperationException("Main Menu video/poster responsive presentation is incomplete.");
        }

        private static Image CreateImage(Transform parent, string name, Sprite sprite)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            return image;
        }

        private static Text CreateText(Transform parent, string name, string value, int size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            Text text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            text.fontSize = size;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 20;
            text.resizeTextMaxSize = size;
            text.raycastTarget = false;
            return text;
        }

        private static Sprite RequireSprite(string path)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new InvalidOperationException("Main Menu sprite did not import: " + path);
            return sprite;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetRect(RectTransform rect, Vector2 anchor, Vector2 size, Vector2 position)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        private static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                Undo.DestroyObjectImmediate(parent.GetChild(i).gameObject);
        }

        private static void Validate()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            MainMenuController menu = UnityEngine.Object.FindObjectOfType<MainMenuController>(true);
            MainMenuVideoSequenceController sequence = UnityEngine.Object.FindObjectOfType<MainMenuVideoSequenceController>(true);
            VideoPlayer player = UnityEngine.Object.FindObjectOfType<VideoPlayer>(true);
            Button button = GameObject.Find("Play Saved Level")?.GetComponent<Button>();
            if (menu == null || sequence == null || player == null || player.clip == null || button == null)
                throw new InvalidOperationException("Video Main Menu wiring is incomplete.");
        }
    }
}
#endif
