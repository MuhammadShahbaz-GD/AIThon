#if UNITY_EDITOR
using System.Text;
using KickTheBuddy.Physics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KickTheBuddy.Editor
{
    /// <summary>Non-destructive authoring UI for creating and tuning basic 2D ragdolls.</summary>
    public sealed class RagdollAuthoringWindow : EditorWindow
    {
        private string ragdollName = "New Buddy";
        private Vector2 spawnPosition = Vector2.zero;
        private float scale = 1f;
        private float gravityScale = 1.35f;
        private float massMultiplier = 1f;
        private float standUpDelay = 2f;
        private float standingStrength = 95f;
        private float getUpStrength = 220f;
        private Color skinColor = new Color(1f, 0.72f, 0.44f);
        private Color bodyColor = new Color(0.20f, 0.62f, 0.95f);
        private Color limbColor = new Color(0.28f, 0.72f, 0.98f);
        private Vector2 scroll;

        [MenuItem("Tools/Ragdoll/Authoring Tool")]
        private static void Open() => GetWindow<RagdollAuthoringWindow>("Ragdoll Authoring");

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Create New Ragdoll", EditorStyles.boldLabel);
            ragdollName = EditorGUILayout.TextField("Name", ragdollName);
            spawnPosition = EditorGUILayout.Vector2Field("Spawn Position", spawnPosition);
            scale = EditorGUILayout.Slider("Overall Scale", scale, 0.4f, 3f);
            DrawAppearance();
            DrawPhysics();

            if (GUILayout.Button("Create New Ragdoll", GUILayout.Height(30f)))
                Selection.activeGameObject = RagdollEditorFactory.Create(ragdollName, spawnPosition, scale, skinColor, bodyColor, limbColor);

            EditorGUILayout.Space(14f);
            EditorGUILayout.LabelField("Customize Existing Ragdoll", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Select a ragdoll root or any of its body parts, then apply settings. The operation supports Undo and does not rebuild or delete the hierarchy.", MessageType.Info);
            using (new EditorGUI.DisabledScope(FindSelectedRoot() == null))
            {
                if (GUILayout.Button("Apply Settings to Selected")) ApplyToSelected();
                if (GUILayout.Button("Validate Selected Ragdoll")) ValidateSelected();
                if (GUILayout.Button("Select Ragdoll Root")) Selection.activeGameObject = FindSelectedRoot();
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawAppearance()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Appearance", EditorStyles.boldLabel);
            skinColor = EditorGUILayout.ColorField("Head", skinColor);
            bodyColor = EditorGUILayout.ColorField("Torso", bodyColor);
            limbColor = EditorGUILayout.ColorField("Limbs", limbColor);
        }

        private void DrawPhysics()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Physics and Standing Recovery", EditorStyles.boldLabel);
            gravityScale = EditorGUILayout.Slider("Gravity Scale", gravityScale, 0f, 5f);
            massMultiplier = EditorGUILayout.Slider("Mass Multiplier", massMultiplier, 0.25f, 4f);
            standUpDelay = EditorGUILayout.Slider("Stand Up Delay (Seconds)", standUpDelay, 0f, 10f);
            standingStrength = EditorGUILayout.Slider("Standing Strength", standingStrength, 0f, 300f);
            getUpStrength = EditorGUILayout.Slider("Get Up Strength", getUpStrength, 0f, 500f);
            EditorGUILayout.HelpBox("A healthy fallen ragdoll remains passive for the delay, then uses pose and balance assistance to stand again.", MessageType.Info);
        }

        private GameObject FindSelectedRoot()
        {
            if (Selection.activeGameObject == null) return null;
            RagdollController controller = Selection.activeGameObject.GetComponentInParent<RagdollController>();
            return controller != null ? controller.gameObject : null;
        }

        private void ApplyToSelected()
        {
            GameObject root = FindSelectedRoot();
            if (root == null) return;
            Undo.SetCurrentGroupName("Customize Ragdoll");
            Rigidbody2D[] bodies = root.GetComponentsInChildren<Rigidbody2D>(true);
            foreach (Rigidbody2D body in bodies)
            {
                Undo.RecordObject(body, "Tune Ragdoll Body");
                body.gravityScale = gravityScale;
                body.mass = BaseMassFor(body.name) * massMultiplier;
                body.interpolation = RigidbodyInterpolation2D.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                EditorUtility.SetDirty(body);
            }

            foreach (SpriteRenderer renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
            {
                Undo.RecordObject(renderer, "Recolor Ragdoll");
                string part = renderer.name.ToLowerInvariant();
                renderer.color = part.Contains("head") ? skinColor : part.Contains("torso") ? bodyColor : limbColor;
                EditorUtility.SetDirty(renderer);
            }

            RagdollController controller = root.GetComponent<RagdollController>();
            SerializedObject serialized = new SerializedObject(controller);
            SetFloat(serialized, "standUpDelay", standUpDelay);
            SetFloat(serialized, "standingMotorTorque", standingStrength);
            SetFloat(serialized, "getUpMotorTorque", getUpStrength);
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(controller);

            EditorSceneManager.MarkSceneDirty(root.scene);
        }

        private static void SetFloat(SerializedObject target, string propertyName, float value)
        {
            SerializedProperty property = target.FindProperty(propertyName);
            if (property != null) property.floatValue = value;
        }

        private static float BaseMassFor(string bodyName)
        {
            string value = bodyName.ToLowerInvariant();
            if (value.Contains("torso")) return 2.4f;
            if (value.Contains("head")) return 1.2f;
            if (value.Contains("upper leg")) return 1.05f;
            if (value.Contains("lower leg")) return 0.85f;
            if (value.Contains("upper arm")) return 0.75f;
            return 0.6f;
        }

        private void ValidateSelected()
        {
            GameObject root = FindSelectedRoot();
            if (root == null) return;
            StringBuilder report = new StringBuilder();
            int bodies = root.GetComponentsInChildren<Rigidbody2D>(true).Length;
            int colliders = root.GetComponentsInChildren<Collider2D>(true).Length;
            HingeJoint2D[] joints = root.GetComponentsInChildren<HingeJoint2D>(true);
            report.AppendLine($"Bodies: {bodies}, Colliders: {colliders}, Hinges: {joints.Length}");
            if (bodies < 6) report.AppendLine("Warning: expected at least six body parts.");
            foreach (HingeJoint2D joint in joints)
                if (joint.connectedBody == null) report.AppendLine($"Missing connected body: {joint.name}");
            foreach (Rigidbody2D body in root.GetComponentsInChildren<Rigidbody2D>(true))
                if (body.GetComponent<Collider2D>() == null) report.AppendLine($"Missing collider: {body.name}");
            EditorUtility.DisplayDialog("Ragdoll Validation", report.ToString(), "OK");
        }
    }

    /// <summary>Stateless construction service shared by editor-facing authoring workflows.</summary>
    internal static class RagdollEditorFactory
    {
        private static Sprite sprite;

        public static GameObject Create(string name, Vector2 position, float scale, Color skin, Color torsoColor, Color limbColor)
        {
            sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            GameObject root = new GameObject(string.IsNullOrWhiteSpace(name) ? "New Buddy" : name);
            Undo.RegisterCreatedObjectUndo(root, "Create Ragdoll");
            root.transform.position = position;
            root.transform.localScale = Vector3.one * scale;
            Undo.AddComponent<RagdollController>(root);
            Undo.AddComponent<RagdollLifeVisuals>(root);

            Rigidbody2D torso = Box(root.transform, "Torso", new Vector2(0f, 1.25f), new Vector2(1.35f, 2.05f), torsoColor, 2.4f, 10);
            Rigidbody2D head = Circle(root.transform, "Head", new Vector2(0f, 3.05f), 0.68f, skin, 1.2f, 12);
            Joint(head, torso, new Vector2(0f, -0.62f), -25f, 25f);
            Arm(root.transform, torso, true, limbColor); Arm(root.transform, torso, false, limbColor);
            Leg(root.transform, torso, true, limbColor); Leg(root.transform, torso, false, limbColor);
            EditorSceneManager.MarkSceneDirty(root.scene);
            return root;
        }

        private static void Arm(Transform root, Rigidbody2D torso, bool left, Color color)
        {
            float s = left ? -1f : 1f; string n = left ? "Left" : "Right";
            Rigidbody2D upper = Box(root, n + " Upper Arm", new Vector2(1.05f * s, 1.75f), new Vector2(.48f, 1.45f), color, .75f, 9);
            Rigidbody2D lower = Box(root, n + " Lower Arm", new Vector2(1.05f * s, .45f), new Vector2(.42f, 1.35f), color, .6f, 8);
            Joint(upper, torso, new Vector2(0f, .62f), -85f, 85f); Joint(lower, upper, new Vector2(0f, .62f), -15f, 130f);
        }

        private static void Leg(Transform root, Rigidbody2D torso, bool left, Color color)
        {
            float s = left ? -1f : 1f; string n = left ? "Left" : "Right";
            Rigidbody2D upper = Box(root, n + " Upper Leg", new Vector2(.4f * s, -.35f), new Vector2(.58f, 1.65f), color, 1.05f, 7);
            Rigidbody2D lower = Box(root, n + " Lower Leg", new Vector2(.4f * s, -1.85f), new Vector2(.5f, 1.55f), color, .85f, 6);
            Joint(upper, torso, new Vector2(0f, .72f), -45f, 45f); Joint(lower, upper, new Vector2(0f, .72f), -130f, 10f);
        }

        private static Rigidbody2D Box(Transform parent, string name, Vector2 pos, Vector2 size, Color color, float mass, int order)
        {
            GameObject go = Visual(parent, name, pos, size, color, order);
            Undo.AddComponent<BoxCollider2D>(go);
            return Body(go, mass);
        }

        private static Rigidbody2D Circle(Transform parent, string name, Vector2 pos, float radius, Color color, float mass, int order)
        {
            GameObject go = Visual(parent, name, pos, Vector2.one * radius * 2f, color, order);
            CircleCollider2D collider = Undo.AddComponent<CircleCollider2D>(go); collider.radius = .5f;
            return Body(go, mass);
        }

        private static GameObject Visual(Transform parent, string name, Vector2 pos, Vector2 size, Color color, int order)
        {
            GameObject go = new GameObject(name, typeof(SpriteRenderer)); Undo.RegisterCreatedObjectUndo(go, "Create Ragdoll Part");
            go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = new Vector3(size.x, size.y, 1f);
            SpriteRenderer renderer = go.GetComponent<SpriteRenderer>(); renderer.sprite = sprite; renderer.color = color; renderer.sortingOrder = order;
            return go;
        }

        private static Rigidbody2D Body(GameObject go, float mass)
        {
            Rigidbody2D body = Undo.AddComponent<Rigidbody2D>(go); body.mass = mass; body.gravityScale = 1.35f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate; body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            return body;
        }

        private static void Joint(Rigidbody2D child, Rigidbody2D parent, Vector2 anchor, float min, float max)
        {
            HingeJoint2D joint = Undo.AddComponent<HingeJoint2D>(child.gameObject); joint.connectedBody = parent;
            joint.autoConfigureConnectedAnchor = true; joint.anchor = anchor; joint.useLimits = true; joint.enableCollision = false;
            JointAngleLimits2D limits = joint.limits; limits.min = min; limits.max = max; joint.limits = limits;
        }
    }
}
#endif
