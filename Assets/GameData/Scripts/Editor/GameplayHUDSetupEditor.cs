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
    /// <summary>Idempotent scene builder for the mobile gameplay HUD and settings overlay.</summary>
    public static class GameplayHUDSetupEditor
    {
        private const string ScenePath = "Assets/GameData/Scene/RagdollSandbox.unity";
        private static readonly Color Navy = new Color(.055f, .075f, .12f, .94f);
        private static readonly Color Blue = new Color(.12f, .50f, .96f, 1f);
        private static readonly Color Cyan = new Color(.10f, .88f, .94f, 1f);
        private static readonly Color Green = new Color(.22f, .86f, .47f, 1f);
        private static readonly Color Muted = new Color(.32f, .37f, .46f, 1f);

        [MenuItem("Tools/Game/UI/Build Gameplay HUD & Settings")]
        public static void BuildFromMenu() => Build(false);

        public static void SetupSandboxBatch() => Build(true);

        public static void ValidateSandboxBatch()
        {
            try
            {
                var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                GameplayHUD hud = UnityEngine.Object.FindObjectOfType<GameplayHUD>(true);
                if (hud == null) throw new InvalidOperationException("GameplayHUD is missing.");
                SerializedObject serialized = new SerializedObject(hud);
                Require(serialized, "scoreText");
                Require(serialized, "objectiveFillImage");
                Require(serialized, "objectiveProgressText");
                Require(serialized, "settingsButton");
                Require(serialized, "settingsPanel");
                Require(serialized, "closeSettingsButton");
                Require(serialized, "musicToggle");
                Require(serialized, "soundToggle");
                Require(serialized, "vibrationToggle");
                Image fill = serialized.FindProperty("objectiveFillImage").objectReferenceValue as Image;
                if (fill == null || fill.type != Image.Type.Filled || fill.fillMethod != Image.FillMethod.Horizontal)
                    throw new InvalidOperationException("Level Progress Fill must be a horizontally filled Image.");
                if (UnityEngine.Object.FindObjectOfType<EventSystem>(true) == null)
                    throw new InvalidOperationException("EventSystem is missing.");
                Debug.Log("GAMEPLAY_HUD_VALIDATION_OK: score bar, filled progress bar, settings overlay, and input wiring are valid.");
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
                var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                GameplayHUD hud = UnityEngine.Object.FindObjectOfType<GameplayHUD>(true);
                if (hud == null) throw new InvalidOperationException("GameplayHUD was not found in RagdollSandbox.");
                Canvas canvas = hud.GetComponent<Canvas>() ?? hud.GetComponentInParent<Canvas>();
                if (canvas == null) throw new InvalidOperationException("Gameplay Canvas was not found.");

                CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
                if (scaler != null)
                {
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = new Vector2(1080f, 1920f);
                    scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                    scaler.matchWidthOrHeight = .5f;
                }
                canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.None;

                SerializedObject hudObject = new SerializedObject(hud);
                GameObject gameplayPanel = hudObject.FindProperty("gameplayPanel").objectReferenceValue as GameObject;
                if (gameplayPanel == null) throw new InvalidOperationException("Gameplay Panel is not assigned.");

                RemoveChild(gameplayPanel.transform, "Mobile HUD");
                RemoveChild(canvas.transform, "Settings Panel");

                Text oldScore = hudObject.FindProperty("scoreText").objectReferenceValue as Text;
                if (oldScore != null) oldScore.gameObject.SetActive(false);

                RepositionExistingLabel(hudObject.FindProperty("levelText").objectReferenceValue as Text, new Vector2(0f, -155f), new Vector2(520f, 52f), TextAnchor.MiddleCenter);
                RepositionExistingLabel(hudObject.FindProperty("objectiveText").objectReferenceValue as Text, new Vector2(0f, -205f), new Vector2(720f, 46f), TextAnchor.MiddleCenter);
                RepositionExistingLabel(hudObject.FindProperty("timerText").objectReferenceValue as Text, new Vector2(-220f, -78f), new Vector2(210f, 58f), TextAnchor.MiddleCenter, new Vector2(1f, 1f));

                GameObject mobileHud = UI("Mobile HUD", gameplayPanel.transform, typeof(RectTransform));
                Stretch(mobileHud.GetComponent<RectTransform>());
                mobileHud.transform.SetAsLastSibling();

                Text score = BuildScoreBar(mobileHud.transform);
                (Image fill, Text progress) = BuildProgressBar(mobileHud.transform);
                Button settings = BuildSettingsButton(mobileHud.transform);
                SettingsReferences settingsReferences = BuildSettingsPanel(canvas.transform);

                hudObject.Update();
                hudObject.FindProperty("scoreText").objectReferenceValue = score;
                hudObject.FindProperty("objectiveFillImage").objectReferenceValue = fill;
                hudObject.FindProperty("objectiveProgressText").objectReferenceValue = progress;
                hudObject.FindProperty("settingsButton").objectReferenceValue = settings;
                hudObject.FindProperty("settingsPanel").objectReferenceValue = settingsReferences.Panel;
                hudObject.FindProperty("closeSettingsButton").objectReferenceValue = settingsReferences.CloseButton;
                hudObject.FindProperty("musicToggle").objectReferenceValue = settingsReferences.Music;
                hudObject.FindProperty("soundToggle").objectReferenceValue = settingsReferences.Sound;
                hudObject.FindProperty("vibrationToggle").objectReferenceValue = settingsReferences.Vibration;
                hudObject.ApplyModifiedPropertiesWithoutUndo();

                CoinFlyVFXController coinFly = UnityEngine.Object.FindObjectOfType<CoinFlyVFXController>(true);
                if (coinFly != null)
                {
                    SerializedObject coinObject = new SerializedObject(coinFly);
                    coinObject.FindProperty("scoreTarget").objectReferenceValue = score.rectTransform;
                    coinObject.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(coinFly);
                }

                if (UnityEngine.Object.FindObjectOfType<EventSystem>(true) == null)
                    new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

                settingsReferences.Panel.SetActive(false);
                EditorUtility.SetDirty(hud);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log("GAMEPLAY_HUD_SETUP_OK: mobile score, level progress, and settings UI created.");
                if (exitWhenDone) EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (exitWhenDone) EditorApplication.Exit(1);
                else throw;
            }
        }

        private static Text BuildScoreBar(Transform parent)
        {
            GameObject card = UI("Score Bar", parent, typeof(Image));
            SetRect(card.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(26f, -28f), new Vector2(300f, 104f), new Vector2(0f, 1f));
            StylePanel(card.GetComponent<Image>(), Navy);
            Image coin = UI("Coin Icon", card.transform, typeof(Image)).GetComponent<Image>();
            SetRect(coin.rectTransform, new Vector2(0f, .5f), new Vector2(54f, 0f), new Vector2(62f, 62f), new Vector2(.5f, .5f));
            coin.sprite = BuiltinSprite(); coin.color = new Color(1f, .72f, .12f, 1f); coin.raycastTarget = false;
            Text symbol = Label("Coin Symbol", coin.transform, "$", 32, Color.white, FontStyle.Bold);
            Stretch(symbol.rectTransform);
            Text caption = Label("Caption", card.transform, "SCORE", 20, new Color(.70f, .76f, .86f, 1f), FontStyle.Bold);
            SetRect(caption.rectTransform, new Vector2(0f, .5f), new Vector2(112f, 24f), new Vector2(150f, 32f), new Vector2(.5f, .5f));
            Text value = Label("Value", card.transform, "0", 38, Color.white, FontStyle.Bold);
            SetRect(value.rectTransform, new Vector2(0f, .5f), new Vector2(112f, -19f), new Vector2(150f, 52f), new Vector2(.5f, .5f));
            return value;
        }

        private static (Image, Text) BuildProgressBar(Transform parent)
        {
            GameObject card = UI("Level Progress Bar", parent, typeof(Image));
            SetRect(card.GetComponent<RectTransform>(), new Vector2(.5f, 1f), new Vector2(0f, -28f), new Vector2(420f, 104f), new Vector2(.5f, 1f));
            StylePanel(card.GetComponent<Image>(), Navy);
            Text caption = Label("Caption", card.transform, "LEVEL PROGRESS", 18, new Color(.70f, .76f, .86f, 1f), FontStyle.Bold);
            SetRect(caption.rectTransform, new Vector2(.5f, 1f), new Vector2(0f, -20f), new Vector2(370f, 30f), new Vector2(.5f, 1f));

            Image track = UI("Track", card.transform, typeof(Image)).GetComponent<Image>();
            SetRect(track.rectTransform, new Vector2(.5f, 0f), new Vector2(0f, 24f), new Vector2(370f, 38f), new Vector2(.5f, .5f));
            StylePanel(track, new Color(.12f, .15f, .22f, 1f));
            Image fill = UI("Fill", track.transform, typeof(Image)).GetComponent<Image>();
            StretchWithPadding(fill.rectTransform, 5f);
            fill.sprite = BuiltinSprite(); fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal; fill.fillOrigin = 0; fill.fillAmount = 0f; fill.color = Cyan; fill.raycastTarget = false;
            Text progress = Label("Progress", track.transform, "0 / 0", 19, Color.white, FontStyle.Bold);
            Stretch(progress.rectTransform);
            return (fill, progress);
        }

        private static Button BuildSettingsButton(Transform parent)
        {
            Button button = MakeButton("Settings Button", parent, "SET", Blue, 26);
            SetRect(button.GetComponent<RectTransform>(), Vector2.one, new Vector2(-26f, -28f), new Vector2(96f, 96f), Vector2.one);
            return button;
        }

        private static SettingsReferences BuildSettingsPanel(Transform parent)
        {
            GameObject panel = UI("Settings Panel", parent, typeof(Image));
            Stretch(panel.GetComponent<RectTransform>());
            panel.GetComponent<Image>().color = new Color(.01f, .015f, .03f, .82f);
            panel.GetComponent<Image>().raycastTarget = true;
            panel.transform.SetAsLastSibling();

            GameObject card = UI("Card", panel.transform, typeof(Image));
            SetRect(card.GetComponent<RectTransform>(), new Vector2(.5f, .5f), Vector2.zero, new Vector2(760f, 790f), new Vector2(.5f, .5f));
            StylePanel(card.GetComponent<Image>(), new Color(.065f, .085f, .14f, .99f));
            Text title = Label("Title", card.transform, "SETTINGS", 50, Color.white, FontStyle.Bold);
            SetRect(title.rectTransform, new Vector2(.5f, 1f), new Vector2(0f, -72f), new Vector2(620f, 80f), new Vector2(.5f, .5f));
            Text subtitle = Label("Subtitle", card.transform, "Choose how the game feels", 24, new Color(.67f, .73f, .84f, 1f), FontStyle.Normal);
            SetRect(subtitle.rectTransform, new Vector2(.5f, 1f), new Vector2(0f, -128f), new Vector2(620f, 48f), new Vector2(.5f, .5f));

            Toggle music = MakeToggle("Music Toggle", card.transform, "MUSIC", new Vector2(0f, 135f));
            Toggle sound = MakeToggle("Sound Toggle", card.transform, "SOUND EFFECTS", new Vector2(0f, 20f));
            Toggle vibration = MakeToggle("Vibration Toggle", card.transform, "VIBRATION", new Vector2(0f, -95f));
            Button close = MakeButton("Close Button", card.transform, "CONTINUE", Blue, 31);
            SetRect(close.GetComponent<RectTransform>(), new Vector2(.5f, 0f), new Vector2(0f, 70f), new Vector2(500f, 100f), new Vector2(.5f, .5f));
            return new SettingsReferences(panel, close, music, sound, vibration);
        }

        private static Toggle MakeToggle(string name, Transform parent, string caption, Vector2 position)
        {
            GameObject row = UI(name, parent, typeof(Image), typeof(Toggle));
            SetRect(row.GetComponent<RectTransform>(), new Vector2(.5f, .5f), position, new Vector2(600f, 92f), new Vector2(.5f, .5f));
            Image rowImage = row.GetComponent<Image>(); StylePanel(rowImage, new Color(.10f, .125f, .19f, 1f));
            Text label = Label("Label", row.transform, caption, 28, Color.white, FontStyle.Bold);
            SetRect(label.rectTransform, new Vector2(0f, .5f), new Vector2(30f, 0f), new Vector2(390f, 70f), new Vector2(0f, .5f));
            label.alignment = TextAnchor.MiddleLeft;

            Image switchBackground = UI("Switch", row.transform, typeof(Image)).GetComponent<Image>();
            SetRect(switchBackground.rectTransform, new Vector2(1f, .5f), new Vector2(-30f, 0f), new Vector2(116f, 58f), new Vector2(1f, .5f));
            StylePanel(switchBackground, Muted);
            Image check = UI("On", switchBackground.transform, typeof(Image)).GetComponent<Image>();
            SetRect(check.rectTransform, new Vector2(1f, .5f), new Vector2(-8f, 0f), new Vector2(42f, 42f), new Vector2(1f, .5f));
            check.sprite = BuiltinSprite(); check.color = Green; check.raycastTarget = false;

            Toggle toggle = row.GetComponent<Toggle>();
            toggle.targetGraphic = rowImage;
            toggle.graphic = check;
            toggle.isOn = true;
            toggle.transition = Selectable.Transition.ColorTint;
            return toggle;
        }

        private static Button MakeButton(string name, Transform parent, string caption, Color color, int fontSize)
        {
            GameObject go = UI(name, parent, typeof(Image), typeof(Button));
            Image image = go.GetComponent<Image>(); StylePanel(image, color);
            Button button = go.GetComponent<Button>(); button.targetGraphic = image;
            ColorBlock colors = button.colors; colors.highlightedColor = Color.Lerp(color, Color.white, .14f); colors.pressedColor = Color.Lerp(color, Color.black, .18f); button.colors = colors;
            Text label = Label("Label", go.transform, caption, fontSize, Color.white, FontStyle.Bold); Stretch(label.rectTransform);
            return button;
        }

        private static Text Label(string name, Transform parent, string value, int size, Color color, FontStyle style)
        {
            Text text = UI(name, parent, typeof(Text)).GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value; text.fontSize = size; text.fontStyle = style; text.alignment = TextAnchor.MiddleCenter; text.color = color; text.raycastTarget = false;
            return text;
        }

        private static void RepositionExistingLabel(Text text, Vector2 position, Vector2 size, TextAnchor alignment, Vector2? anchor = null)
        {
            if (text == null) return;
            Vector2 point = anchor ?? new Vector2(.5f, 1f);
            SetRect(text.rectTransform, point, position, size, point);
            text.alignment = alignment;
        }

        private static void StylePanel(Image image, Color color)
        {
            image.sprite = BuiltinSprite(); image.type = Image.Type.Sliced; image.color = color;
        }

        private static Sprite BuiltinSprite() => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        private static GameObject UI(string name, Transform parent, params Type[] components) { GameObject go = new GameObject(name, components); go.transform.SetParent(parent, false); return go; }
        private static void RemoveChild(Transform parent, string name) { Transform child = parent.Find(name); if (child != null) UnityEngine.Object.DestroyImmediate(child.gameObject); }
        private static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f, .5f); rect.offsetMin = rect.offsetMax = Vector2.zero; }
        private static void StretchWithPadding(RectTransform rect, float padding) { Stretch(rect); rect.offsetMin = new Vector2(padding, padding); rect.offsetMax = new Vector2(-padding, -padding); }
        private static void SetRect(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size, Vector2 pivot) { rect.anchorMin = rect.anchorMax = anchor; rect.pivot = pivot; rect.sizeDelta = size; rect.anchoredPosition = position; rect.localScale = Vector3.one; }
        private static void Require(SerializedObject target, string propertyName) { if (target.FindProperty(propertyName)?.objectReferenceValue == null) throw new InvalidOperationException(propertyName + " is not assigned."); }

        private readonly struct SettingsReferences
        {
            public readonly GameObject Panel;
            public readonly Button CloseButton;
            public readonly Toggle Music;
            public readonly Toggle Sound;
            public readonly Toggle Vibration;
            public SettingsReferences(GameObject panel, Button closeButton, Toggle music, Toggle sound, Toggle vibration) { Panel = panel; CloseButton = closeButton; Music = music; Sound = sound; Vibration = vibration; }
        }
    }
}
#endif
