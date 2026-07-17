#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using KickTheBuddy.Gameplay;
using KickTheBuddy.Physics;
using KickTheBuddy.VFX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KickTheBuddy.Editor
{
    /// <summary>
    /// Maintains all authored gameplay levels inside one shipping scene. The legacy CandyLab scene is
    /// retained as a non-build source asset, while runtime progression only reloads RagdollSandbox.
    /// </summary>
    public static class SingleSceneLevelsSetupEditor
    {
        public const string GameplayScenePath = "Assets/GameData/Scene/RagdollSandbox.unity";
        public const string LegacyLevelTwoScenePath = "Assets/GameData/Scene/CandyLab.unity";
        public const string LevelsRootName = "Levels";
        public const string LevelOneRootName = "Level 01 - Wall Smash";
        public const string LevelTwoRootName = "Level 02 - Candy Lab";
        public const string LevelThreeRootName = "Level 03 - Candy Cannons";

        private const string SplashPath = "Assets/GameData/Scene/Splash.unity";
        private const string MenuPath = "Assets/GameData/Scene/MainMenu.unity";
        private const string LevelOneAssetPath = "Assets/GameData/Materials/Gameplay/Level_01.asset";
        private const string LevelTwoAssetPath = "Assets/GameData/Materials/Gameplay/Level_02.asset";
        private const string LevelThreeAssetPath = "Assets/GameData/Materials/Gameplay/Level_03.asset";
        private const string LollipopPrefabPath = "Assets/GameData/Prefabs/Gameplay/Lollipop.prefab";
        private const string JellyPrefabPath = "Assets/GameData/Prefabs/Gameplay/Jelly.prefab";
        private const string JellyPoolPrefabPath = "Assets/GameData/Prefabs/VFX/VFX_Jelly_ContactPool.prefab";
        private const string ToolsRootName = "Level 2 Interactive Tools";
        private const float LevelOneWallDamagePerSpeed = 1.25f;
        private const float LevelOneWallMinimumSpeed = 4f;
        private const float LevelOneWallMaximumDamage = 16f;
        private const float LevelTwoWallDamagePerSpeed = .85f;
        private const float LevelTwoWallMinimumSpeed = 4.5f;
        private const float LevelTwoWallMaximumDamage = 9f;
        private const float LevelThreeWallDamagePerSpeed = .55f;
        private const float LevelThreeWallMinimumSpeed = 5f;
        private const float LevelThreeWallMaximumDamage = 8f;

        [MenuItem("Tools/Game/Build Single-Scene Level Hierarchy")]
        public static void BuildFromMenu() => Build(false);

        public static void BuildBatch() => Build(true);

        [MenuItem("Tools/Game/Validate Single-Scene Level Hierarchy")]
        public static void ValidateFromMenu() => Validate(false);

        public static void ValidateBatch() => Validate(true);

        private static void Build(bool exitWhenDone)
        {
            try
            {
                ConfigureLevelDefinitions();
                AuthorGameplayScene();
                EditorBuildSettings.scenes = new[]
                {
                    new EditorBuildSettingsScene(SplashPath, true),
                    new EditorBuildSettingsScene(MenuPath, true),
                    new EditorBuildSettingsScene(GameplayScenePath, true)
                };
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                ValidateOrThrow();
                Debug.Log("SINGLE_SCENE_LEVELS_BUILD_OK: one shared Levels/Room and all available level roots are authored in RagdollSandbox.");
                if (exitWhenDone && Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (exitWhenDone && Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        private static void Validate(bool exitWhenDone)
        {
            try
            {
                ValidateOrThrow();
                if (exitWhenDone && Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (exitWhenDone && Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        public static void ValidateOrThrow()
        {
            LevelDefinition levelOne = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelOneAssetPath);
            LevelDefinition levelTwo = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelTwoAssetPath);
            LevelDefinition levelThree = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelThreeAssetPath);
            if (levelOne == null || levelTwo == null)
                throw new InvalidOperationException("Both LevelDefinition assets are required.");
            int expectedLevelCount = levelThree != null ? 3 : 2;
            if (!string.Equals(levelOne.ScenePath, GameplayScenePath, StringComparison.Ordinal) ||
                !string.Equals(levelTwo.ScenePath, GameplayScenePath, StringComparison.Ordinal))
                throw new InvalidOperationException("Every gameplay level must point to the one RagdollSandbox scene.");
            ValidateDefinitionRoomProfile(levelOne, LevelOneWallDamagePerSpeed,
                LevelOneWallMinimumSpeed, LevelOneWallMaximumDamage);
            ValidateDefinitionRoomProfile(levelTwo, LevelTwoWallDamagePerSpeed,
                LevelTwoWallMinimumSpeed, LevelTwoWallMaximumDamage);
            if (levelThree != null)
                ValidateDefinitionRoomProfile(levelThree, LevelThreeWallDamagePerSpeed,
                    LevelThreeWallMinimumSpeed, LevelThreeWallMaximumDamage);

            EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes;
            if (buildScenes.Length != 3 || buildScenes[0].path != SplashPath || buildScenes[1].path != MenuPath ||
                buildScenes[2].path != GameplayScenePath || !buildScenes[2].enabled)
                throw new InvalidOperationException("Build Settings must contain Splash, MainMenu, and exactly one gameplay scene.");

            Scene scene = EditorSceneManager.OpenScene(GameplayScenePath, OpenSceneMode.Single);
            GameObject levelsRoot = FindRoot(scene, LevelsRootName);
            if (levelsRoot == null) throw new InvalidOperationException("The gameplay scene is missing its Levels root.");
            GameplayLevelSceneController controller = levelsRoot.GetComponent<GameplayLevelSceneController>();
            if (controller == null || controller.LevelCount != expectedLevelCount)
                throw new InvalidOperationException("Levels has an incorrect GameplayLevelSceneController entry count.");

            Transform levelOneRoot = FindDirectChild(levelsRoot.transform, LevelOneRootName);
            Transform levelTwoRoot = FindDirectChild(levelsRoot.transform, LevelTwoRootName);
            Transform levelThreeRoot = FindDirectChild(levelsRoot.transform, LevelThreeRootName);
            if (levelOneRoot == null || levelTwoRoot == null)
                throw new InvalidOperationException("Levels must contain the Level 01 and Level 02 child roots.");
            if (levelThree != null && levelThreeRoot == null)
                throw new InvalidOperationException("Levels must contain the authored Level 03 child root.");
            if (!levelOneRoot.gameObject.activeSelf || levelTwoRoot.gameObject.activeSelf ||
                (levelThreeRoot != null && levelThreeRoot.gameObject.activeSelf))
                throw new InvalidOperationException("The preview must enable Level 01 and disable later levels.");

            Transform sharedRoom = FindDirectChild(levelsRoot.transform, "Room");
            if (sharedRoom == null || !sharedRoom.gameObject.activeSelf)
                throw new InvalidOperationException("Levels must contain one active shared Room child.");
            if (FindDirectChild(levelOneRoot, "Room") != null || FindDirectChild(levelTwoRoot, "Room") != null)
                throw new InvalidOperationException("Room must be shared under Levels, never duplicated inside a level root.");
            if (levelThreeRoot != null && FindDirectChild(levelThreeRoot, "Room") != null)
                throw new InvalidOperationException("Level 03 must also use the shared Room.");
            ValidateRoom(sharedRoom, "Levels/Room", LevelOneWallDamagePerSpeed,
                LevelOneWallMinimumSpeed, LevelOneWallMaximumDamage);
            if (levelOneRoot.GetComponentInChildren<SandboxToolInput2D>(true) != null)
                throw new InvalidOperationException("Level 01 must not contain Level 02 tools.");
            ValidateLevelTwoTools(levelTwoRoot);

            SerializedObject controllerData = new SerializedObject(controller);
            SerializedProperty entries = controllerData.FindProperty("levels");
            if (entries == null || entries.arraySize != expectedLevelCount)
                throw new InvalidOperationException("The Levels controller entries are not fully authored.");
            ValidateEntry(entries.GetArrayElementAtIndex(0), "level_01", levelOneRoot.gameObject, false, false);
            ValidateEntry(entries.GetArrayElementAtIndex(1), "level_02", levelTwoRoot.gameObject, true, false);
            if (levelThree != null)
                ValidateEntry(entries.GetArrayElementAtIndex(2), "level_03",
                    levelThreeRoot.gameObject, false, true);
            SerializedProperty sharedRoomReference = controllerData.FindProperty("sharedRoom");
            SerializedProperty sharedAttacks = controllerData.FindProperty("sharedRoomAttacks");
            if (sharedRoomReference == null || sharedRoomReference.objectReferenceValue != sharedRoom.gameObject ||
                sharedAttacks == null || sharedAttacks.arraySize == 0)
                throw new InvalidOperationException("The Levels controller is missing explicit shared Room references.");

            Collider2D[] roomBoundaries = sharedRoom.GetComponentsInChildren<Collider2D>(true);
            HashSet<RagdollAttackManager2D> expectedAttacks = new HashSet<RagdollAttackManager2D>();
            for (int i = 0; i < roomBoundaries.Length; i++)
            {
                RagdollAttackManager2D expected = roomBoundaries[i].GetComponent<RagdollAttackManager2D>();
                if (expected != null) expectedAttacks.Add(expected);
            }
            if (sharedAttacks.arraySize != expectedAttacks.Count)
                throw new InvalidOperationException("The shared Room attack array does not cover every wall exactly once.");
            HashSet<RagdollAttackManager2D> assignedAttacks = new HashSet<RagdollAttackManager2D>();
            for (int i = 0; i < sharedAttacks.arraySize; i++)
            {
                RagdollAttackManager2D assigned =
                    sharedAttacks.GetArrayElementAtIndex(i).objectReferenceValue as RagdollAttackManager2D;
                if (assigned == null || !expectedAttacks.Contains(assigned) || !assignedAttacks.Add(assigned))
                    throw new InvalidOperationException("The shared Room attack array contains a null, duplicate, or foreign reference.");
            }

            if (FindRoot(scene, "Room") != null || FindRoot(scene, ToolsRootName) != null)
                throw new InvalidOperationException("Room and tool objects must live under the Levels hierarchy.");
            if (CountMissingScripts(scene) != 0)
                throw new InvalidOperationException("The single gameplay scene contains missing script references.");

            Debug.Log("SINGLE_SCENE_LEVELS_VALIDATION_OK: one shared Levels/Room, all catalog content roots, per-level room profiles, tools, and zero missing scripts.");
        }

        private static void ValidateDefinitionRoomProfile(LevelDefinition definition, float damagePerSpeed,
            float minimumSpeed, float maximumDamage)
        {
            if (!Mathf.Approximately(definition.WallBaseDamage, 0f) ||
                !Mathf.Approximately(definition.WallDamagePerSpeed, damagePerSpeed) ||
                !Mathf.Approximately(definition.WallMinimumImpactSpeed, minimumSpeed) ||
                !Mathf.Approximately(definition.WallMaximumDamage, maximumDamage))
                throw new InvalidOperationException(definition.name + " has an incorrect shared Room damage profile.");
        }

        private static void ConfigureLevelDefinitions()
        {
            ConfigureLevelDefinition(LevelOneAssetPath, LevelOneWallDamagePerSpeed,
                LevelOneWallMinimumSpeed, LevelOneWallMaximumDamage);
            ConfigureLevelDefinition(LevelTwoAssetPath, LevelTwoWallDamagePerSpeed,
                LevelTwoWallMinimumSpeed, LevelTwoWallMaximumDamage);
            if (AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelThreeAssetPath) != null)
                ConfigureLevelDefinition(LevelThreeAssetPath, LevelThreeWallDamagePerSpeed,
                    LevelThreeWallMinimumSpeed, LevelThreeWallMaximumDamage);
        }

        private static void ConfigureLevelDefinition(string assetPath, float wallDamagePerSpeed,
            float wallMinimumSpeed, float wallMaximumDamage)
        {
            LevelDefinition definition = AssetDatabase.LoadAssetAtPath<LevelDefinition>(assetPath);
            if (definition == null) throw new InvalidOperationException("Missing level definition: " + assetPath);
            SerializedObject data = new SerializedObject(definition);
            data.FindProperty("scenePath").stringValue = GameplayScenePath;
            data.FindProperty("wallBaseDamage").floatValue = 0f;
            data.FindProperty("wallDamagePerSpeed").floatValue = wallDamagePerSpeed;
            data.FindProperty("wallMinimumImpactSpeed").floatValue = wallMinimumSpeed;
            data.FindProperty("wallMaximumDamage").floatValue = wallMaximumDamage;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
        }

        private static void AuthorGameplayScene()
        {
            Scene scene = EditorSceneManager.OpenScene(GameplayScenePath, OpenSceneMode.Single);
            RagdollController ragdoll = FindAuthoredRagdoll(scene);
            if (ragdoll == null) throw new InvalidOperationException("RagdollSandbox has no active authored ragdoll.");
            RagdollInputManager ragdollInput = ragdoll.GetComponent<RagdollInputManager>();
            if (ragdollInput == null) throw new InvalidOperationException("The active ragdoll is missing RagdollInputManager.");
            Camera camera = FindSceneComponent<Camera>(scene);
            if (camera == null) throw new InvalidOperationException("RagdollSandbox has no authored camera.");

            GameObject levelsRoot = FindRoot(scene, LevelsRootName);
            if (levelsRoot == null)
            {
                levelsRoot = new GameObject(LevelsRootName);
                SceneManager.MoveGameObjectToScene(levelsRoot, scene);
            }
            levelsRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            levelsRoot.transform.localScale = Vector3.one;

            Transform levelOne = EnsureDirectChild(levelsRoot.transform, LevelOneRootName);
            Transform levelTwo = EnsureDirectChild(levelsRoot.transform, LevelTwoRootName);
            GameObject sharedRoom = FindDirectChild(levelsRoot.transform, "Room")?.gameObject;
            GameObject levelOneRoom = FindDirectChild(levelOne, "Room")?.gameObject;
            GameObject levelTwoRoom = FindDirectChild(levelTwo, "Room")?.gameObject;
            GameObject topLevelRoom = FindRoot(scene, "Room");
            if (sharedRoom == null) sharedRoom = levelOneRoom != null ? levelOneRoom : topLevelRoom;
            if (sharedRoom == null) sharedRoom = levelTwoRoom;
            if (sharedRoom == null) sharedRoom = CloneLegacyRoom(scene, levelsRoot.transform);
            if (sharedRoom == null)
                throw new InvalidOperationException("The shared Levels/Room hierarchy could not be authored.");
            sharedRoom.name = "Room";
            sharedRoom.transform.SetParent(levelsRoot.transform, true);
            sharedRoom.SetActive(true);

            if (levelOneRoom != null && levelOneRoom != sharedRoom)
                UnityEngine.Object.DestroyImmediate(levelOneRoom);
            if (levelTwoRoom != null && levelTwoRoom != sharedRoom)
                UnityEngine.Object.DestroyImmediate(levelTwoRoom);
            if (topLevelRoom != null && topLevelRoom != sharedRoom)
                UnityEngine.Object.DestroyImmediate(topLevelRoom);

            sharedRoom.transform.SetSiblingIndex(0);
            levelOne.SetSiblingIndex(1);
            levelTwo.SetSiblingIndex(2);

            GameObject toolsRoot = FindDirectChild(levelTwo, ToolsRootName)?.gameObject;
            GameObject topLevelTools = FindRoot(scene, ToolsRootName);
            if (toolsRoot == null && topLevelTools != null)
            {
                topLevelTools.transform.SetParent(levelTwo, true);
                toolsRoot = topLevelTools;
            }

            if (toolsRoot == null) CloneMissingLevelTwoTools(scene, levelTwo, ref toolsRoot);
            if (toolsRoot == null) toolsRoot = CreateFallbackTools(scene, levelTwo);

            SandboxToolInput2D toolInput = toolsRoot.GetComponent<SandboxToolInput2D>();
            if (toolInput == null) throw new InvalidOperationException("Level 02 tools are missing SandboxToolInput2D.");
            RebindLevelTwoReferences(toolInput, toolsRoot, camera, ragdoll);

            GameplayLevelSceneController sceneController = levelsRoot.GetComponent<GameplayLevelSceneController>();
            if (sceneController == null) sceneController = levelsRoot.AddComponent<GameplayLevelSceneController>();
            LevelDefinition levelOneDefinition = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelOneAssetPath);
            RagdollAttackManager2D[] sharedRoomAttacks = ConfigureSharedRoomPreview(sharedRoom, levelOneDefinition);
            ConfigureSceneController(sceneController, sharedRoom, sharedRoomAttacks,
                levelOne.gameObject, levelTwo.gameObject, ragdoll, ragdollInput, toolInput);

            levelOne.gameObject.SetActive(true);
            levelTwo.gameObject.SetActive(false);
            EditorUtility.SetDirty(levelsRoot);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static GameObject CloneLegacyRoom(Scene targetScene, Transform levelsRoot)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(LegacyLevelTwoScenePath) == null) return null;
            Scene sourceScene = EditorSceneManager.OpenScene(LegacyLevelTwoScenePath, OpenSceneMode.Additive);
            try
            {
                GameObject sourceRoom = FindRoot(sourceScene, "Room");
                if (sourceRoom == null) return null;
                GameObject room = UnityEngine.Object.Instantiate(sourceRoom);
                room.name = "Room";
                SceneManager.MoveGameObjectToScene(room, targetScene);
                room.transform.SetParent(levelsRoot, true);
                return room;
            }
            finally
            {
                EditorSceneManager.CloseScene(sourceScene, true);
            }
        }

        private static void CloneMissingLevelTwoTools(Scene targetScene, Transform levelTwo,
            ref GameObject tools)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(LegacyLevelTwoScenePath) == null) return;
            Scene sourceScene = EditorSceneManager.OpenScene(LegacyLevelTwoScenePath, OpenSceneMode.Additive);
            try
            {
                if (tools == null)
                {
                    GameObject sourceTools = FindRoot(sourceScene, ToolsRootName);
                    if (sourceTools != null)
                    {
                        tools = UnityEngine.Object.Instantiate(sourceTools);
                        tools.name = ToolsRootName;
                        SceneManager.MoveGameObjectToScene(tools, targetScene);
                        tools.transform.SetParent(levelTwo, true);
                    }
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(sourceScene, true);
            }
        }

        private static GameObject CreateFallbackTools(Scene scene, Transform parent)
        {
            GameObject lollipopPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LollipopPrefabPath);
            GameObject jellyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(JellyPrefabPath);
            if (lollipopPrefab == null || jellyPrefab == null)
                throw new InvalidOperationException("The Level 02 lollipop and jelly prefabs are required.");

            GameObject root = new GameObject(ToolsRootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.SetParent(parent, false);
            GameObject lollipopObject = PrefabUtility.InstantiatePrefab(lollipopPrefab, scene) as GameObject;
            GameObject jellyObject = PrefabUtility.InstantiatePrefab(jellyPrefab, scene) as GameObject;
            if (lollipopObject == null || jellyObject == null)
                throw new InvalidOperationException("Could not instantiate Level 02 tools.");
            lollipopObject.transform.SetParent(root.transform, true);
            jellyObject.transform.SetParent(root.transform, true);
            lollipopObject.transform.SetPositionAndRotation(new Vector3(-4.25f, -2.2f, 0f), Quaternion.Euler(0f, 0f, -12f));
            jellyObject.transform.SetPositionAndRotation(new Vector3(4.25f, -2.75f, 0f), Quaternion.identity);

            SandboxToolInput2D input = root.AddComponent<SandboxToolInput2D>();
            SerializedObject inputData = new SerializedObject(input);
            SerializedProperty tools = inputData.FindProperty("tools");
            tools.arraySize = 2;
            tools.GetArrayElementAtIndex(0).objectReferenceValue = lollipopObject.GetComponent<SandboxTool2D>();
            tools.GetArrayElementAtIndex(1).objectReferenceValue = jellyObject.GetComponent<SandboxTool2D>();
            inputData.FindProperty("toolLayers").intValue = 1 << lollipopObject.layer;
            inputData.ApplyModifiedPropertiesWithoutUndo();

            GameObject poolPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(JellyPoolPrefabPath);
            if (poolPrefab != null)
            {
                GameObject pool = PrefabUtility.InstantiatePrefab(poolPrefab, scene) as GameObject;
                if (pool != null) pool.transform.SetParent(root.transform, false);
            }
            return root;
        }

        private static void RebindLevelTwoReferences(SandboxToolInput2D input, GameObject toolsRoot,
            Camera camera, RagdollController ragdoll)
        {
            SerializedObject inputData = new SerializedObject(input);
            inputData.FindProperty("inputCamera").objectReferenceValue = camera;
            inputData.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(input);

            // Legacy CandyLab accumulated scene-instance overrides during testing. Normalize the two tools
            // so both are visible, active and reachable when Level 02 is selected.
            SandboxTool2D lollipop = FindTool(input, SandboxToolKind.Lollipop);
            SandboxTool2D jelly = FindTool(input, SandboxToolKind.Jelly);
            if (lollipop == null || jelly == null)
                throw new InvalidOperationException("Level 02 must contain both Lollipop and Jelly tools.");
            lollipop.gameObject.SetActive(true);
            jelly.gameObject.SetActive(true);
            lollipop.transform.SetPositionAndRotation(new Vector3(-4.25f, -2.2f, 0f), Quaternion.Euler(0f, 0f, -12f));
            jelly.transform.SetPositionAndRotation(new Vector3(4.25f, -2.75f, 0f), Quaternion.identity);
            lollipop.Body.velocity = Vector2.zero;
            lollipop.Body.angularVelocity = 0f;
            jelly.Body.velocity = Vector2.zero;
            jelly.Body.angularVelocity = 0f;

            RagdollAnimationController animation = ragdoll.GetComponent<RagdollAnimationController>();
            JellyContactVFXController[] liquidControllers = toolsRoot.GetComponentsInChildren<JellyContactVFXController>(true);
            for (int i = 0; i < liquidControllers.Length; i++)
            {
                SerializedObject liquid = new SerializedObject(liquidControllers[i]);
                SerializedProperty jellyTool = liquid.FindProperty("jellyTool");
                SerializedProperty ragdollReference = liquid.FindProperty("ragdoll");
                SerializedProperty animationReference = liquid.FindProperty("animationController");
                if (jellyTool != null) jellyTool.objectReferenceValue = jelly;
                if (ragdollReference != null) ragdollReference.objectReferenceValue = ragdoll;
                if (animationReference != null) animationReference.objectReferenceValue = animation;
                liquid.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(liquidControllers[i]);
            }
        }

        private static RagdollAttackManager2D[] ConfigureSharedRoomPreview(GameObject sharedRoom,
            LevelDefinition definition)
        {
            if (sharedRoom == null || definition == null)
                throw new InvalidOperationException("The shared Room preview cannot be configured without its level data.");
            Collider2D[] boundaries = sharedRoom.GetComponentsInChildren<Collider2D>(true);
            List<RagdollAttackManager2D> attacks = new List<RagdollAttackManager2D>(boundaries.Length);
            for (int i = 0; i < boundaries.Length; i++)
            {
                RagdollAttackManager2D attack = boundaries[i].GetComponent<RagdollAttackManager2D>();
                if (attack == null) attack = boundaries[i].gameObject.AddComponent<RagdollAttackManager2D>();
                attack.Configure(RagdollAttackType.Wall, definition.WallBaseDamage,
                    definition.WallDamagePerSpeed, definition.WallMinimumImpactSpeed,
                    definition.WallMaximumDamage);
                if (!attacks.Contains(attack)) attacks.Add(attack);
                EditorUtility.SetDirty(attack);
            }
            if (attacks.Count == 0)
                throw new InvalidOperationException("The shared Room has no damaging wall colliders.");
            return attacks.ToArray();
        }

        private static void ConfigureSceneController(GameplayLevelSceneController controller,
            GameObject sharedRoom, RagdollAttackManager2D[] sharedRoomAttacks,
            GameObject levelOne, GameObject levelTwo, RagdollController ragdoll,
            RagdollInputManager ragdollInput, SandboxToolInput2D toolInput)
        {
            SerializedObject data = new SerializedObject(controller);
            SerializedProperty levels = data.FindProperty("levels");
            Transform levelThree = FindDirectChild(controller.transform, LevelThreeRootName);
            CandyCannonController2D cannons =
                levelThree != null ? levelThree.GetComponent<CandyCannonController2D>() : null;
            levels.arraySize = levelThree != null && cannons != null ? 3 : 2;
            ConfigureEntry(levels.GetArrayElementAtIndex(0), "level_01", levelOne, ragdoll, ragdollInput, null, null);
            ConfigureEntry(levels.GetArrayElementAtIndex(1), "level_02", levelTwo, ragdoll, ragdollInput, toolInput, null);
            if (levels.arraySize == 3)
                ConfigureEntry(levels.GetArrayElementAtIndex(2), "level_03", levelThree.gameObject,
                    ragdoll, ragdollInput, null, cannons);
            data.FindProperty("sharedRoom").objectReferenceValue = sharedRoom;
            SerializedProperty roomAttacks = data.FindProperty("sharedRoomAttacks");
            roomAttacks.arraySize = sharedRoomAttacks.Length;
            for (int i = 0; i < sharedRoomAttacks.Length; i++)
                roomAttacks.GetArrayElementAtIndex(i).objectReferenceValue = sharedRoomAttacks[i];
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(controller);
        }

        private static void ConfigureEntry(SerializedProperty entry, string id, GameObject root,
            RagdollController ragdoll, RagdollInputManager ragdollInput, SandboxToolInput2D toolInput,
            CandyCannonController2D cannons)
        {
            entry.FindPropertyRelative("levelId").stringValue = id;
            entry.FindPropertyRelative("root").objectReferenceValue = root;
            entry.FindPropertyRelative("ragdoll").objectReferenceValue = ragdoll;
            entry.FindPropertyRelative("ragdollInput").objectReferenceValue = ragdollInput;
            entry.FindPropertyRelative("sandboxToolInput").objectReferenceValue = toolInput;
            entry.FindPropertyRelative("candyCannons").objectReferenceValue = cannons;
        }

        private static void ValidateEntry(SerializedProperty entry, string id, GameObject expectedRoot,
            bool requiresTools, bool requiresCannons)
        {
            if (entry.FindPropertyRelative("levelId").stringValue != id ||
                entry.FindPropertyRelative("root").objectReferenceValue != expectedRoot ||
                entry.FindPropertyRelative("ragdoll").objectReferenceValue == null ||
                entry.FindPropertyRelative("ragdollInput").objectReferenceValue == null ||
                (requiresTools && entry.FindPropertyRelative("sandboxToolInput").objectReferenceValue == null) ||
                (!requiresTools && entry.FindPropertyRelative("sandboxToolInput").objectReferenceValue != null) ||
                (requiresCannons && entry.FindPropertyRelative("candyCannons").objectReferenceValue == null) ||
                (!requiresCannons && entry.FindPropertyRelative("candyCannons").objectReferenceValue != null))
                throw new InvalidOperationException(id + " has incomplete explicit scene references.");
        }

        private static void ValidateRoom(Transform room, string label, float expectedSpeedDamage,
            float expectedMinimumSpeed, float expectedMaximumDamage)
        {
            Collider2D[] boundaries = room != null
                ? room.GetComponentsInChildren<Collider2D>(true)
                : Array.Empty<Collider2D>();
            if (boundaries.Length == 0) throw new InvalidOperationException(label + " has no Room boundaries.");
            for (int i = 0; i < boundaries.Length; i++)
            {
                RagdollAttackManager2D attack = boundaries[i].GetComponent<RagdollAttackManager2D>();
                if (attack == null || attack.AttackType != RagdollAttackType.Wall)
                    throw new InvalidOperationException(label + "/" + boundaries[i].name + " is missing its wall attack.");
                SerializedObject attackData = new SerializedObject(attack);
                if (!Mathf.Approximately(attackData.FindProperty("baseDamage").floatValue, 0f) ||
                    !Mathf.Approximately(attackData.FindProperty("damagePerSpeed").floatValue, expectedSpeedDamage) ||
                    !Mathf.Approximately(attackData.FindProperty("minimumImpactSpeed").floatValue, expectedMinimumSpeed) ||
                    !Mathf.Approximately(attackData.FindProperty("maximumDamage").floatValue, expectedMaximumDamage))
                    throw new InvalidOperationException(label + "/" + boundaries[i].name +
                                                        " lost its level-specific wall tuning.");
            }
        }

        private static void ValidateLevelTwoTools(Transform levelTwoRoot)
        {
            SandboxToolInput2D input = levelTwoRoot.GetComponentInChildren<SandboxToolInput2D>(true);
            if (input == null || input.Tools.Count != 2)
                throw new InvalidOperationException("Level 02 must explicitly reference two interactive tools.");
            SandboxTool2D lollipop = FindTool(input, SandboxToolKind.Lollipop);
            SandboxTool2D jelly = FindTool(input, SandboxToolKind.Jelly);
            if (lollipop == null || jelly == null || lollipop.Body == null || jelly.Body == null ||
                lollipop.Attack == null || jelly.Attack == null ||
                lollipop.GetComponent<Collider2D>() == null || jelly.GetComponent<Collider2D>() == null)
                throw new InvalidOperationException("Level 02 lollipop/jelly physics references are incomplete.");
            if (!lollipop.gameObject.activeSelf || !jelly.gameObject.activeSelf || !lollipop.Body.simulated ||
                !jelly.Body.simulated || !lollipop.GetComponent<Collider2D>().enabled ||
                !jelly.GetComponent<Collider2D>().enabled ||
                Vector2.Distance(lollipop.transform.position, new Vector2(-4.25f, -2.2f)) > .01f ||
                Vector2.Distance(jelly.transform.position, new Vector2(4.25f, -2.75f)) > .01f)
                throw new InvalidOperationException("Level 02 tools must be active, simulated, collidable and on-screen.");
            if (lollipop.Attack.CalculateDamage(8f) <= 0f || jelly.Attack.CalculateDamage(100f) != 0f)
                throw new InvalidOperationException("Lollipop must damage; Jelly must remain a zero-damage nuisance tool.");
        }

        private static SandboxTool2D FindTool(SandboxToolInput2D input, SandboxToolKind kind)
        {
            if (input == null) return null;
            for (int i = 0; i < input.Tools.Count; i++)
                if (input.Tools[i] != null && input.Tools[i].Kind == kind) return input.Tools[i];
            return null;
        }

        private static RagdollController FindAuthoredRagdoll(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            RagdollController fallback = null;
            for (int i = 0; i < roots.Length; i++)
            {
                RagdollController[] controllers = roots[i].GetComponentsInChildren<RagdollController>(true);
                for (int j = 0; j < controllers.Length; j++)
                {
                    RagdollController candidate = controllers[j];
                    if (candidate == null || candidate.gameObject.scene != scene) continue;
                    RagdollRigController2D rig = candidate.GetComponent<RagdollRigController2D>();
                    SerializedProperty authoredParts = rig != null
                        ? new SerializedObject(rig).FindProperty("authoredParts")
                        : null;
                    if (authoredParts == null || authoredParts.arraySize != 6) continue;
                    if (candidate.gameObject.activeInHierarchy) return candidate;
                    if (fallback == null) fallback = candidate;
                }
            }
            return fallback;
        }

        private static T FindSceneComponent<T>(Scene scene) where T : Component
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                T value = roots[i].GetComponentInChildren<T>(true);
                if (value != null) return value;
            }
            return null;
        }

        private static Transform EnsureDirectChild(Transform parent, string name)
        {
            Transform child = FindDirectChild(parent, name);
            if (child == null)
            {
                GameObject created = new GameObject(name);
                created.transform.SetParent(parent, false);
                child = created.transform;
            }
            child.localPosition = Vector3.zero;
            child.localRotation = Quaternion.identity;
            child.localScale = Vector3.one;
            return child;
        }

        private static Transform FindDirectChild(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == name) return child;
            }
            return null;
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                if (roots[i].name == name) return roots[i];
            return null;
        }

        private static int CountMissingScripts(Scene scene)
        {
            int count = 0;
            Stack<Transform> stack = new Stack<Transform>();
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++) stack.Push(roots[i].transform);
            while (stack.Count > 0)
            {
                Transform current = stack.Pop();
                count += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(current.gameObject);
                for (int i = 0; i < current.childCount; i++) stack.Push(current.GetChild(i));
            }
            return count;
        }
    }
}
#endif
