#if UNITY_EDITOR
using System;
using KickTheBuddy.Gameplay;
using KickTheBuddy.Physics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KickTheBuddy.Editor
{
    /// <summary>Aligns the user-authored press level and registers it in single-scene progression.</summary>
    public static class HydraulicPressLevelSetupEditor
    {
        private const string ScenePath = "Assets/GameData/Scene/RagdollSandbox.unity";
        private const string LevelRootName = "Level 05 New -hydrolic press";
        private const string LevelId = "level_05";
        private const string LevelAssetPath = "Assets/GameData/Materials/Gameplay/Level_05.asset";
        private const string CatalogPath = "Assets/GameData/Materials/Gameplay/Level Catalog.asset";
        private const string BaseSpritePath = "Assets/GameData/Art/Level 5 Hydrolic/Hydraulic Pess Base.png";
        private const string TopSpritePath = "Assets/GameData/Art/Level 5 Hydrolic/Hydraulic Pess top.png";
        private const string PrefabFolder = "Assets/GameData/Prefabs/Gameplay";
        private const string PrefabPath = PrefabFolder + "/HydraulicPress.prefab";

        [MenuItem("Tools/Gameplay/Levels/Integrate Level 05 Hydraulic Press")]
        public static void Integrate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before integrating Level 05.");

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject levelRoot = FindSceneObject(LevelRootName);
            if (levelRoot == null) throw new InvalidOperationException("Missing authored root: " + LevelRootName);
            Transform levelsRoot = levelRoot.transform.parent;
            if (levelsRoot == null || levelsRoot.name != "Levels")
                throw new InvalidOperationException(LevelRootName + " must be a direct child of Levels.");

            Undo.RecordObject(levelRoot.transform, "Align Hydraulic Press Level");
            levelRoot.transform.localPosition = Vector3.zero;
            levelRoot.transform.localRotation = Quaternion.identity;
            levelRoot.transform.localScale = Vector3.one;

            HydroicPress press = ConfigureMachine(levelRoot.transform);
            ConfigureLevelController(levelsRoot.GetComponent<GameplayLevelSceneController>(), levelRoot, press);
            LevelDefinition definition = ConfigureLevelDefinition();
            ConfigureCatalog(definition);
            levelRoot.SetActive(false);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Could not save Level 05 integration.");
            AssetDatabase.SaveAssets();
            Validate();
            Debug.Log("HYDRAULIC_PRESS_LEVEL_05_OK: authored root aligned, press prefab saved, input/damage cycle wired, and level_05 appended to progression.");
        }

        private static HydroicPress ConfigureMachine(Transform levelRoot)
        {
            Transform machine = levelRoot.Find("Candy Playground Interactive Tools");
            if (machine == null) machine = levelRoot.Find("Hydraulic Press");
            if (machine == null) throw new InvalidOperationException("Hydraulic press machine hierarchy is missing.");
            Undo.RecordObject(machine.gameObject, "Rename Hydraulic Press");
            machine.name = "Hydraulic Press";
            machine.localRotation = Quaternion.identity;
            machine.localScale = Vector3.one;

            Transform baseTransform = FindDirectChild(machine, "Hydraulic Pess Base", "Hydraulic Press Base");
            Transform head = FindDirectChild(machine, "Hydraulic Pess Base (1)", "Hydraulic Press Head");
            if (baseTransform == null || head == null)
                throw new InvalidOperationException("Hydraulic press base or moving head is missing.");

            baseTransform.name = "Hydraulic Press Base";
            head.name = "Hydraulic Press Head";
            baseTransform.localPosition = new Vector3(-.07f, -3.62f, 0f);
            head.localPosition = new Vector3(-.07f, 4.89f, 0f);
            baseTransform.localRotation = head.localRotation = Quaternion.identity;
            baseTransform.localScale = head.localScale = Vector3.one;

            SpriteRenderer baseRenderer = RequireComponent<SpriteRenderer>(baseTransform.gameObject);
            SpriteRenderer headRenderer = RequireComponent<SpriteRenderer>(head.gameObject);
            baseRenderer.sprite = RequireSprite(BaseSpritePath);
            headRenderer.sprite = RequireSprite(TopSpritePath);
            baseRenderer.sortingOrder = 24;
            headRenderer.sortingOrder = 25;

            BoxCollider2D baseCollider = RequireComponent<BoxCollider2D>(baseTransform.gameObject);
            baseCollider.isTrigger = false;
            baseCollider.offset = Vector2.zero;
            baseCollider.size = baseRenderer.sprite.bounds.size;

            Animator animator = head.GetComponent<Animator>();
            if (animator != null) Undo.DestroyObjectImmediate(animator);
            Rigidbody2D movingBody = RequireComponent<Rigidbody2D>(head.gameObject);
            movingBody.bodyType = RigidbodyType2D.Kinematic;
            movingBody.gravityScale = 0f;
            movingBody.interpolation = RigidbodyInterpolation2D.Interpolate;
            movingBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            movingBody.useFullKinematicContacts = true;
            movingBody.constraints = RigidbodyConstraints2D.FreezeRotation;

            Transform plate = head.Find("Chocolate Bar B");
            if (plate == null) plate = head.Find("Press Hit Plate");
            if (plate == null) throw new InvalidOperationException("The authored press hit-plate child is missing.");
            plate.name = "Press Hit Plate";
            RemoveComponent<SandboxTool2D>(plate.gameObject);
            RemoveComponent<TargetJoint2D>(plate.gameObject);
            RemoveComponent<Rigidbody2D>(plate.gameObject);
            RemoveComponent<RagdollAttackManager2D>(plate.gameObject);
            RemoveComponent<SpriteRenderer>(plate.gameObject);
            plate.localPosition = new Vector3(0f, -4.32f, 0f);
            plate.localRotation = Quaternion.identity;
            plate.localScale = Vector3.one;
            BoxCollider2D strikeCollider = RequireComponent<BoxCollider2D>(plate.gameObject);
            strikeCollider.isTrigger = false;
            strikeCollider.offset = Vector2.zero;
            strikeCollider.size = new Vector2(3.1f, .72f);
            RagdollAttackManager2D attack = Undo.AddComponent<RagdollAttackManager2D>(plate.gameObject);
            attack.Configure(RagdollAttackType.Custom, 220f, 0f, 0f, 220f);
            attack.SetDamageEnabled(false);

            ParticleSystem particles = machine.GetComponentInChildren<ParticleSystem>(true);
            if (particles != null)
            {
                particles.gameObject.SetActive(true);
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            HydroicPress press = head.GetComponent<HydroicPress>();
            if (press == null) press = Undo.AddComponent<HydroicPress>(head.gameObject);
            SerializedObject pressData = new SerializedObject(press);
            Assign(pressData, "movingBody", movingBody);
            Assign(pressData, "movableRoot", machine);
            Assign(pressData, "tapSurface", headRenderer);
            Assign(pressData, "strikeCollider", strikeCollider);
            Assign(pressData, "attack", attack);
            Assign(pressData, "impactParticles", particles);
            pressData.FindProperty("pressIterationSpeed").floatValue = 1f;
            pressData.FindProperty("maximumBottomOffset").floatValue = 2.9f;
            pressData.FindProperty("downwardSpeed").floatValue = 18f;
            pressData.FindProperty("returnSpeed").floatValue = 2.2f;
            pressData.FindProperty("bottomHoldTime").floatValue = .22f;
            pressData.FindProperty("iterationDelay").floatValue = .32f;
            pressData.FindProperty("loopAfterActivation").boolValue = true;
            pressData.FindProperty("allowDragReposition").boolValue = true;
            pressData.FindProperty("dragThresholdPixels").floatValue = 12f;
            pressData.FindProperty("fixedDamage").floatValue = 220f;
            pressData.FindProperty("limbDownwardImpulse").floatValue = 34f;
            pressData.FindProperty("wholeBodyDownwardVelocity").floatValue = 7f;
            pressData.FindProperty("knockoutDuration").floatValue = .55f;
            pressData.ApplyModifiedPropertiesWithoutUndo();

            EnsureFolder(PrefabFolder);
            PrefabUtility.SaveAsPrefabAsset(machine.gameObject, PrefabPath, out bool saved);
            if (!saved) throw new InvalidOperationException("Could not create hydraulic press prefab.");
            return press;
        }

        private static void ConfigureLevelController(GameplayLevelSceneController controller,
            GameObject levelRoot, HydroicPress press)
        {
            if (controller == null) throw new InvalidOperationException("Levels controller is missing.");
            RagdollController ragdoll = UnityEngine.Object.FindObjectOfType<RagdollController>(true);
            RagdollInputManager input = ragdoll != null ? ragdoll.GetComponent<RagdollInputManager>() : null;
            if (ragdoll == null || input == null) throw new InvalidOperationException("Shared ragdoll references are missing.");

            SerializedObject data = new SerializedObject(controller);
            SerializedProperty levels = data.FindProperty("levels");
            int index = FindLevelEntry(levels, LevelId);
            if (index < 0)
            {
                index = levels.arraySize;
                levels.InsertArrayElementAtIndex(index);
            }
            SerializedProperty entry = levels.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("levelId").stringValue = LevelId;
            entry.FindPropertyRelative("root").objectReferenceValue = levelRoot;
            entry.FindPropertyRelative("ragdoll").objectReferenceValue = ragdoll;
            entry.FindPropertyRelative("ragdollInput").objectReferenceValue = input;
            entry.FindPropertyRelative("sandboxToolInput").objectReferenceValue = null;
            entry.FindPropertyRelative("candyCannons").objectReferenceValue = null;
            entry.FindPropertyRelative("levelFourPipes").objectReferenceValue = null;
            entry.FindPropertyRelative("hydraulicPress").objectReferenceValue = press;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(controller);
        }

        private static LevelDefinition ConfigureLevelDefinition()
        {
            LevelDefinition level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelAssetPath);
            if (level == null)
            {
                level = ScriptableObject.CreateInstance<LevelDefinition>();
                AssetDatabase.CreateAsset(level, LevelAssetPath);
            }
            SerializedObject data = new SerializedObject(level);
            data.FindProperty("levelId").stringValue = LevelId;
            data.FindProperty("displayName").stringValue = "Level 05 - Hydraulic Press";
            data.FindProperty("scenePath").stringValue = ScenePath;
            data.FindProperty("objectiveText").stringValue = "Tap the hydraulic press and crush the buddy until it breaks.";
            data.FindProperty("completionRule").enumValueIndex = (int)LevelCompletionRule.CharacterDestroyed;
            data.FindProperty("targetDamage").floatValue = 1800f;
            data.FindProperty("targetDurabilityMultiplier").floatValue = 6.5f;
            data.FindProperty("minimumHitsToDepletePart").intValue = 18;
            data.FindProperty("timeLimit").floatValue = 60f;
            data.FindProperty("completionCoins").intValue = 650;
            data.FindProperty("oneStarScore").intValue = 600;
            data.FindProperty("twoStarScore").intValue = 1050;
            data.FindProperty("threeStarScore").intValue = 1550;
            data.FindProperty("wallBaseDamage").floatValue = 0f;
            data.FindProperty("wallDamagePerSpeed").floatValue = 0f;
            data.FindProperty("wallMinimumImpactSpeed").floatValue = 4f;
            data.FindProperty("wallMaximumDamage").floatValue = 0f;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(level);
            return level;
        }

        private static void ConfigureCatalog(LevelDefinition level)
        {
            LevelCatalog catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(CatalogPath);
            if (catalog == null) throw new InvalidOperationException("Level Catalog is missing.");
            SerializedObject data = new SerializedObject(catalog);
            SerializedProperty levels = data.FindProperty("levels");
            for (int i = 0; i < levels.arraySize; i++)
            {
                if (levels.GetArrayElementAtIndex(i).objectReferenceValue == level) return;
            }
            int index = levels.arraySize;
            levels.InsertArrayElementAtIndex(index);
            levels.GetArrayElementAtIndex(index).objectReferenceValue = level;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
        }

        private static void Validate()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject root = FindSceneObject(LevelRootName);
            GameplayLevelSceneController controller = UnityEngine.Object.FindObjectOfType<GameplayLevelSceneController>(true);
            LevelCatalog catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(CatalogPath);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (root == null || root.transform.parent == null || root.transform.parent.name != "Levels" ||
                controller == null || !controller.HasAuthoredLevel(LevelId) || catalog == null ||
                catalog.IndexOf(LevelId) < 0 || prefab == null || prefab.GetComponentInChildren<HydroicPress>(true) == null)
                throw new InvalidOperationException("Hydraulic press Level 05 validation failed.");
        }

        private static int FindLevelEntry(SerializedProperty levels, string id)
        {
            for (int i = 0; i < levels.arraySize; i++)
                if (levels.GetArrayElementAtIndex(i).FindPropertyRelative("levelId").stringValue == id) return i;
            return -1;
        }

        private static Transform FindDirectChild(Transform parent, params string[] names)
        {
            for (int n = 0; n < names.Length; n++)
                for (int i = 0; i < parent.childCount; i++)
                    if (parent.GetChild(i).name == names[n]) return parent.GetChild(i);
            return null;
        }

        private static GameObject FindSceneObject(string objectName)
        {
            Transform[] transforms = UnityEngine.Object.FindObjectsOfType<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform candidate = transforms[i];
                if (candidate != null && candidate.name == objectName && candidate.gameObject.scene.IsValid())
                    return candidate.gameObject;
            }
            return null;
        }

        private static T RequireComponent<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : Undo.AddComponent<T>(target);
        }

        private static void RemoveComponent<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            if (component != null) Undo.DestroyObjectImmediate(component);
        }

        private static void Assign(SerializedObject data, string property, UnityEngine.Object value) =>
            data.FindProperty(property).objectReferenceValue = value;

        private static Sprite RequireSprite(string path)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new InvalidOperationException("Missing sprite: " + path);
            return sprite;
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
#endif
