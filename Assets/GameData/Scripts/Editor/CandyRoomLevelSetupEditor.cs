#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using KickTheBuddy.Audio;
using KickTheBuddy.Gameplay;
using KickTheBuddy.Physics;
using KickTheBuddy.Physics.VFX;
using KickTheBuddy.VFX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KickTheBuddy.Editor
{
    /// <summary>Authors the additive "Level 2 New" candy-room variant without modifying old level content.</summary>
    public static class CandyRoomLevelSetupEditor
    {
        public const string LevelId = "level_02_new";
        public const string LevelRootName = "Level 02 New - Candy Playground";
        public const string ScenePath = "Assets/GameData/Scene/RagdollSandbox.unity";
        private const string LevelAssetPath = "Assets/GameData/Materials/Gameplay/Level_02_New.asset";
        private const string CatalogPath = "Assets/GameData/Materials/Gameplay/Level Catalog.asset";
        private const string ArtRoot = "Assets/GameData/Art/Level 02 New/";
        private const string CandyRoot = "Assets/GameData/Art/Candies/";
        private const string ExistingToolsRoot = "Assets/GameData/Art/Gameplay Tools/";
        private const string JellyPoolPath = "Assets/GameData/Prefabs/VFX/VFX_Jelly_ContactPool.prefab";
        private const string JarSpritePath = "Assets/GameData/Art/Themes/Bg/Glass characters/belly.png";

        [MenuItem("Tools/Game/Build Level 2 New Candy Playground")]
        public static void BuildFromMenu() => Build(false);

        public static void BuildBatch() => Build(true);

        [MenuItem("Tools/Game/Validate Level 2 New Candy Playground")]
        public static void ValidateFromMenu() => Validate(false);

        public static void ValidateBatch() => Validate(true);

        private static void Build(bool exitWhenDone)
        {
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                ConfigureSpriteImports();
                LevelDefinition definition = CreateOrUpdateDefinition();
                AppendCatalogEntry(definition);
                AuthorScene();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                ValidateOrThrow();
                Debug.Log("LEVEL02_NEW_CANDY_ROOM_BUILD_OK: old Level 02 preserved; automatic held candy gun, " +
                          "base-gripped beating sticks, per-prop attack values, zero-damage sticky jelly, " +
                          "localized damage/cracks, candy fill, and destruction completion are authored.");
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
            LevelDefinition definition = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelAssetPath);
            if (definition == null || definition.LevelId != LevelId ||
                definition.ScenePath != ScenePath ||
                definition.CompletionRule != LevelCompletionRule.CharacterDestroyed ||
                definition.TimeLimit < LevelDefinition.MinimumPlayTimeSeconds)
                throw new InvalidOperationException("Level 2 New definition is missing or invalid.");

            LevelCatalog catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(CatalogPath);
            if (catalog == null || catalog.IndexOf(LevelId) < 0 || catalog.Count < 4)
                throw new InvalidOperationException("Level Catalog does not preserve old levels and include Level 2 New.");

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameplayLevelSceneController levels = FindSceneComponent<GameplayLevelSceneController>(scene);
            Transform newRoot = levels != null ? FindDirectChild(levels.transform, LevelRootName) : null;
            Transform oldRoot = levels != null
                ? FindDirectChild(levels.transform, SingleSceneLevelsSetupEditor.LevelTwoRootName)
                : null;
            if (levels == null || newRoot == null || oldRoot == null || levels.LevelCount < 4)
                throw new InvalidOperationException("The old and new Level 2 roots must coexist under Levels.");

            SandboxToolInput2D input = newRoot.GetComponentInChildren<SandboxToolInput2D>(true);
            if (input == null || input.Tools.Count != 11)
                throw new InvalidOperationException("Level 2 New must have exactly eleven explicitly referenced tools.");

            HashSet<SandboxToolKind> required = new HashSet<SandboxToolKind>
            {
                SandboxToolKind.CandyGun,
                SandboxToolKind.CandyStick,
                SandboxToolKind.ChocolateBar,
                SandboxToolKind.GummyBear,
                SandboxToolKind.Jelly,
                SandboxToolKind.Lollipop,
                SandboxToolKind.LooseCandy,
                SandboxToolKind.CandyJar
            };
            int jellyCount = 0;
            for (int i = 0; i < input.Tools.Count; i++)
            {
                SandboxTool2D tool = input.Tools[i];
                if (tool == null || tool.Body == null || tool.Attack == null ||
                    tool.GetComponent<Collider2D>() == null)
                    throw new InvalidOperationException("A Level 2 New tool has incomplete authored references.");
                required.Remove(tool.Kind);
                bool shouldAutoThrow = tool.Kind == SandboxToolKind.ChocolateBar ||
                                       tool.Kind == SandboxToolKind.GummyBear ||
                                       tool.Kind == SandboxToolKind.LooseCandy ||
                                       tool.Kind == SandboxToolKind.CandyJar ||
                                       tool.Kind == SandboxToolKind.Jelly;
                if (shouldAutoThrow && (!tool.AutoThrowOnTap || tool.ThrowTarget == null))
                    throw new InvalidOperationException(
                        "A tappable Level 2 prop is missing its automatic ragdoll throw target.");
                if (tool.Kind != SandboxToolKind.Jelly) continue;
                jellyCount++;
                if (tool.Attack.CalculateDamage(100f) != 0f)
                    throw new InvalidOperationException("Jelly must remain presentation-only and deal zero damage.");
            }
            if (required.Count != 0 || jellyCount != 1)
                throw new InvalidOperationException("Level 2 New is missing one or more requested tool categories.");

            CandyGunController2D gun = newRoot.GetComponentInChildren<CandyGunController2D>(true);
            SerializedObject gunData = gun != null ? new SerializedObject(gun) : null;
            if (gunData == null || gunData.FindProperty("projectilePool").arraySize < 20 ||
                !gunData.FindProperty("automaticFireWhileGrabbed").boolValue ||
                !gunData.FindProperty("lockAimWhileGrabbed").boolValue ||
                gunData.FindProperty("aimTarget").objectReferenceValue == null)
                throw new InvalidOperationException(
                    "Candy gun requires ragdoll-aimed automatic held fire and a sustained-fire pool.");

            SandboxMeleeTool2D[] meleeTools = newRoot.GetComponentsInChildren<SandboxMeleeTool2D>(true);
            if (meleeTools.Length != 3)
                throw new InvalidOperationException("Candy stick and both lollipops require authored beating controllers.");
            for (int i = 0; i < meleeTools.Length; i++)
            {
                SerializedObject meleeData = new SerializedObject(meleeTools[i]);
                SandboxTool2D meleeTool = meleeData.FindProperty("tool").objectReferenceValue as SandboxTool2D;
                Transform grip = meleeTool != null
                    ? new SerializedObject(meleeTool).FindProperty("dragGrip").objectReferenceValue as Transform
                    : null;
                if (grip == null)
                    throw new InvalidOperationException("A melee tool is missing its bottom/base pickup grip.");
                if (meleeTools[i].AttackTarget == null)
                    throw new InvalidOperationException("A melee tool is missing its moving ragdoll target.");
                if (meleeData.FindProperty("windUpAngle").floatValue > 30.01f ||
                    meleeData.FindProperty("followThroughAngle").floatValue > 30.01f)
                    throw new InvalidOperationException("A melee tool exceeds the requested 30 degree beating arc.");
            }

            CandyJarBreakable2D jar = newRoot.GetComponentInChildren<CandyJarBreakable2D>(true);
            JellyContactVFXController jellyVfx = newRoot.GetComponentInChildren<JellyContactVFXController>(true);
            RagdollController ragdoll = FindAuthoredRagdoll(scene);
            RagdollVFXController ragdollVfx = ragdoll != null ? ragdoll.GetComponent<RagdollVFXController>() : null;
            CracksModifier[] cracks = ragdoll != null
                ? ragdoll.GetComponentsInChildren<CracksModifier>(true)
                : Array.Empty<CracksModifier>();
            RagdollCandyFill2D[] fills = ragdoll != null
                ? ragdoll.GetComponentsInChildren<RagdollCandyFill2D>(true)
                : Array.Empty<RagdollCandyFill2D>();
            if (jar == null || jellyVfx == null || ragdollVfx == null || cracks.Length < 6 || fills.Length < 2)
                throw new InvalidOperationException("Candy jar, jelly liquid, cracks, fill, or death VFX integration is incomplete.");

            GameplayAudioController audio = ragdoll != null ? ragdoll.GetComponent<GameplayAudioController>() : null;
            SerializedObject audioData = audio != null ? new SerializedObject(audio) : null;
            if (audioData == null || audioData.FindProperty("candyGuns").arraySize == 0)
                throw new InvalidOperationException("Gameplay audio is not connected to the new candy gun and tools.");

            if (CountMissingScripts(scene) != 0)
                throw new InvalidOperationException("The gameplay scene contains missing scripts.");
            Debug.Log("LEVEL02_NEW_CANDY_ROOM_VALIDATION_OK: old level retained, full tool set, aim-locked automatic pooled gun, " +
                      "large candies, ragdoll-targeted 30-degree base-gripped beating tools, tap auto-throw props, individual attack values, springy hit impulse, zero-damage jelly, cracks, face/audio hooks, " +
                      "candy fill and death burst are valid.");
        }

        private static void ConfigureSpriteImports()
        {
            string[] paths =
            {
                ArtRoot + "Baseball Bat.png",
                ArtRoot + "Candy Gun.png",
                ArtRoot + "Candy Stick.png",
                ArtRoot + "Chocolate Bar 1.png",
                ArtRoot + "Chocolate Bar 2.png",
                ArtRoot + "Gummy Bear 1.png",
                ArtRoot + "Gummy Bear 2.png",
                ArtRoot + "Wrapped Candy.png"
            };
            for (int i = 0; i < paths.Length; i++)
            {
                TextureImporter importer = AssetImporter.GetAtPath(paths[i]) as TextureImporter;
                if (importer == null) throw new InvalidOperationException("Missing Level 2 New art: " + paths[i]);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100f;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.SaveAndReimport();
            }
        }

        private static LevelDefinition CreateOrUpdateDefinition()
        {
            LevelDefinition definition = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelAssetPath);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<LevelDefinition>();
                AssetDatabase.CreateAsset(definition, LevelAssetPath);
            }

            SerializedObject data = new SerializedObject(definition);
            data.FindProperty("levelId").stringValue = LevelId;
            data.FindProperty("displayName").stringValue = "Level 2 New - Candy Playground";
            data.FindProperty("scenePath").stringValue = ScenePath;
            data.FindProperty("objectiveText").stringValue =
                "Pick up the candy gun for rapid fire, beat with sticks, and throw every candy toy.";
            data.FindProperty("completionRule").enumValueIndex = (int)LevelCompletionRule.CharacterDestroyed;
            data.FindProperty("targetDamage").floatValue = 500f;
            data.FindProperty("timeLimit").floatValue = 70f;
            data.FindProperty("completionCoins").intValue = 350;
            data.FindProperty("oneStarScore").intValue = 450;
            data.FindProperty("twoStarScore").intValue = 750;
            data.FindProperty("threeStarScore").intValue = 1100;
            data.FindProperty("wallBaseDamage").floatValue = 0f;
            data.FindProperty("wallDamagePerSpeed").floatValue = .7f;
            data.FindProperty("wallMinimumImpactSpeed").floatValue = 5f;
            data.FindProperty("wallMaximumDamage").floatValue = 8f;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static void AppendCatalogEntry(LevelDefinition definition)
        {
            LevelCatalog catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(CatalogPath);
            if (catalog == null) throw new InvalidOperationException("Level Catalog is missing.");
            SerializedObject data = new SerializedObject(catalog);
            SerializedProperty levels = data.FindProperty("levels");
            int existing = -1;
            for (int i = 0; i < levels.arraySize; i++)
                if (levels.GetArrayElementAtIndex(i).objectReferenceValue == definition)
                    existing = i;
            if (existing < 0)
            {
                int index = levels.arraySize;
                levels.arraySize++;
                levels.GetArrayElementAtIndex(index).objectReferenceValue = definition;
            }
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
        }

        private static void AuthorScene()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameplayLevelSceneController levels = FindSceneComponent<GameplayLevelSceneController>(scene);
            RagdollController ragdoll = FindAuthoredRagdoll(scene);
            Camera camera = FindSceneComponent<Camera>(scene);
            if (levels == null || ragdoll == null || camera == null)
                throw new InvalidOperationException("Gameplay scene is missing Levels, ragdoll, or camera.");

            Transform root = FindDirectChild(levels.transform, LevelRootName);
            if (root == null)
            {
                GameObject created = new GameObject(LevelRootName);
                created.transform.SetParent(levels.transform, false);
                root = created.transform;
            }
            ClearChildren(root);

            GameObject toolsRoot = new GameObject("Candy Playground Interactive Tools");
            toolsRoot.transform.SetParent(root, false);
            SandboxToolInput2D input = toolsRoot.AddComponent<SandboxToolInput2D>();
            List<SandboxTool2D> tools = new List<SandboxTool2D>(11);

            SandboxTool2D gunTool = CreateTool(toolsRoot.transform, "Candy Gun", ArtRoot + "Candy Gun.png",
                SandboxToolKind.CandyGun, RagdollAttackType.Custom, new Vector2(-4.35f, -2.45f), 0f, 1f,
                .2f, .3f, 5f, 1250f);
            tools.Add(gunTool);
            ConfigureGrip(gunTool, new Vector2(-.28f, -.42f));
            Transform meleeTarget = ResolveMeleeTarget(ragdoll);
            ConfigureRagdollHitReaction(ragdoll);
            CandyGunController2D gun = ConfigureGun(root, gunTool, meleeTarget);
            SandboxTool2D candyStick = CreateTool(toolsRoot.transform, "Candy Stick", ArtRoot + "Candy Stick.png",
                SandboxToolKind.CandyStick, RagdollAttackType.CandyStick, new Vector2(-3.25f, -3.15f), 8f, .9f,
                3f, 2.2f, 30f, 1900f);
            ConfigureMelee(candyStick, meleeTarget, new Vector2(-.84f, 0f), Vector2.right, 2.5f, 105f);
            tools.Add(candyStick);
            tools.Add(CreateTool(toolsRoot.transform, "Chocolate Bar A", ArtRoot + "Chocolate Bar 1.png",
                SandboxToolKind.ChocolateBar, RagdollAttackType.ChocolateBar, new Vector2(2.45f, -3.05f), -12f, .72f,
                1.5f, .9f, 16f, 1700f));
            tools.Add(CreateTool(toolsRoot.transform, "Chocolate Bar B", ArtRoot + "Chocolate Bar 2.png",
                SandboxToolKind.ChocolateBar, RagdollAttackType.ChocolateBar, new Vector2(3.1f, -2.9f), 20f, .68f,
                1.8f, 1.05f, 18f, 1700f));
            tools.Add(CreateTool(toolsRoot.transform, "Gummy Bear Purple", ArtRoot + "Gummy Bear 1.png",
                SandboxToolKind.GummyBear, RagdollAttackType.GummyBear, new Vector2(-4.95f, -3.15f), -8f, .62f,
                .65f, .38f, 7f, 900f));
            tools.Add(CreateTool(toolsRoot.transform, "Gummy Bear Orange", ArtRoot + "Gummy Bear 2.png",
                SandboxToolKind.GummyBear, RagdollAttackType.GummyBear, new Vector2(4.95f, -3.1f), 8f, .62f,
                .8f, .44f, 8f, 900f));

            SandboxTool2D jelly = CreateTool(toolsRoot.transform, "Jelly Splash",
                ExistingToolsRoot + "Jelly.png", SandboxToolKind.Jelly, RagdollAttackType.Jelly,
                new Vector2(-2.1f, -3.15f), 0f, .9f, 0f, 0f, 0f, 900f, true);
            tools.Add(jelly);
            SandboxTool2D greenLollipop = CreateTool(toolsRoot.transform, "Lollipop Green",
                ExistingToolsRoot + "Lollipop.png", SandboxToolKind.Lollipop, RagdollAttackType.Lollipop,
                new Vector2(4.15f, -2.6f), 18f, .75f, 4f, 2.8f, 35f, 1950f);
            ConfigureMelee(greenLollipop, meleeTarget, new Vector2(0f, -1.04f), Vector2.up, 2.15f, 118f);
            tools.Add(greenLollipop);
            SandboxTool2D pinkLollipop = CreateTool(toolsRoot.transform, "Lollipop Pink",
                "Assets/GameData/Art/Level 03/lillipop 2.png", SandboxToolKind.Lollipop,
                RagdollAttackType.Lollipop, new Vector2(4.65f, -2.45f), -14f, .48f,
                3.5f, 2.5f, 32f, 1900f);
            ConfigureMelee(pinkLollipop, meleeTarget, new Vector2(0f, -.74f), Vector2.up, 2.25f, 112f);
            tools.Add(pinkLollipop);
            tools.Add(CreateTool(toolsRoot.transform, "Wrapped Loose Candy", ArtRoot + "Wrapped Candy.png",
                SandboxToolKind.LooseCandy, RagdollAttackType.Custom, new Vector2(1.55f, -3.2f), -6f, .62f,
                .45f, .32f, 6f, 850f));

            SandboxTool2D jarTool = CreateTool(toolsRoot.transform, "Interactive Candy Jar", JarSpritePath,
                SandboxToolKind.CandyJar, RagdollAttackType.CandyJar, new Vector2(-5.05f, -1.7f), 0f, .58f,
                .3f, .38f, 6f, 1800f);
            tools.Add(jarTool);
            ConfigureJar(root, jarTool);

            for (int i = 0; i < tools.Count; i++)
            {
                SandboxTool2D throwable = tools[i];
                float impulse = ResolveTapThrowImpulse(throwable.Kind);
                if (impulse > 0f) throwable.ConfigureAutoThrow(meleeTarget, impulse);
            }

            SerializedObject inputData = new SerializedObject(input);
            inputData.FindProperty("inputCamera").objectReferenceValue = camera;
            SerializedProperty toolArray = inputData.FindProperty("tools");
            toolArray.arraySize = tools.Count;
            for (int i = 0; i < tools.Count; i++)
                toolArray.GetArrayElementAtIndex(i).objectReferenceValue = tools[i];
            inputData.FindProperty("toolLayers").intValue = ~0;
            inputData.ApplyModifiedPropertiesWithoutUndo();

            ConfigureJellyVfx(root, jelly, ragdoll);
            ConfigureLevelEntry(levels, root.gameObject, ragdoll, input);
            ConfigureGameplayAudio(ragdoll, tools, gun);

            root.gameObject.SetActive(false);
            EditorUtility.SetDirty(levels);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static SandboxTool2D CreateTool(
            Transform parent,
            string name,
            string spritePath,
            SandboxToolKind kind,
            RagdollAttackType attackType,
            Vector2 position,
            float rotation,
            float scale,
            float baseDamage,
            float damagePerSpeed,
            float maximumDamage,
            float maximumDragForce,
            bool sticky = false)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            if (sprite == null) throw new InvalidOperationException("Missing tool sprite: " + spritePath);
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, rotation));
            go.transform.localScale = Vector3.one * scale;

            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 85;
            Rigidbody2D body = go.AddComponent<Rigidbody2D>();
            body.mass = kind == SandboxToolKind.GummyBear || kind == SandboxToolKind.Jelly ? .45f : 1.25f;
            body.gravityScale = 1.45f;
            body.drag = .22f;
            body.angularDrag = .65f;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            BoxCollider2D collider = go.AddComponent<BoxCollider2D>();
            Vector2 spriteSize = sprite.bounds.size;
            collider.size = new Vector2(Mathf.Max(.15f, spriteSize.x * .82f), Mathf.Max(.15f, spriteSize.y * .78f));
            TargetJoint2D dragJoint = go.AddComponent<TargetJoint2D>();
            dragJoint.autoConfigureTarget = false;
            dragJoint.enabled = false;
            FixedJoint2D stickyJoint = sticky ? go.AddComponent<FixedJoint2D>() : null;
            if (stickyJoint != null)
            {
                stickyJoint.autoConfigureConnectedAnchor = false;
                stickyJoint.enableCollision = false;
                stickyJoint.enabled = false;
            }

            RagdollAttackManager2D attack = go.AddComponent<RagdollAttackManager2D>();
            attack.Configure(attackType, baseDamage, damagePerSpeed, attackType == RagdollAttackType.Jelly ? 0f : 3.8f,
                maximumDamage);
            SandboxTool2D tool = go.AddComponent<SandboxTool2D>();
            SerializedObject data = new SerializedObject(tool);
            data.FindProperty("kind").enumValueIndex = (int)kind;
            data.FindProperty("body").objectReferenceValue = body;
            data.FindProperty("dragJoint").objectReferenceValue = dragJoint;
            data.FindProperty("stickyJoint").objectReferenceValue = stickyJoint;
            data.FindProperty("attack").objectReferenceValue = attack;
            data.FindProperty("visual").objectReferenceValue = go.transform;
            data.FindProperty("dragFrequency").floatValue = 8.5f;
            data.FindProperty("dragDamping").floatValue = .94f;
            data.FindProperty("dragMaximumForce").floatValue = maximumDragForce;
            data.FindProperty("targetSmoothTime").floatValue = .022f;
            data.FindProperty("maximumTargetSpeed").floatValue = 75f;
            data.ApplyModifiedPropertiesWithoutUndo();
            return tool;
        }

        private static Transform ConfigureGrip(SandboxTool2D tool, Vector2 localPosition)
        {
            GameObject gripObject = new GameObject("Base Grip");
            gripObject.transform.SetParent(tool.transform, false);
            gripObject.transform.localPosition = localPosition;
            SerializedObject toolData = new SerializedObject(tool);
            toolData.FindProperty("dragGrip").objectReferenceValue = gripObject.transform;
            toolData.ApplyModifiedPropertiesWithoutUndo();
            return gripObject.transform;
        }

        private static SandboxMeleeTool2D ConfigureMelee(
            SandboxTool2D tool,
            Transform target,
            Vector2 gripPosition,
            Vector2 tipAxis,
            float strikesPerSecond,
            float maximumTorque)
        {
            ConfigureGrip(tool, gripPosition);
            SandboxMeleeTool2D melee = tool.gameObject.AddComponent<SandboxMeleeTool2D>();
            SerializedObject data = new SerializedObject(melee);
            data.FindProperty("tool").objectReferenceValue = tool;
            data.FindProperty("body").objectReferenceValue = tool.Body;
            data.FindProperty("attackTarget").objectReferenceValue = target;
            data.FindProperty("localTipAxis").vector2Value = tipAxis;
            data.FindProperty("strikesPerSecond").floatValue = strikesPerSecond;
            data.FindProperty("windUpAngle").floatValue = 30f;
            data.FindProperty("followThroughAngle").floatValue = 30f;
            data.FindProperty("strikePhase").floatValue = .34f;
            data.FindProperty("rotationDrive").floatValue = 26f;
            data.FindProperty("rotationDamping").floatValue = 2.2f;
            data.FindProperty("maximumTorque").floatValue = Mathf.Max(220f, maximumTorque);
            data.FindProperty("maximumAngularSpeed").floatValue = 720f;
            data.ApplyModifiedPropertiesWithoutUndo();
            return melee;
        }

        private static Transform ResolveMeleeTarget(RagdollController ragdoll)
        {
            for (int i = 0; i < ragdoll.Parts.Count; i++)
            {
                RagdollController.RagdollPart part = ragdoll.Parts[i];
                if (part != null && part.PartType == RagdollPartType.Torso && part.Body != null)
                    return part.Body.transform;
            }
            return ragdoll.transform;
        }

        private static CandyGunController2D ConfigureGun(
            Transform levelRoot,
            SandboxTool2D gunTool,
            Transform aimTarget)
        {
            GameObject muzzleObject = new GameObject("Muzzle");
            muzzleObject.transform.SetParent(gunTool.transform, false);
            muzzleObject.transform.localPosition = new Vector3(.68f, .08f, 0f);

            Transform poolRoot = new GameObject("Candy Gun Projectile Pool").transform;
            poolRoot.SetParent(levelRoot, false);
            CandyGunProjectile2D[] projectiles = new CandyGunProjectile2D[24];
            for (int i = 0; i < projectiles.Length; i++)
            {
                Sprite sprite = LoadCandySprite(i);
                GameObject go = new GameObject("Candy Projectile " + (i + 1));
                go.transform.SetParent(poolRoot, false);
                SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sortingOrder = 92;
                go.transform.localScale = Vector3.one * .68f;
                Rigidbody2D body = go.AddComponent<Rigidbody2D>();
                body.mass = .2f;
                body.gravityScale = .55f;
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                body.interpolation = RigidbodyInterpolation2D.Interpolate;
                CircleCollider2D collider = go.AddComponent<CircleCollider2D>();
                collider.radius = Mathf.Max(.12f, Mathf.Min(sprite.bounds.extents.x, sprite.bounds.extents.y) * .72f);
                RagdollAttackManager2D attack = go.AddComponent<RagdollAttackManager2D>();
                attack.Configure(RagdollAttackType.CandyProjectile, 8f, 1.6f, 2f, 24f);
                CandyGunProjectile2D projectile = go.AddComponent<CandyGunProjectile2D>();
                SerializedObject projectileData = new SerializedObject(projectile);
                projectileData.FindProperty("body").objectReferenceValue = body;
                projectileData.FindProperty("projectileCollider").objectReferenceValue = collider;
                projectileData.FindProperty("attack").objectReferenceValue = attack;
                projectileData.FindProperty("lifetime").floatValue = 3f;
                projectileData.FindProperty("limbHitImpulse").floatValue = 24f;
                projectileData.FindProperty("bodyPushImpulse").floatValue = 14f;
                projectileData.FindProperty("upwardLift").floatValue = .22f;
                projectileData.ApplyModifiedPropertiesWithoutUndo();
                body.simulated = false;
                collider.enabled = false;
                go.SetActive(false);
                projectiles[i] = projectile;
            }

            CandyGunController2D gun = gunTool.gameObject.AddComponent<CandyGunController2D>();
            SerializedObject gunData = new SerializedObject(gun);
            gunData.FindProperty("gunTool").objectReferenceValue = gunTool;
            gunData.FindProperty("muzzle").objectReferenceValue = muzzleObject.transform;
            gunData.FindProperty("aimTarget").objectReferenceValue = aimTarget;
            SerializedProperty pool = gunData.FindProperty("projectilePool");
            pool.arraySize = projectiles.Length;
            for (int i = 0; i < projectiles.Length; i++)
                pool.GetArrayElementAtIndex(i).objectReferenceValue = projectiles[i];
            gunData.FindProperty("launchSpeed").floatValue = 18f;
            gunData.FindProperty("fireCooldown").floatValue = .14f;
            gunData.FindProperty("recoilImpulse").floatValue = .5f;
            gunData.FindProperty("automaticFireWhileGrabbed").boolValue = true;
            gunData.FindProperty("tapToFire").boolValue = true;
            gunData.FindProperty("lockAimWhileGrabbed").boolValue = true;
            gunData.FindProperty("aimOffsetDegrees").floatValue = 0f;
            gunData.ApplyModifiedPropertiesWithoutUndo();
            return gun;
        }

        private static float ResolveTapThrowImpulse(SandboxToolKind kind)
        {
            switch (kind)
            {
                case SandboxToolKind.ChocolateBar: return 11f;
                case SandboxToolKind.GummyBear: return 8f;
                case SandboxToolKind.LooseCandy: return 8.5f;
                case SandboxToolKind.CandyJar: return 12f;
                case SandboxToolKind.Jelly: return 7f;
                default: return 0f;
            }
        }

        private static void ConfigureRagdollHitReaction(RagdollController ragdoll)
        {
            RagdollDamageManager damage = ragdoll.GetComponent<RagdollDamageManager>();
            if (damage == null) return;
            SerializedObject data = new SerializedObject(damage);
            data.FindProperty("hitImpulsePerSpeed").floatValue = .32f;
            data.FindProperty("minimumHitImpulse").floatValue = .5f;
            data.FindProperty("maximumHitImpulse").floatValue = 11f;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureJar(Transform levelRoot, SandboxTool2D jarTool)
        {
            Transform contentsRoot = new GameObject("Jar Candy Contents").transform;
            contentsRoot.SetParent(levelRoot, false);
            Rigidbody2D[] candies = new Rigidbody2D[6];
            for (int i = 0; i < candies.Length; i++)
            {
                Sprite sprite = LoadCandySprite(i + 8);
                GameObject go = new GameObject("Jar Candy " + (i + 1));
                go.transform.SetParent(contentsRoot, false);
                SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sortingOrder = 91;
                go.transform.localScale = Vector3.one * .36f;
                Rigidbody2D body = go.AddComponent<Rigidbody2D>();
                body.mass = .09f;
                body.gravityScale = 1.25f;
                body.drag = .08f;
                body.angularDrag = .12f;
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                CircleCollider2D collider = go.AddComponent<CircleCollider2D>();
                collider.radius = .16f;
                body.simulated = false;
                go.SetActive(false);
                candies[i] = body;
            }

            CandyJarBreakable2D jar = jarTool.gameObject.AddComponent<CandyJarBreakable2D>();
            SerializedObject data = new SerializedObject(jar);
            data.FindProperty("jarBody").objectReferenceValue = jarTool.Body;
            data.FindProperty("jarCollider").objectReferenceValue = jarTool.GetComponent<Collider2D>();
            data.FindProperty("jarRenderer").objectReferenceValue = jarTool.GetComponent<SpriteRenderer>();
            SerializedProperty contained = data.FindProperty("containedCandy");
            contained.arraySize = candies.Length;
            for (int i = 0; i < candies.Length; i++)
                contained.GetArrayElementAtIndex(i).objectReferenceValue = candies[i];
            data.FindProperty("breakImpactSpeed").floatValue = 8.5f;
            data.FindProperty("burstImpulse").floatValue = 3.1f;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureJellyVfx(Transform root, SandboxTool2D jelly, RagdollController ragdoll)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(JellyPoolPath);
            if (prefab == null) throw new InvalidOperationException("Jelly contact VFX pool prefab is missing.");
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab, root.gameObject.scene) as GameObject;
            if (instance == null) throw new InvalidOperationException("Could not instantiate Jelly VFX pool.");
            instance.name = "Jelly Liquid Contact VFX";
            instance.transform.SetParent(root, false);
            JellyContactVFXController controller = instance.GetComponent<JellyContactVFXController>();
            if (controller == null) controller = instance.AddComponent<JellyContactVFXController>();
            SerializedObject data = new SerializedObject(controller);
            data.FindProperty("jellyTool").objectReferenceValue = jelly;
            data.FindProperty("ragdoll").objectReferenceValue = ragdoll;
            data.FindProperty("animationController").objectReferenceValue =
                ragdoll.GetComponent<RagdollAnimationController>();
            data.FindProperty("minimumImpactSpeed").floatValue = 1.5f;
            data.FindProperty("effectDuration").floatValue = 1.75f;
            data.FindProperty("slideDistance").floatValue = .58f;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureLevelEntry(
            GameplayLevelSceneController levels,
            GameObject root,
            RagdollController ragdoll,
            SandboxToolInput2D input)
        {
            SerializedObject data = new SerializedObject(levels);
            SerializedProperty entries = data.FindProperty("levels");
            int index = -1;
            for (int i = 0; i < entries.arraySize; i++)
                if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("levelId").stringValue == LevelId)
                    index = i;
            if (index < 0)
            {
                index = entries.arraySize;
                entries.arraySize++;
            }
            SerializedProperty entry = entries.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("levelId").stringValue = LevelId;
            entry.FindPropertyRelative("root").objectReferenceValue = root;
            entry.FindPropertyRelative("ragdoll").objectReferenceValue = ragdoll;
            entry.FindPropertyRelative("ragdollInput").objectReferenceValue =
                ragdoll.GetComponent<RagdollInputManager>();
            entry.FindPropertyRelative("sandboxToolInput").objectReferenceValue = input;
            entry.FindPropertyRelative("candyCannons").objectReferenceValue = null;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureGameplayAudio(
            RagdollController ragdoll,
            List<SandboxTool2D> newTools,
            CandyGunController2D gun)
        {
            GameplayAudioController audio = ragdoll.GetComponent<GameplayAudioController>();
            if (audio == null) throw new InvalidOperationException("Shared ragdoll is missing GameplayAudioController.");
            SerializedObject data = new SerializedObject(audio);
            SerializedProperty tools = data.FindProperty("tools");
            List<SandboxTool2D> combined = new List<SandboxTool2D>(tools.arraySize + newTools.Count);
            for (int i = 0; i < tools.arraySize; i++)
            {
                SandboxTool2D existing = tools.GetArrayElementAtIndex(i).objectReferenceValue as SandboxTool2D;
                if (existing != null && !combined.Contains(existing)) combined.Add(existing);
            }
            for (int i = 0; i < newTools.Count; i++)
                if (newTools[i] != null && !combined.Contains(newTools[i])) combined.Add(newTools[i]);
            tools.arraySize = combined.Count;
            for (int i = 0; i < combined.Count; i++)
                tools.GetArrayElementAtIndex(i).objectReferenceValue = combined[i];

            SerializedProperty guns = data.FindProperty("candyGuns");
            guns.arraySize = 1;
            guns.GetArrayElementAtIndex(0).objectReferenceValue = gun;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(audio);
        }

        private static Sprite LoadCandySprite(int index)
        {
            int candyNumber = index % 24 + 1;
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(CandyRoot + "Candy " + candyNumber + ".png");
            if (sprite == null) throw new InvalidOperationException("Missing candy sprite " + candyNumber + ".");
            return sprite;
        }

        private static void ClearChildren(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(root.GetChild(i).gameObject);
        }

        private static Transform FindDirectChild(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
                if (parent.GetChild(i).name == name) return parent.GetChild(i);
            return null;
        }

        private static T FindSceneComponent<T>(Scene scene) where T : Component
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                T[] candidates = roots[i].GetComponentsInChildren<T>(true);
                for (int j = 0; j < candidates.Length; j++)
                    if (candidates[j] != null && candidates[j].gameObject.scene == scene)
                        return candidates[j];
            }
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
                    SerializedProperty parts = rig != null
                        ? new SerializedObject(rig).FindProperty("authoredParts")
                        : null;
                    if (parts == null || parts.arraySize != 6) continue;
                    if (candidate.gameObject.activeInHierarchy) return candidate;
                    if (fallback == null) fallback = candidate;
                }
            }
            return fallback;
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
