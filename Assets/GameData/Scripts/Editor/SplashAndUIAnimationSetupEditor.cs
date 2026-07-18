#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using KickTheBuddy.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KickTheBuddy.Editor
{
    /// <summary>Authors the supplied splash artwork and reusable DOTween UI entrances.</summary>
    public static class SplashAndUIAnimationSetupEditor
    {
        private const string SplashScene = "Assets/GameData/Scene/Splash.unity";
        private const string MenuScene = "Assets/GameData/Scene/MainMenu.unity";
        private const string GameplayScene = "Assets/GameData/Scene/RagdollSandbox.unity";
        private const string ArtRoot = "Assets/GameData/UI/Splash/";
        private const string MockupPath = ArtRoot + "splash_mocup.png";

        [MenuItem("Tools/Gameplay/UI/Apply Animated Splash And Scene UI")]
        public static void SetupBatch()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureArtworkImporters();
            BuildSplash();
            ApplySceneEntrance(MenuScene);
            ApplySceneEntrance(GameplayScene);
            AssetDatabase.SaveAssets();
            Validate();
            Debug.Log("ANIMATED_SPLASH_UI_OK: supplied splash applied, duration=3.5s, DOTween entrances authored in every built scene.");
        }

        private static void ConfigureArtworkImporters()
        {
            string[] files = { "character.png", "splash.jpg", "title.png", "splash_mocup.png" };
            for (int i = 0; i < files.Length; i++)
            {
                string path = ArtRoot + files[i];
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) throw new InvalidOperationException("Missing splash artwork: " + path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.maxTextureSize = 2048;
                TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
                android.overridden = true;
                android.maxTextureSize = 2048;
                android.format = TextureImporterFormat.ASTC_6x6;
                android.compressionQuality = 70;
                importer.SetPlatformTextureSettings(android);
                importer.SaveAndReimport();
            }
        }

        private static void BuildSplash()
        {
            Scene scene = EditorSceneManager.OpenScene(SplashScene, OpenSceneMode.Single);
            SplashScreenController controller = UnityEngine.Object.FindObjectOfType<SplashScreenController>(true);
            if (controller == null) throw new InvalidOperationException("SplashScreenController is missing.");
            Canvas canvas = controller.GetComponent<Canvas>();
            if (canvas == null) throw new InvalidOperationException("Splash controller has no Canvas.");

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
            Image artwork = CreateImage(canvas.transform, "Splash Artwork", RequireSprite(MockupPath));
            Stretch(artwork.rectTransform);
            artwork.preserveAspect = false;
            artwork.raycastTarget = false;

            Image loadingPanel = CreateImage(canvas.transform, "Animated Loading", null);
            loadingPanel.color = new Color(.28f, .04f, .38f, .86f);
            SetRect(loadingPanel.rectTransform, new Vector2(.5f, 0f), new Vector2(520f, 92f), new Vector2(0f, 36f));

            Text status = CreateText(loadingPanel.transform, "Loading Label", "LOADING...", 34);
            SetRect(status.rectTransform, new Vector2(.5f, .5f), new Vector2(460f, 48f), new Vector2(0f, 14f));
            status.color = Color.white;
            status.fontStyle = FontStyle.Bold;
            status.alignment = TextAnchor.MiddleCenter;
            Outline outline = status.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(.18f, .02f, .25f, .9f);
            outline.effectDistance = new Vector2(2f, -2f);

            Image track = CreateImage(loadingPanel.transform, "Loading Track", null);
            track.color = new Color(1f, 1f, 1f, .28f);
            SetRect(track.rectTransform, new Vector2(.5f, 0f), new Vector2(430f, 14f), new Vector2(0f, 12f));
            Image fill = CreateImage(track.transform, "Loading Fill", null);
            Stretch(fill.rectTransform);
            fill.color = new Color(1f, .76f, .08f, 1f);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            fill.fillAmount = 0f;

            CanvasGroup group = canvas.GetComponent<CanvasGroup>();
            if (group == null) group = Undo.AddComponent<CanvasGroup>(canvas.gameObject);
            SerializedObject serialized = new SerializedObject(controller);
            serialized.FindProperty("minimumDisplaySeconds").floatValue = 3.5f;
            serialized.FindProperty("splashGroup").objectReferenceValue = group;
            serialized.FindProperty("artwork").objectReferenceValue = artwork.rectTransform;
            serialized.FindProperty("loadingGroup").objectReferenceValue = loadingPanel.rectTransform;
            serialized.FindProperty("progressFill").objectReferenceValue = fill;
            serialized.FindProperty("statusText").objectReferenceValue = status;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(controller);
            Save(scene);
        }

        private static void ApplySceneEntrance(string scenePath)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Canvas[] canvases = UnityEngine.Object.FindObjectsByType<Canvas>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            int authored = 0;
            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas canvas = canvases[i];
                if (canvas == null || !canvas.isRootCanvas || canvas.renderMode == RenderMode.WorldSpace ||
                    canvas.transform.childCount == 0) continue;
                CanvasGroup group = canvas.GetComponent<CanvasGroup>();
                if (group == null) group = Undo.AddComponent<CanvasGroup>(canvas.gameObject);
                SceneUIEntranceAnimator animator = canvas.GetComponent<SceneUIEntranceAnimator>();
                if (animator == null) animator = Undo.AddComponent<SceneUIEntranceAnimator>(canvas.gameObject);

                List<RectTransform> targets = new List<RectTransform>(canvas.transform.childCount);
                for (int childIndex = 0; childIndex < canvas.transform.childCount; childIndex++)
                {
                    RectTransform target = canvas.transform.GetChild(childIndex) as RectTransform;
                    if (target == null || IsStaticBackground(target.name)) continue;
                    targets.Add(target);
                }
                SerializedObject serialized = new SerializedObject(animator);
                serialized.FindProperty("canvasGroup").objectReferenceValue = group;
                SerializedProperty entries = serialized.FindProperty("targets");
                entries.arraySize = targets.Count;
                for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
                    entries.GetArrayElementAtIndex(targetIndex).objectReferenceValue = targets[targetIndex];
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(animator);
                authored++;
            }
            if (authored == 0) throw new InvalidOperationException(scenePath + " has no animatable root UI Canvas.");
            Save(scene);
        }

        private static bool IsStaticBackground(string objectName) =>
            objectName.IndexOf("background", StringComparison.OrdinalIgnoreCase) >= 0 ||
            objectName.IndexOf("backdrop", StringComparison.OrdinalIgnoreCase) >= 0 ||
            objectName.IndexOf("room", StringComparison.OrdinalIgnoreCase) >= 0;

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
            text.resizeTextMinSize = 18;
            text.resizeTextMaxSize = size;
            text.raycastTarget = false;
            return text;
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

        private static Sprite RequireSprite(string path)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new InvalidOperationException("Splash sprite did not import: " + path);
            return sprite;
        }

        private static void Save(Scene scene)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Could not save scene: " + scene.path);
        }

        private static void Validate()
        {
            Scene splash = EditorSceneManager.OpenScene(SplashScene, OpenSceneMode.Single);
            SplashScreenController controller = UnityEngine.Object.FindObjectOfType<SplashScreenController>(true);
            if (controller == null || UnityEngine.Object.FindObjectOfType<CanvasGroup>(true) == null)
                throw new InvalidOperationException("Animated splash wiring is incomplete.");
            string[] animatedScenes = { MenuScene, GameplayScene };
            for (int i = 0; i < animatedScenes.Length; i++)
            {
                Scene scene = EditorSceneManager.OpenScene(animatedScenes[i], OpenSceneMode.Single);
                if (UnityEngine.Object.FindObjectOfType<SceneUIEntranceAnimator>(true) == null)
                    throw new InvalidOperationException(scene.path + " has no DOTween UI entrance animator.");
            }
            EditorSceneManager.OpenScene(SplashScene, OpenSceneMode.Single);
        }
    }
}
#endif
