#if UNITY_EDITOR
using KickTheBuddy.Physics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KickTheBuddy.Editor
{
    public static class RagdollSceneBuilder
    {
        private const string SceneFolder = "Assets/GameData/Scene";
        private const string ScenePath = SceneFolder + "/RagdollSandbox.unity";
        private static Sprite whiteSprite;

        [InitializeOnLoadMethod]
        private static void CreateMissingDemoOnReload()
        {
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))
                EditorApplication.delayCall += CreateScene;
        }

        [MenuItem("Tools/Ragdoll/Create Basic Sandbox Scene")]
        public static void CreateScene()
        {
            if (!AssetDatabase.IsValidFolder(SceneFolder)) AssetDatabase.CreateFolder("Assets", "Scenes");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            whiteSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            CreateCamera();
            CreateRoom();
            CreateRagdoll();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Selection.activeGameObject = GameObject.Find("Buddy");
            Debug.Log("Created playable ragdoll sandbox at " + ScenePath);
        }

        public static void AddLifeVisualsToSandbox()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject buddy = GameObject.Find("Buddy");
            if (buddy != null && buddy.GetComponent<RagdollLifeVisuals>() == null)
                buddy.AddComponent<RagdollLifeVisuals>();
            EditorSceneManager.SaveScene(scene);
        }

        private static void CreateCamera()
        {
            GameObject go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            go.tag = "MainCamera";
            go.transform.position = new Vector3(0f, 0.5f, -10f);
            Camera camera = go.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 6f;
            camera.backgroundColor = new Color(0.055f, 0.07f, 0.10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
        }

        private static void CreateRoom()
        {
            GameObject root = new GameObject("Room");
            CreateWall(root.transform, "Floor", new Vector2(0f, -4.25f), new Vector2(12f, 0.5f));
            CreateWall(root.transform, "Ceiling", new Vector2(0f, 5.75f), new Vector2(12f, 0.5f));
            CreateWall(root.transform, "Left Wall", new Vector2(-5.75f, 0.75f), new Vector2(0.5f, 10f));
            CreateWall(root.transform, "Right Wall", new Vector2(5.75f, 0.75f), new Vector2(0.5f, 10f));
        }

        private static void CreateWall(Transform parent, string name, Vector2 position, Vector2 size)
        {
            GameObject go = Visual(name, parent, position, size, new Color(0.18f, 0.22f, 0.30f), 0);
            go.AddComponent<BoxCollider2D>();
            RagdollAttackManager2D attack = go.AddComponent<RagdollAttackManager2D>();
            attack.Configure(RagdollAttackType.Wall, 0f, 1.25f, 4f,
                LongFunBalanceSetupEditor.MaximumRawDamagePerHit);
        }

        private static void CreateRagdoll()
        {
            GameObject root = new GameObject("Buddy");
            root.AddComponent<RagdollController>();
            root.AddComponent<RagdollLifeVisuals>();

            Rigidbody2D torso = CreateBox(root.transform, "Torso", new Vector2(0f, 1.25f), new Vector2(1.35f, 2.05f), new Color(0.20f, 0.62f, 0.95f), 2.4f, 10);
            Rigidbody2D head = CreateCircle(root.transform, "Head", new Vector2(0f, 3.05f), 0.68f, new Color(1f, 0.72f, 0.44f), 1.2f, 12);
            Connect(head, torso, new Vector2(0f, -0.62f), -25f, 25f);

            CreateArm(root.transform, torso, true);
            CreateArm(root.transform, torso, false);
            CreateLeg(root.transform, torso, true);
            CreateLeg(root.transform, torso, false);
        }

        private static void CreateArm(Transform root, Rigidbody2D torso, bool left)
        {
            float side = left ? -1f : 1f;
            string prefix = left ? "Left" : "Right";
            Color color = new Color(0.28f, 0.72f, 0.98f);
            Rigidbody2D upper = CreateBox(root, prefix + " Upper Arm", new Vector2(1.05f * side, 1.75f), new Vector2(0.48f, 1.45f), color, 0.75f, 9);
            Rigidbody2D lower = CreateBox(root, prefix + " Lower Arm", new Vector2(1.05f * side, 0.45f), new Vector2(0.42f, 1.35f), color, 0.6f, 8);
            Connect(upper, torso, new Vector2(0f, 0.62f), -85f, 85f);
            Connect(lower, upper, new Vector2(0f, 0.62f), -15f, 130f);
        }

        private static void CreateLeg(Transform root, Rigidbody2D torso, bool left)
        {
            float side = left ? -1f : 1f;
            string prefix = left ? "Left" : "Right";
            Color color = new Color(0.15f, 0.45f, 0.82f);
            Rigidbody2D upper = CreateBox(root, prefix + " Upper Leg", new Vector2(0.4f * side, -0.35f), new Vector2(0.58f, 1.65f), color, 1.05f, 7);
            Rigidbody2D lower = CreateBox(root, prefix + " Lower Leg", new Vector2(0.4f * side, -1.85f), new Vector2(0.5f, 1.55f), color, 0.85f, 6);
            Connect(upper, torso, new Vector2(0f, 0.72f), -45f, 45f);
            Connect(lower, upper, new Vector2(0f, 0.72f), -130f, 10f);
        }

        private static Rigidbody2D CreateBox(Transform parent, string name, Vector2 position, Vector2 size, Color color, float mass, int order)
        {
            GameObject go = Visual(name, parent, position, size, color, order);
            BoxCollider2D collider = go.AddComponent<BoxCollider2D>();
            collider.size = Vector2.one;
            return ConfigureBody(go, mass);
        }

        private static Rigidbody2D CreateCircle(Transform parent, string name, Vector2 position, float radius, Color color, float mass, int order)
        {
            GameObject go = Visual(name, parent, position, Vector2.one * radius * 2f, color, order);
            CircleCollider2D collider = go.AddComponent<CircleCollider2D>();
            collider.radius = 0.5f;
            return ConfigureBody(go, mass);
        }

        private static GameObject Visual(string name, Transform parent, Vector2 position, Vector2 size, Color color, int order)
        {
            GameObject go = new GameObject(name, typeof(SpriteRenderer));
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
            renderer.sprite = whiteSprite;
            renderer.color = color;
            renderer.sortingOrder = order;
            return go;
        }

        private static Rigidbody2D ConfigureBody(GameObject go, float mass)
        {
            Rigidbody2D body = go.AddComponent<Rigidbody2D>();
            body.mass = mass;
            body.gravityScale = 1.35f;
            body.drag = 0.15f;
            body.angularDrag = 0.2f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            return body;
        }

        private static void Connect(Rigidbody2D child, Rigidbody2D parent, Vector2 childAnchor, float minimum, float maximum)
        {
            HingeJoint2D joint = child.gameObject.AddComponent<HingeJoint2D>();
            joint.connectedBody = parent;
            joint.autoConfigureConnectedAnchor = true;
            joint.anchor = childAnchor;
            joint.useLimits = true;
            JointAngleLimits2D limits = joint.limits;
            limits.min = minimum;
            limits.max = maximum;
            joint.limits = limits;
            joint.enableCollision = false;
        }
    }
}
#endif
