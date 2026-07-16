#if UNITY_EDITOR
using KickTheBuddy.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KickTheBuddy.Editor
{
    public static class DeathPopupSetupEditor
    {
        public static void Install()
        {
            const string scenePath = "Assets/GameData/Scene/RagdollSandbox.unity";
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            GameplayHUD hud = Object.FindObjectOfType<GameplayHUD>();
            if (hud == null) throw new System.InvalidOperationException("GameplayHUD was not found.");
            Canvas canvas = hud.GetComponent<Canvas>(); if (canvas == null) canvas = hud.GetComponentInParent<Canvas>();
            if (canvas == null) throw new System.InvalidOperationException("Gameplay Canvas was not found.");

            Transform existing = canvas.transform.Find("Level Complete Popup");
            GameObject popup = existing != null ? existing.gameObject : BuildPopup(canvas.transform);
            Text result = popup.transform.Find("Card/Result Text").GetComponent<Text>();
            Button restart = popup.transform.Find("Card/Restart Button").GetComponent<Button>();
            Button next = popup.transform.Find("Card/Next Button").GetComponent<Button>();

            SerializedObject hudObject = new SerializedObject(hud);
            hudObject.FindProperty("resultPanel").objectReferenceValue = popup;
            hudObject.FindProperty("resultText").objectReferenceValue = result;
            hudObject.FindProperty("restartButton").objectReferenceValue = restart;
            hudObject.FindProperty("nextButton").objectReferenceValue = next;
            hudObject.ApplyModifiedPropertiesWithoutUndo();
            popup.SetActive(false);

            if (Object.FindObjectOfType<EventSystem>() == null) new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            EditorUtility.SetDirty(hud); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        }

        private static GameObject BuildPopup(Transform parent)
        {
            GameObject popup = UI("Level Complete Popup", parent, typeof(Image)); Stretch(popup.GetComponent<RectTransform>()); Image overlay = popup.GetComponent<Image>(); overlay.color = new Color(.02f, .025f, .04f, .82f); overlay.raycastTarget = true;
            GameObject card = UI("Card", popup.transform, typeof(Image)); RectTransform cardRect = card.GetComponent<RectTransform>(); cardRect.anchorMin = cardRect.anchorMax = new Vector2(.5f, .5f); cardRect.sizeDelta = new Vector2(760, 620); cardRect.anchoredPosition = Vector2.zero; card.GetComponent<Image>().color = new Color(.10f, .13f, .20f, .98f);
            Text result = Label("Result Text", card.transform, new Vector2(0, 130), new Vector2(680, 300), 52); result.text = "LEVEL COMPLETE";
            Button restart = Button("Restart Button", card.transform, new Vector2(-185, -190), "RESTART");
            Button next = Button("Next Button", card.transform, new Vector2(185, -190), "NEXT");
            return popup;
        }
        private static Button Button(string name, Transform parent, Vector2 position, string caption)
        {
            GameObject go = UI(name, parent, typeof(Image), typeof(Button)); RectTransform rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.sizeDelta = new Vector2(300, 110); rect.anchoredPosition = position; go.GetComponent<Image>().color = new Color(.16f, .55f, .92f, 1f); Text label = Label("Label", go.transform, Vector2.zero, rect.sizeDelta, 34); label.text = caption; return go.GetComponent<Button>();
        }
        private static Text Label(string name, Transform parent, Vector2 position, Vector2 size, int fontSize)
        {
            GameObject go = UI(name, parent, typeof(Text)); RectTransform rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.sizeDelta = size; rect.anchoredPosition = position; Text text = go.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.fontSize = fontSize; text.alignment = TextAnchor.MiddleCenter; text.color = Color.white; text.raycastTarget = false; return text;
        }
        private static GameObject UI(string name, Transform parent, params System.Type[] components) { GameObject go = new GameObject(name, components); go.transform.SetParent(parent, false); return go; }
        private static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    }
}
#endif
