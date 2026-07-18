#if UNITY_EDITOR
using System;
using KickTheBuddy.Gameplay;
using KickTheBuddy.VFX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KickTheBuddy.Editor
{
    /// <summary>
    /// Idempotent 1920x1080 HUD builder based on Tehreem's approved gameplay mockups.
    /// The hierarchy is assembled from individual sprites so labels, progress and controls remain live.
    /// </summary>
    public static class GameplayHUDSetupEditor
    {
        private const string ScenePath = "Assets/GameData/Scene/RagdollSandbox.unity";
        private const string Art = "Assets/GameData/UI/Tehreem/";
        private const string GameplayArt = Art + "Gameplay screen assets/";
        private const string CompleteArt = Art + "Levl comp/";
        private const string SettingsArt = Art + "settings/";

        [MenuItem("Tools/Game/UI/Build Tehreem Gameplay UI")]
        public static void BuildFromMenu() => Build(false);

        public static void SetupSandboxBatch() => Build(true);

        [MenuItem("Tools/Game/UI/Remove Gameplay Booster UI")]
        public static void RemoveBoostersFromMenu() => RemoveBoosters(false);
        public static void RemoveBoostersBatch() => RemoveBoosters(true);

        [MenuItem("Tools/Game/UI/Remove Level Timer")]
        public static void RemoveLevelTimerFromMenu()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Transform hudRoot = GameObject.Find("Tehreem Gameplay HUD")?.transform;
            if (hudRoot != null) Remove(hudRoot, "Timer");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("LEVEL_TIMER_REMOVAL_OK: gameplay timer UI removed.");
        }

        private static void RemoveBoosters(bool exitWhenDone)
        {
            try
            {
                var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                GameplayHUD hud = UnityEngine.Object.FindObjectOfType<GameplayHUD>(true);
                if (hud == null) throw new InvalidOperationException("GameplayHUD is missing.");
                GameObject gameplayPanel = new SerializedObject(hud)
                    .FindProperty("gameplayPanel").objectReferenceValue as GameObject;
                Transform root = gameplayPanel != null
                    ? gameplayPanel.transform.Find("Tehreem Gameplay HUD")
                    : null;
                if (root == null) throw new InvalidOperationException("Tehreem Gameplay HUD hierarchy is missing.");

                Remove(root, "Booster Drawer");
                Remove(root, "Booster Closed Handle");
                EditorUtility.SetDirty(hud);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log("GAMEPLAY_BOOSTER_UI_REMOVAL_OK: drawer, handle, references and functionality removed.");
                if (exitWhenDone && Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (exitWhenDone && Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        [MenuItem("Tools/Game/UI/Validate Tehreem Gameplay UI")]
        public static void ValidateSandboxBatch()
        {
            try
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                GameplayHUD hud = UnityEngine.Object.FindObjectOfType<GameplayHUD>(true);
                if (hud == null) throw new InvalidOperationException("GameplayHUD is missing.");
                SerializedObject serialized = new SerializedObject(hud);
                string[] required =
                {
                    "levelText", "scoreText", "objectiveFillImage", "statusFaceImage",
                    "gameplayPanel", "resultPanel", "settingsPanel",
                    "settingsButton", "closeSettingsButton", "settingsPlayButton", "settingsRetryButton", "restartButton", "nextButton",
                    "musicToggle", "soundToggle", "vibrationToggle"
                };
                foreach (string property in required) Require(serialized, property);

                Image fill = (Image)serialized.FindProperty("objectiveFillImage").objectReferenceValue;
                if (fill.type != Image.Type.Filled || fill.fillMethod != Image.FillMethod.Horizontal)
                    throw new InvalidOperationException("Progress fill is not configured as Horizontal Filled.");
                if (serialized.FindProperty("resultStars").arraySize != 3)
                    throw new InvalidOperationException("Exactly three result stars are required.");

                Canvas canvas = hud.GetComponent<Canvas>() ?? hud.GetComponentInParent<Canvas>();
                CanvasScaler scaler = canvas != null ? canvas.GetComponent<CanvasScaler>() : null;
                if (scaler == null || scaler.referenceResolution != new Vector2(1920f, 1080f))
                    throw new InvalidOperationException("Canvas reference resolution must be 1920x1080.");
                if (!Mathf.Approximately(scaler.matchWidthOrHeight, 1f))
                    throw new InvalidOperationException("Landscape HUD must match reference height.");
                if (GameObject.Find("Tehreem Gameplay HUD") == null)
                    throw new InvalidOperationException("Tehreem Gameplay HUD hierarchy is missing.");
                if (UnityEngine.Object.FindObjectOfType<EventSystem>(true) == null)
                    throw new InvalidOperationException("EventSystem is missing.");

                Transform hudRoot = GameObject.Find("Tehreem Gameplay HUD")?.transform;
                if (hudRoot == null)
                    throw new InvalidOperationException("Tehreem Gameplay HUD hierarchy is missing.");
                if (hudRoot.Find("Booster Drawer") != null ||
                    hudRoot.Find("Booster Closed Handle") != null)
                    throw new InvalidOperationException("Removed booster UI is still present in the scene.");
                if (hudRoot.GetComponent<SafeAreaRectTransform>() == null)
                    throw new InvalidOperationException("Gameplay HUD safe-area fitter is missing.");

                RequireRect(hudRoot, "Settings", new Vector2(163f, -102.5f), new Vector2(152f, 153f));
                RequireRect(hudRoot, "Progress", new Vector2(0f, -120f), new Vector2(526f, 86f));
                if (hudRoot.Find("Timer") != null)
                    throw new InvalidOperationException("Removed level timer is still present.");
                RequireRect(hudRoot, "Coins", new Vector2(-200f, -93.5f), new Vector2(274f, 123f));

                Transform settingsCard = ((GameObject)serialized.FindProperty("settingsPanel").objectReferenceValue)
                    .transform.Find("Card");
                RequireRect(settingsCard, "Banner", new Vector2(.5f, 328f), new Vector2(715f, 175f));
                RequireRect(settingsCard, "Close", new Vector2(633.5f, 294.5f), new Vector2(131f, 134f));
                RequireRect(settingsCard, "Play", new Vector2(-234.5f, -202f), new Vector2(417f, 139f));
                RequireRect(settingsCard, "Retry", new Vector2(257f, -202f), new Vector2(424f, 137f));

                Transform resultCard = ((GameObject)serialized.FindProperty("resultPanel").objectReferenceValue)
                    .transform.Find("Card");
                RequireRect(resultCard, "Banner", new Vector2(6f, 302f), new Vector2(957f, 190f));
                RequireRect(resultCard, "Next", new Vector2(-235f, -136.5f), new Vector2(417f, 139f));
                RequireRect(resultCard, "Retry", new Vector2(256.5f, -136.5f), new Vector2(424f, 137f));
                Debug.Log("TEHREEM_GAMEPLAY_UI_VALIDATION_OK: measured 1920x1080 alignment, safe area, settings and result popup are valid; booster UI is absent.");
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
                ConfigureSpriteImports();
                var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                GameplayHUD hud = UnityEngine.Object.FindObjectOfType<GameplayHUD>(true);
                if (hud == null) throw new InvalidOperationException("GameplayHUD was not found in RagdollSandbox.");
                Canvas canvas = hud.GetComponent<Canvas>() ?? hud.GetComponentInParent<Canvas>();
                if (canvas == null) throw new InvalidOperationException("Gameplay Canvas was not found.");

                CanvasScaler scaler = canvas.GetComponent<CanvasScaler>() ?? canvas.gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                // The approved composition is height-led. Matching height keeps the HUD
                // at the authored physical size on modern 18:9 through 21:9 phones.
                scaler.matchWidthOrHeight = 1f;
                canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.None;

                SerializedObject so = new SerializedObject(hud);
                GameObject gameplayPanel = (GameObject)so.FindProperty("gameplayPanel").objectReferenceValue;
                if (gameplayPanel == null) throw new InvalidOperationException("Gameplay Panel is not assigned.");

                Remove(canvas.transform, "Tehreem Settings");
                Remove(canvas.transform, "Tehreem Level Complete");
                Remove(gameplayPanel.transform, "Tehreem Gameplay HUD");
                Remove(gameplayPanel.transform, "Mobile HUD");
                Remove(canvas.transform, "Settings Panel");
                Remove(canvas.transform, "Level Complete Popup");

                SetOldPresentationHidden(so);
                HudReferences main = BuildGameplayHUD(gameplayPanel.transform);
                SettingsReferences settings = BuildSettings(canvas.transform);
                ResultReferences result = BuildResult(canvas.transform);

                so.Update();
                Assign(so, "levelText", main.Level);
                Assign(so, "scoreText", main.Score);
                Assign(so, "resultText", result.Result);
                Assign(so, "objectiveFillImage", main.Progress);
                Assign(so, "statusFaceImage", main.Face);
                Assign(so, "normalStatusSprite", Sprite(GameplayArt + "normal.png"));
                Assign(so, "hitStatusSprite", Sprite(GameplayArt + "hit taken.png"));
                Assign(so, "brokenStatusSprite", Sprite(GameplayArt + "broken.png"));
                Assign(so, "resultPanel", result.Panel);
                Assign(so, "settingsPanel", settings.Panel);
                Assign(so, "settingsButton", main.SettingsButton);
                Assign(so, "closeSettingsButton", settings.Close);
                Assign(so, "settingsPlayButton", settings.Play);
                Assign(so, "settingsRetryButton", settings.Retry);
                Assign(so, "musicToggle", settings.Music);
                Assign(so, "soundToggle", settings.Sound);
                Assign(so, "vibrationToggle", settings.Vibration);
                Assign(so, "restartButton", result.Retry);
                Assign(so, "nextButton", result.Next);
                SerializedProperty stars = so.FindProperty("resultStars");
                stars.arraySize = 3;
                for (int i = 0; i < 3; i++) stars.GetArrayElementAtIndex(i).objectReferenceValue = result.Stars[i];
                so.ApplyModifiedPropertiesWithoutUndo();

                CoinFlyVFXController coinFly = UnityEngine.Object.FindObjectOfType<CoinFlyVFXController>(true);
                if (coinFly != null)
                {
                    SerializedObject coinObject = new SerializedObject(coinFly);
                    SerializedProperty target = coinObject.FindProperty("scoreTarget");
                    if (target != null) target.objectReferenceValue = main.Score.rectTransform;
                    coinObject.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(coinFly);
                }

                if (UnityEngine.Object.FindObjectOfType<EventSystem>(true) == null)
                    new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

                settings.Panel.SetActive(false);
                result.Panel.SetActive(false);
                EditorUtility.SetDirty(hud);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log("TEHREEM_GAMEPLAY_UI_SETUP_OK: exact 1920x1080 responsive HUD and popup art created.");
                if (exitWhenDone) EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (exitWhenDone) EditorApplication.Exit(1);
                else throw;
            }
        }

        private static HudReferences BuildGameplayHUD(Transform parent)
        {
            GameObject root = UI("Tehreem Gameplay HUD", parent);
            Stretch(root.GetComponent<RectTransform>());
            root.AddComponent<SafeAreaRectTransform>();
            root.transform.SetAsLastSibling();

            Button settings = ImageButton("Settings", root.transform, GameplayArt + "settings.png",
                new Vector2(0f, 1f), new Vector2(163f, -102.5f), new Vector2(152f, 153f), new Vector2(.5f, .5f));

            Text level = Label("Level", root.transform, "Level 01", 51, Color.white, FontStyle.Bold);
            SetRect(level.rectTransform, new Vector2(.5f, 1f), new Vector2(0f, -55f), new Vector2(420f, 70f), new Vector2(.5f, .5f));
            AddOutline(level, new Color(.18f, .15f, .32f, 1f), new Vector2(3f, -3f));

            GameObject progressRoot = UI("Progress", root.transform);
            SetRect(progressRoot.GetComponent<RectTransform>(), new Vector2(.5f, 1f), new Vector2(0f, -120f), new Vector2(526f, 86f), new Vector2(.5f, .5f));
            ArtImage("Track", progressRoot.transform, GameplayArt + "progress bar.png", Vector2.zero, new Vector2(484f, 42f));
            Image fill = ArtImage("Fill", progressRoot.transform, GameplayArt + "progress bar filled.png", Vector2.zero, new Vector2(484f, 42f));
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            fill.fillAmount = 0f;
            ArtImage("Stroke", progressRoot.transform, GameplayArt + "progress bar stroke.png", Vector2.zero, new Vector2(526f, 86f));
            Image face = ArtImage("Status Face", progressRoot.transform, GameplayArt + "normal.png", new Vector2(220f, 0f), new Vector2(101f, 101f));

            GameObject coins = UI("Coins", root.transform);
            SetRect(coins.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-200f, -93.5f), new Vector2(274f, 123f), new Vector2(.5f, .5f));
            ArtImage("Coin Bar", coins.transform, GameplayArt + "coin bar.png", Vector2.zero, new Vector2(274f, 123f));
            Text score = Label("Value", coins.transform, "0", 42, Color.white, FontStyle.Bold);
            SetRect(score.rectTransform, new Vector2(.5f, .5f), new Vector2(45f, -1f), new Vector2(142f, 58f), new Vector2(.5f, .5f));
            AddOutline(score, new Color(.25f, .18f, .35f, 1f), new Vector2(2f, -2f));

            return new HudReferences(level, score, fill, face, settings);
        }

        private static SettingsReferences BuildSettings(Transform parent)
        {
            GameObject overlay = Overlay("Tehreem Settings", parent, .58f);
            GameObject card = UI("Card", overlay.transform);
            SetRect(card.GetComponent<RectTransform>(), new Vector2(.5f, .5f), new Vector2(0f, -50.5f), new Vector2(1392f, 679f), new Vector2(.5f, .5f));
            ArtImage("Panel", card.transform, SettingsArt + "panel.png", Vector2.zero, new Vector2(1392f, 679f));
            ArtImage("Banner", card.transform, SettingsArt + "settings banner.png", new Vector2(.5f, 328f), new Vector2(715f, 175f));
            Button close = ImageButton("Close", card.transform, SettingsArt + "cross.png",
                new Vector2(.5f, .5f), new Vector2(633.5f, 294.5f), new Vector2(131f, 134f), new Vector2(.5f, .5f));

            ArtImage("Music Label", card.transform, SettingsArt + "music.png", new Vector2(-383.5f, 188.5f), new Vector2(235f, 72f));
            ArtImage("Sound Label", card.transform, SettingsArt + "sound.png", new Vector2(-376.5f, 63.5f), new Vector2(250f, 72f));
            ArtImage("Vibration Label", card.transform, SettingsArt + "Vibration.png", new Vector2(-338.5f, -57.5f), new Vector2(325f, 72f));
            Toggle music = ArtToggle("Music", card.transform, new Vector2(455f, 188.5f), SettingsArt + "toggle 1.png");
            Toggle sound = ArtToggle("Sound", card.transform, new Vector2(455f, 63.5f), SettingsArt + "toggle 2.png");
            Toggle vibration = ArtToggle("Vibration", card.transform, new Vector2(455f, -57.5f), SettingsArt + "toggle.3.png");
            Button play = ImageButton("Play", card.transform, SettingsArt + "play.png",
                new Vector2(.5f, .5f), new Vector2(-234.5f, -202f), new Vector2(417f, 139f), new Vector2(.5f, .5f));
            Button retry = ImageButton("Retry", card.transform, SettingsArt + "retry.png",
                new Vector2(.5f, .5f), new Vector2(257f, -202f), new Vector2(424f, 137f), new Vector2(.5f, .5f));
            return new SettingsReferences(overlay, close, play, retry, music, sound, vibration);
        }

        private static ResultReferences BuildResult(Transform parent)
        {
            GameObject overlay = Overlay("Tehreem Level Complete", parent, .60f);
            GameObject card = UI("Card", overlay.transform);
            SetRect(card.GetComponent<RectTransform>(), new Vector2(.5f, .5f), new Vector2(.5f, -70f), new Vector2(1173f, 572f), new Vector2(.5f, .5f));
            ArtImage("Panel", card.transform, CompleteArt + "panel.png", Vector2.zero, new Vector2(1173f, 572f));
            ArtImage("Banner", card.transform, CompleteArt + "magnific_ubvClg0QLD.png", new Vector2(6f, 302f), new Vector2(957f, 190f));

            // The exported star files are numbered by art layer rather than screen order.
            string[] empty = { "star empty 3.png", "star empty 1.png", "star empty 2.png" };
            string[] full = { "star fil 3.png", "star fil1.png", "star fil 2.png" };
            Vector2[] positions =
            {
                new Vector2(-244f, 63.5f),
                new Vector2(-1.5f, 96f),
                new Vector2(231.5f, 68f)
            };
            Vector2[] sizes =
            {
                new Vector2(179f, 189f),
                new Vector2(208f, 206f),
                new Vector2(180f, 182f)
            };
            Image[] stars = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                ArtImage("Empty Star " + (i + 1), card.transform, CompleteArt + empty[i], positions[i], sizes[i]);
                stars[i] = ArtImage("Star " + (i + 1), card.transform, CompleteArt + full[i], positions[i], sizes[i]);
            }

            Button next = ImageButton("Next", card.transform, CompleteArt + "next.png",
                new Vector2(.5f, .5f), new Vector2(-235f, -136.5f), new Vector2(417f, 139f), new Vector2(.5f, .5f));
            Button retry = ImageButton("Retry", card.transform, CompleteArt + "retry.png",
                new Vector2(.5f, .5f), new Vector2(256.5f, -136.5f), new Vector2(424f, 137f), new Vector2(.5f, .5f));
            Text result = Label("Result Accessibility Text", card.transform, string.Empty, 1, Color.clear, FontStyle.Normal);
            result.gameObject.SetActive(false);
            return new ResultReferences(overlay, next, retry, result, stars);
        }

        private static Toggle ArtToggle(string name, Transform parent, Vector2 position, string onPath)
        {
            GameObject go = UI(name + " Toggle", parent);
            SetRect(go.GetComponent<RectTransform>(), new Vector2(.5f, .5f), position, new Vector2(150f, 75f), new Vector2(.5f, .5f));
            Image off = ArtImage("Off", go.transform, SettingsArt + "toggle unckehc.png", Vector2.zero, new Vector2(133f, 67f));
            Image on = ArtImage("On", go.transform, onPath, Vector2.zero, new Vector2(133f, 67f));
            Toggle toggle = go.AddComponent<Toggle>();
            toggle.targetGraphic = off;
            toggle.graphic = on;
            toggle.isOn = true;
            go.AddComponent<UIButtonPressFeedback>();
            return toggle;
        }

        private static GameObject Overlay(string name, Transform parent, float alpha)
        {
            GameObject overlay = UI(name, parent, typeof(Image));
            Stretch(overlay.GetComponent<RectTransform>());
            Image dim = overlay.GetComponent<Image>();
            dim.color = new Color(.18f, .02f, .10f, alpha);
            dim.raycastTarget = true;
            overlay.transform.SetAsLastSibling();
            return overlay;
        }

        private static Button ImageButton(string name, Transform parent, string path, Vector2 anchor, Vector2 position, Vector2 size, Vector2 pivot)
        {
            GameObject go = UI(name, parent, typeof(Image), typeof(Button));
            SetRect(go.GetComponent<RectTransform>(), anchor, position, size, pivot);
            Image image = go.GetComponent<Image>();
            image.sprite = Sprite(path);
            image.preserveAspect = true;
            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, .92f);
            colors.pressedColor = new Color(.83f, .83f, .83f, 1f);
            colors.fadeDuration = .07f;
            button.colors = colors;
            go.AddComponent<UIButtonPressFeedback>();
            return button;
        }

        private static Image ArtImage(string name, Transform parent, string path, Vector2 position, Vector2 size, Vector2? pivot = null)
        {
            GameObject go = UI(name, parent, typeof(Image));
            SetRect(go.GetComponent<RectTransform>(), new Vector2(.5f, .5f), position, size, pivot ?? new Vector2(.5f, .5f));
            Image image = go.GetComponent<Image>();
            image.sprite = Sprite(path);
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private static Text Label(string name, Transform parent, string value, int size, Color color, FontStyle style)
        {
            Text text = UI(name, parent, typeof(Text)).GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        private static void SetOldPresentationHidden(SerializedObject so)
        {
            string[] labels = { "levelText", "objectiveText", "scoreText", "resultText", "objectiveProgressText" };
            foreach (string property in labels)
            {
                Text text = so.FindProperty(property)?.objectReferenceValue as Text;
                if (text != null) text.gameObject.SetActive(false);
            }
            Slider slider = so.FindProperty("objectiveSlider")?.objectReferenceValue as Slider;
            if (slider != null) slider.gameObject.SetActive(false);
        }

        private static void ConfigureSpriteImports()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { Art.TrimEnd('/') });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                bool dirty = importer.textureType != TextureImporterType.Sprite || importer.mipmapEnabled ||
                             importer.alphaIsTransparency == false || importer.spriteImportMode != SpriteImportMode.Single;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.maxTextureSize = 2048;
                if (dirty) importer.SaveAndReimport();
            }
        }

        private static Sprite Sprite(string path)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new InvalidOperationException("Missing UI sprite: " + path);
            return sprite;
        }

        private static void AddOutline(Graphic graphic, Color color, Vector2 distance)
        {
            Outline outline = graphic.gameObject.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = distance;
            outline.useGraphicAlpha = true;
        }

        private static GameObject UI(string name, Transform parent, params Type[] extra)
        {
            Type[] components = new Type[extra.Length + 1];
            components[0] = typeof(RectTransform);
            Array.Copy(extra, 0, components, 1, extra.Length);
            GameObject go = new GameObject(name, components);
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void Remove(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null) UnityEngine.Object.DestroyImmediate(child.gameObject);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, .5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetRect(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size, Vector2 pivot)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            rect.localScale = Vector3.one;
        }

        private static void Assign(SerializedObject target, string propertyName, UnityEngine.Object value)
        {
            SerializedProperty property = target.FindProperty(propertyName);
            if (property == null) throw new InvalidOperationException("GameplayHUD property is missing: " + propertyName);
            property.objectReferenceValue = value;
        }

        private static void Require(SerializedObject target, string propertyName)
        {
            SerializedProperty property = target.FindProperty(propertyName);
            if (property == null || property.objectReferenceValue == null)
                throw new InvalidOperationException(propertyName + " is not assigned.");
        }

        private static void RequireRect(Transform parent, string childName, Vector2 expectedPosition, Vector2 expectedSize)
        {
            RectTransform rect = parent != null ? parent.Find(childName) as RectTransform : null;
            if (rect == null)
                throw new InvalidOperationException("Missing UI RectTransform: " + childName);
            if ((rect.anchoredPosition - expectedPosition).sqrMagnitude > .01f ||
                (rect.sizeDelta - expectedSize).sqrMagnitude > .01f)
                throw new InvalidOperationException(
                    $"{childName} alignment drifted. Position {rect.anchoredPosition}, size {rect.sizeDelta}.");
        }

        private readonly struct HudReferences
        {
            public readonly Text Level, Score;
            public readonly Image Progress, Face;
            public readonly Button SettingsButton;
            public HudReferences(Text level, Text score, Image progress, Image face, Button settingsButton)
            {
                Level = level; Score = score; Progress = progress; Face = face;
                SettingsButton = settingsButton;
            }
        }

        private readonly struct SettingsReferences
        {
            public readonly GameObject Panel;
            public readonly Button Close, Play, Retry;
            public readonly Toggle Music, Sound, Vibration;
            public SettingsReferences(GameObject panel, Button close, Button play, Button retry, Toggle music, Toggle sound, Toggle vibration)
            {
                Panel = panel; Close = close; Play = play; Retry = retry; Music = music; Sound = sound; Vibration = vibration;
            }
        }

        private readonly struct ResultReferences
        {
            public readonly GameObject Panel;
            public readonly Button Next, Retry;
            public readonly Text Result;
            public readonly Image[] Stars;
            public ResultReferences(GameObject panel, Button next, Button retry, Text result, Image[] stars)
            {
                Panel = panel; Next = next; Retry = retry; Result = result; Stars = stars;
            }
        }
    }
}
#endif
