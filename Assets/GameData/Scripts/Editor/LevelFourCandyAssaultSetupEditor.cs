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
    public static class LevelFourCandyAssaultSetupEditor
    {
        public const string LevelId = "level_04";
        public const string LevelRootName = "Level 04 - Pipe Assault";
        private const string ScenePath = "Assets/GameData/Scene/RagdollSandbox.unity";
        private const string LevelAssetPath = "Assets/GameData/Materials/Gameplay/Level_04.asset";
        private const string CatalogPath = "Assets/GameData/Materials/Gameplay/Level Catalog.asset";
        private const string ArtRoot = "Assets/GameData/Art/Level 04/";
        private const int PoolSize = 12;
        private const int ImpactPoolSize = 5;
        private const string ParticleMaterialPath = "Assets/GameData/Materials/VFX/MAT_Level04_PipeVisible.mat";

        [MenuItem("Tools/Game/Build Level 4 Pipe Assault")]
        public static void BuildFromMenu() => Build();

        [MenuItem("Tools/Game/Validate Level 4 Pipe Assault")]
        public static void ValidateFromMenu() => ValidateOrThrow();

        private static void Build()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureSpriteImports();
            LevelDefinition definition = CreateDefinition();
            AppendCatalog(definition);
            AuthorScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ValidateOrThrow();
            Debug.Log("LEVEL_04_PIPE_ASSAULT_BUILD_OK: reusable tools and candy gun plus button-controlled bomb/soda pipe pools are authored.");
        }

        private static void ConfigureSpriteImports()
        {
            string[] paths =
            {
                ArtRoot + "Chocolate Bomb.png", ArtRoot + "Soda Can.png", ArtRoot + "Hammer.png",
                ArtRoot + "Left Pipe.png", ArtRoot + "Right Pipe.png", ArtRoot + "Bomb Button.png",
                ArtRoot + "Soda Button.png", ArtRoot + "Buttons Board.png", ArtRoot + "Buttons Glass.png"
            };
            for (int i = 0; i < paths.Length; i++)
            {
                TextureImporter importer = AssetImporter.GetAtPath(paths[i]) as TextureImporter;
                if (importer == null) throw new InvalidOperationException("Missing Level 4 art: " + paths[i]);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.SaveAndReimport();
            }
        }

        private static LevelDefinition CreateDefinition()
        {
            LevelDefinition definition = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelAssetPath);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<LevelDefinition>();
                AssetDatabase.CreateAsset(definition, LevelAssetPath);
            }
            SerializedObject data = new SerializedObject(definition);
            data.FindProperty("levelId").stringValue = LevelId;
            data.FindProperty("displayName").stringValue = "Level 4 - Pipe Assault";
            data.FindProperty("scenePath").stringValue = ScenePath;
            data.FindProperty("objectiveText").stringValue = "Use the bomb pipe, soda pipe, candy cannon and tools to destroy the buddy.";
            data.FindProperty("completionRule").enumValueIndex = (int)LevelCompletionRule.CharacterDestroyed;
            data.FindProperty("targetDamage").floatValue = 500f;
            data.FindProperty("timeLimit").floatValue = LevelDefinition.MinimumPlayTimeSeconds;
            data.FindProperty("completionCoins").intValue = 650;
            data.FindProperty("oneStarScore").intValue = 450;
            data.FindProperty("twoStarScore").intValue = 800;
            data.FindProperty("threeStarScore").intValue = 1200;
            data.FindProperty("wallBaseDamage").floatValue = 120f;
            data.FindProperty("wallDamagePerSpeed").floatValue = 0f;
            data.FindProperty("wallMinimumImpactSpeed").floatValue = 0f;
            data.FindProperty("wallMaximumDamage").floatValue = 120f;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static void AppendCatalog(LevelDefinition definition)
        {
            LevelCatalog catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(CatalogPath);
            if (catalog == null) throw new InvalidOperationException("Level Catalog is missing.");
            SerializedObject data = new SerializedObject(catalog);
            SerializedProperty levels = data.FindProperty("levels");
            for (int i = 0; i < levels.arraySize; i++)
                if (levels.GetArrayElementAtIndex(i).objectReferenceValue == definition) return;
            int index = levels.arraySize;
            levels.arraySize++;
            levels.GetArrayElementAtIndex(index).objectReferenceValue = definition;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
        }

        private static void AuthorScene()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameplayLevelSceneController levels = FindSceneComponent<GameplayLevelSceneController>(scene);
            RagdollController ragdoll = FindSceneComponent<RagdollController>(scene, true);
            Camera camera = FindSceneComponent<Camera>(scene);
            if (levels == null || ragdoll == null || camera == null)
                throw new InvalidOperationException("Gameplay scene core references are missing.");

            Transform oldRoot = levels.transform.Find(LevelRootName);
            if (oldRoot != null) UnityEngine.Object.DestroyImmediate(oldRoot.gameObject);
            Transform source = levels.transform.Find(CandyRoomLevelSetupEditor.LevelRootName);
            if (source == null) throw new InvalidOperationException("Build Level 2 New before Level 4 so its reusable tools can be cloned.");

            GameObject root = UnityEngine.Object.Instantiate(source.gameObject, levels.transform);
            root.name = LevelRootName;
            root.SetActive(true);
            Material visibleVfxMaterial = EnsureVisibleVfxMaterial();
            LevelFourPipeController2D pipes = root.AddComponent<LevelFourPipeController2D>();

            GameObject pipeRoot = new GameObject("Button Controlled Attack Pipes");
            pipeRoot.transform.SetParent(root.transform, false);
            SpriteRenderer leftPipe = CreateSprite(pipeRoot.transform, "Chocolate Bomb Pipe", ArtRoot + "Left Pipe.png",
                new Vector3(-5.55f, 1.75f, 0f), Vector3.one * .72f, 120);
            SpriteRenderer rightPipe = CreateSprite(pipeRoot.transform, "Soda Can Pipe", ArtRoot + "Right Pipe.png",
                new Vector3(5.55f, 1.75f, 0f), Vector3.one * .72f, 120);
            Transform leftMuzzle = CreatePoint(leftPipe.transform, "Bomb Muzzle", new Vector3(.72f, -.05f, 0f));
            Transform rightMuzzle = CreatePoint(rightPipe.transform, "Soda Muzzle", new Vector3(-.72f, -.05f, 0f));

            Transform controls = new GameObject("Pipe Controls").transform;
            controls.SetParent(root.transform, false);
            CreateSprite(controls, "Buttons Board", ArtRoot + "Buttons Board.png",
                new Vector3(-5.25f, -.35f, 0f), Vector3.one * .7f, 118);
            SpriteRenderer bombButton = CreateSprite(controls, "Chocolate Bomb Button", ArtRoot + "Bomb Button.png",
                new Vector3(-5.48f, -.35f, -.01f), Vector3.one * .72f, 121);
            SpriteRenderer sodaButton = CreateSprite(controls, "Soda Can Button", ArtRoot + "Soda Button.png",
                new Vector3(-5.03f, -.35f, -.01f), Vector3.one * .72f, 121);
            CircleCollider2D bombButtonCollider = bombButton.gameObject.AddComponent<CircleCollider2D>();
            CircleCollider2D sodaButtonCollider = sodaButton.gameObject.AddComponent<CircleCollider2D>();
            CreateSprite(controls, "Buttons Glass", ArtRoot + "Buttons Glass.png",
                new Vector3(-5.25f, -.35f, -.02f), Vector3.one * .7f, 122);

            LevelFourPipeProjectile2D[] bombPool = CreatePool(root.transform, "Chocolate Bomb Pool",
                ArtRoot + "Chocolate Bomb.png", .42f, 1.1f, visibleVfxMaterial);
            LevelFourPipeProjectile2D[] sodaPool = CreatePool(root.transform, "Soda Can Pool",
                ArtRoot + "Soda Can.png", .5f, .65f, visibleVfxMaterial);
            LevelFourPipeVFXController2D pipeVfx = CreatePipeVfx(root.transform, leftMuzzle, rightMuzzle,
                visibleVfxMaterial);
            ConfigurePipeController(pipes, camera, ragdoll, bombButtonCollider, bombButton,
                leftMuzzle, bombPool, sodaButtonCollider, sodaButton, rightMuzzle, sodaPool, pipeVfx);

            AddHammer(root, ragdoll);
            BoostLevelAttacks(root);
            AppendLevelContent(levels, root, ragdoll, pipes);
            root.SetActive(false);
            EditorUtility.SetDirty(levels);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void AddHammer(GameObject root, RagdollController ragdoll)
        {
            SandboxToolInput2D input = root.GetComponentInChildren<SandboxToolInput2D>(true);
            if (input == null) return;
            Transform parent = input.transform;
            SpriteRenderer renderer = CreateSprite(parent, "Candy Hammer", ArtRoot + "Hammer.png",
                new Vector3(3.55f, -2.65f, 0f), Vector3.one * .55f, 110);
            Rigidbody2D body = renderer.gameObject.AddComponent<Rigidbody2D>();
            body.mass = 1.6f; body.gravityScale = 1f; body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            BoxCollider2D collider = renderer.gameObject.AddComponent<BoxCollider2D>();
            TargetJoint2D joint = renderer.gameObject.AddComponent<TargetJoint2D>(); joint.enabled = false;
            RagdollAttackManager2D attack = renderer.gameObject.AddComponent<RagdollAttackManager2D>();
            attack.Configure(RagdollAttackType.Hammer, 32f, 5f, .5f, 95f);
            SandboxTool2D tool = renderer.gameObject.AddComponent<SandboxTool2D>();
            SerializedObject toolData = new SerializedObject(tool);
            toolData.FindProperty("kind").enumValueIndex = (int)SandboxToolKind.CandyStick;
            toolData.FindProperty("body").objectReferenceValue = body;
            toolData.FindProperty("dragJoint").objectReferenceValue = joint;
            toolData.FindProperty("attack").objectReferenceValue = attack;
            toolData.FindProperty("visual").objectReferenceValue = renderer.transform;
            toolData.ApplyModifiedPropertiesWithoutUndo();

            Transform target = ResolveHead(ragdoll);
            tool.ConfigureAutoThrow(target, 28f, .12f, 0f);
            SerializedObject inputData = new SerializedObject(input);
            SerializedProperty tools = inputData.FindProperty("tools");
            int index = tools.arraySize;
            tools.arraySize++;
            tools.GetArrayElementAtIndex(index).objectReferenceValue = tool;
            inputData.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BoostLevelAttacks(GameObject root)
        {
            SandboxTool2D[] tools = root.GetComponentsInChildren<SandboxTool2D>(true);
            for (int i = 0; i < tools.Length; i++)
            {
                if (tools[i] == null || tools[i].Attack == null ||
                    tools[i].Attack.AttackType == RagdollAttackType.Jelly) continue;
                SerializedObject toolData = new SerializedObject(tools[i]);
                toolData.FindProperty("ballisticBaseDamage").floatValue = 120f;
                toolData.FindProperty("ballisticDamagePerSpeed").floatValue = 0f;
                toolData.FindProperty("ballisticMaximumDamage").floatValue = 120f;
                toolData.ApplyModifiedPropertiesWithoutUndo();
            }

            RagdollAttackManager2D[] attacks = root.GetComponentsInChildren<RagdollAttackManager2D>(true);
            for (int i = 0; i < attacks.Length; i++)
            {
                RagdollAttackManager2D attack = attacks[i];
                if (attack == null || attack.AttackType == RagdollAttackType.Jelly) continue;
                switch (attack.AttackType)
                {
                    case RagdollAttackType.Bullet:
                        attack.Configure(attack.AttackType, 85f, 0f, 0f, 85f);
                        break;
                    case RagdollAttackType.Hammer:
                    case RagdollAttackType.Lollipop:
                    case RagdollAttackType.CandyStick:
                    case RagdollAttackType.ChocolateBar:
                        attack.Configure(attack.AttackType, 95f, 0f, 0f, 95f);
                        break;
                    case RagdollAttackType.GummyBear:
                        attack.Configure(attack.AttackType, 80f, 0f, 0f, 80f);
                        break;
                    case RagdollAttackType.CandyJar:
                    case RagdollAttackType.CandyProjectile:
                        attack.Configure(attack.AttackType, 70f, 0f, 0f, 70f);
                        break;
                    case RagdollAttackType.Explosion:
                        attack.Configure(attack.AttackType, 150f, 0f, 0f, 150f);
                        break;
                    default:
                        attack.Configure(attack.AttackType, 85f, 0f, 0f, 85f);
                        break;
                }
            }
        }

        private static Material EnsureVisibleVfxMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(ParticleMaterialPath);
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) throw new InvalidOperationException("Sprites/Default shader is unavailable.");
            if (material == null)
            {
                material = new Material(shader) { name = "MAT_Level04_PipeVisible" };
                AssetDatabase.CreateAsset(material, ParticleMaterialPath);
            }
            else material.shader = shader;

            material.color = Color.white;
            material.renderQueue = 3000;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static LevelFourPipeProjectile2D[] CreatePool(Transform parent, string name,
            string spritePath, float scale, float mass, Material visibleVfxMaterial)
        {
            Transform poolRoot = new GameObject(name).transform;
            poolRoot.SetParent(parent, false);
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            LevelFourPipeProjectile2D[] result = new LevelFourPipeProjectile2D[PoolSize];
            for (int i = 0; i < result.Length; i++)
            {
                GameObject go = new GameObject(name + " " + (i + 1).ToString("00"));
                go.transform.SetParent(poolRoot, false);
                go.transform.localScale = Vector3.one * scale;
                SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite; renderer.sortingOrder = 119;
                Rigidbody2D body = go.AddComponent<Rigidbody2D>();
                body.mass = mass; body.gravityScale = .35f; body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                CircleCollider2D collider = go.AddComponent<CircleCollider2D>();
                RagdollAttackManager2D attack = go.AddComponent<RagdollAttackManager2D>();
                LevelFourPipeProjectile2D projectile = go.AddComponent<LevelFourPipeProjectile2D>();
                TrailRenderer trail = go.AddComponent<TrailRenderer>();
                trail.time = name.Contains("Chocolate") ? .48f : .38f;
                trail.startWidth = name.Contains("Chocolate") ? .38f : .28f;
                trail.endWidth = 0f;
                trail.minVertexDistance = .035f;
                trail.sortingOrder = 148;
                trail.textureMode = LineTextureMode.Stretch;
                trail.alignment = LineAlignment.View;
                trail.material = visibleVfxMaterial;
                trail.startColor = name.Contains("Chocolate")
                    ? new Color(1f, .38f, .05f, 1f) : new Color(.35f, .9f, 1f, 1f);
                trail.endColor = new Color(trail.startColor.r, trail.startColor.g, trail.startColor.b, 0f);
                trail.emitting = false;
                ParticleSystem motionTrail = CreateMotionTrail(go.transform,
                    name.Contains("Chocolate"), visibleVfxMaterial);
                SerializedObject data = new SerializedObject(projectile);
                data.FindProperty("body").objectReferenceValue = body;
                data.FindProperty("hitCollider").objectReferenceValue = collider;
                data.FindProperty("spriteRenderer").objectReferenceValue = renderer;
                data.FindProperty("trail").objectReferenceValue = trail;
                data.FindProperty("motionTrail").objectReferenceValue = motionTrail;
                data.FindProperty("attack").objectReferenceValue = attack;
                data.ApplyModifiedPropertiesWithoutUndo();
                result[i] = projectile;
                projectile.Recycle();
            }
            return result;
        }

        private static ParticleSystem CreateMotionTrail(Transform parent, bool bomb, Material material)
        {
            GameObject go = new GameObject(bomb ? "VFX_Bomb_MotionTrail" : "VFX_Soda_MotionTrail",
                typeof(ParticleSystem));
            go.transform.SetParent(parent, false);
            ParticleSystem system = go.GetComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.24f, .42f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.05f, .25f);
            main.startSize = bomb
                ? new ParticleSystem.MinMaxCurve(.18f, .32f)
                : new ParticleSystem.MinMaxCurve(.14f, .25f);
            main.startColor = bomb
                ? new Color(1f, .3f, .025f, .95f)
                : new Color(.34f, .9f, 1f, .9f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.maxParticles = 24;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = bomb ? 34f : 28f;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = bomb ? .16f : .12f;
            ParticleSystem.ColorOverLifetimeModule colors = system.colorOverLifetime;
            colors.enabled = true;
            Gradient fade = new Gradient();
            Color color = bomb ? new Color(1f, .3f, .025f) : new Color(.34f, .9f, 1f);
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(color, .3f),
                    new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(.95f, 0f), new GradientAlphaKey(.7f, .5f),
                    new GradientAlphaKey(0f, 1f) });
            colors.color = fade;
            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, .15f)));
            ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.material = material;
            renderer.sortingOrder = 149;
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return system;
        }

        private static LevelFourPipeVFXController2D CreatePipeVfx(Transform parent,
            Transform bombMuzzle, Transform sodaMuzzle, Material material)
        {
            Transform root = new GameObject("VFX_Level04_PipeAssault").transform;
            root.SetParent(parent, false);
            LevelFourPipeVFXController2D controller = root.gameObject.AddComponent<LevelFourPipeVFXController2D>();

            ParticleSystem bombLaunch = CreateBurst(bombMuzzle, "VFX_Bomb_Muzzle",
                new Color(1f, .48f, .08f, 1f), 12, .34f, 5.5f, .3f, 0f, 22f, .12f, 151, material);
            ParticleSystem sodaLaunch = CreateBurst(sodaMuzzle, "VFX_Soda_Muzzle",
                new Color(.72f, .96f, 1f, 1f), 13, .36f, 5f, .26f, -.1f, 25f, .14f, 151, material);

            ParticleSystem[] bombImpacts = new ParticleSystem[ImpactPoolSize];
            ParticleSystem[] sodaImpacts = new ParticleSystem[ImpactPoolSize];
            Transform bombPool = new GameObject("VFX_Bomb_ImpactPool").transform;
            bombPool.SetParent(root, false);
            Transform sodaPool = new GameObject("VFX_Soda_ImpactPool").transform;
            sodaPool.SetParent(root, false);
            for (int i = 0; i < ImpactPoolSize; i++)
            {
                bombImpacts[i] = CreateBurst(bombPool, "VFX_Bomb_Impact_" + (i + 1).ToString("00"),
                    new Color(1f, .68f, .12f, 1f), 18, .48f, 8.5f, .48f, .7f, 180f, .08f, 155, material);
                CreateBurst(bombImpacts[i].transform, "Chocolate_Droplets",
                    new Color(.42f, .09f, .025f, 1f), 26, .8f, 6.2f, .3f, 2.4f, 180f, .15f, 154, material);
                CreateBurst(bombImpacts[i].transform, "Golden_Sparks",
                    new Color(1f, .9f, .3f, 1f), 22, .44f, 10f, .13f, .2f, 180f, .03f, 156, material);

                sodaImpacts[i] = CreateBurst(sodaPool, "VFX_Soda_Impact_" + (i + 1).ToString("00"),
                    Color.white, 15, .4f, 7f, .36f, -.15f, 170f, .1f, 155, material);
                CreateBurst(sodaImpacts[i].transform, "Blue_Foam",
                    new Color(.22f, .78f, 1f, 1f), 23, .7f, 5.8f, .28f, .5f, 180f, .18f, 154, material);
                CreateBurst(sodaImpacts[i].transform, "Fizz_Bubbles",
                    new Color(.76f, .96f, 1f, .95f), 16, .9f, 4f, .18f, -.45f, 180f, .22f, 153, material);
            }

            SerializedObject data = new SerializedObject(controller);
            data.FindProperty("bombMuzzle").objectReferenceValue = bombLaunch;
            data.FindProperty("sodaMuzzle").objectReferenceValue = sodaLaunch;
            AssignParticleArray(data.FindProperty("bombImpactPool"), bombImpacts);
            AssignParticleArray(data.FindProperty("sodaImpactPool"), sodaImpacts);
            data.ApplyModifiedPropertiesWithoutUndo();
            return controller;
        }

        private static ParticleSystem CreateBurst(Transform parent, string name, Color color, int count,
            float lifetime, float speed, float size, float gravity, float angle, float radius,
            int sortingOrder, Material material)
        {
            GameObject go = new GameObject(name, typeof(ParticleSystem));
            go.transform.SetParent(parent, false);
            ParticleSystem system = go.GetComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * .7f, lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * .55f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * .55f, size);
            main.startColor = color;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.maxParticles = Mathf.Max(24, count + 4);

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = angle;
            shape.radius = radius;
            ParticleSystem.ColorOverLifetimeModule colors = system.colorOverLifetime;
            colors.enabled = true;
            Gradient fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(color.a, 0f), new GradientAlphaKey(color.a, .55f),
                    new GradientAlphaKey(0f, 1f) });
            colors.color = fade;
            ParticleSystem.SizeOverLifetimeModule sizes = system.sizeOverLifetime;
            sizes.enabled = true;
            sizes.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, .45f), new Keyframe(.15f, 1f), new Keyframe(1f, 0f)));
            ParticleSystem.RotationOverLifetimeModule rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-3f, 3f);
            ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.material = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingOrder = sortingOrder;
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return system;
        }

        private static void AssignParticleArray(SerializedProperty property, ParticleSystem[] values)
        {
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        private static void ConfigurePipeController(LevelFourPipeController2D controller, Camera camera,
            RagdollController ragdoll, Collider2D bombCollider, SpriteRenderer bombRenderer,
            Transform bombMuzzle, LevelFourPipeProjectile2D[] bombPool, Collider2D sodaCollider,
            SpriteRenderer sodaRenderer, Transform sodaMuzzle, LevelFourPipeProjectile2D[] sodaPool,
            LevelFourPipeVFXController2D pipeVfx)
        {
            SerializedObject data = new SerializedObject(controller);
            data.FindProperty("inputCamera").objectReferenceValue = camera;
            data.FindProperty("ragdoll").objectReferenceValue = ragdoll;
            data.FindProperty("pipeVfx").objectReferenceValue = pipeVfx;
            ConfigureStream(data.FindProperty("chocolateBombs"), bombCollider, bombRenderer, bombMuzzle, bombPool);
            ConfigureStream(data.FindProperty("sodaCans"), sodaCollider, sodaRenderer, sodaMuzzle, sodaPool);
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureStream(SerializedProperty stream, Collider2D collider,
            SpriteRenderer renderer, Transform muzzle, LevelFourPipeProjectile2D[] pool)
        {
            stream.FindPropertyRelative("buttonCollider").objectReferenceValue = collider;
            stream.FindPropertyRelative("buttonRenderer").objectReferenceValue = renderer;
            stream.FindPropertyRelative("muzzle").objectReferenceValue = muzzle;
            SerializedProperty array = stream.FindPropertyRelative("pool");
            array.arraySize = pool.Length;
            for (int i = 0; i < pool.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = pool[i];
        }

        private static void AppendLevelContent(GameplayLevelSceneController controller, GameObject root,
            RagdollController ragdoll, LevelFourPipeController2D pipes)
        {
            SerializedObject data = new SerializedObject(controller);
            SerializedProperty entries = data.FindProperty("levels");
            int index = -1;
            for (int i = 0; i < entries.arraySize; i++)
            {
                if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("levelId").stringValue == LevelId)
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                index = entries.arraySize;
                entries.arraySize++;
            }

            SerializedProperty entry = entries.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("levelId").stringValue = LevelId;
            entry.FindPropertyRelative("root").objectReferenceValue = root;
            entry.FindPropertyRelative("ragdoll").objectReferenceValue = ragdoll;
            entry.FindPropertyRelative("ragdollInput").objectReferenceValue = ragdoll.GetComponent<RagdollInputManager>();
            entry.FindPropertyRelative("sandboxToolInput").objectReferenceValue = root.GetComponentInChildren<SandboxToolInput2D>(true);
            entry.FindPropertyRelative("candyCannons").objectReferenceValue = null;
            entry.FindPropertyRelative("levelFourPipes").objectReferenceValue = pipes;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static SpriteRenderer CreateSprite(Transform parent, string name, string path,
            Vector3 position, Vector3 scale, int order)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new InvalidOperationException("Missing imported sprite: " + path);
            GameObject go = new GameObject(name, typeof(SpriteRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite; renderer.sortingOrder = order;
            return renderer;
        }

        private static Transform CreatePoint(Transform parent, string name, Vector3 localPosition)
        {
            Transform point = new GameObject(name).transform;
            point.SetParent(parent, false);
            point.localPosition = localPosition;
            return point;
        }

        private static Transform ResolveHead(RagdollController ragdoll)
        {
            for (int i = 0; i < ragdoll.Parts.Count; i++)
                if (ragdoll.Parts[i] != null && ragdoll.Parts[i].PartType == RagdollPartType.Head)
                    return ragdoll.Parts[i].Body.transform;
            return ragdoll.transform;
        }

        private static T FindSceneComponent<T>(Scene scene, bool preferActive = false) where T : Component
        {
            T fallback = null;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                T[] items = roots[i].GetComponentsInChildren<T>(true);
                for (int j = 0; j < items.Length; j++)
                {
                    if (preferActive && items[j].gameObject.activeInHierarchy) return items[j];
                    if (fallback == null) fallback = items[j];
                }
            }
            return fallback;
        }

        public static void ValidateOrThrow()
        {
            LevelDefinition definition = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelAssetPath);
            LevelCatalog catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(CatalogPath);
            if (definition == null || definition.LevelId != LevelId || catalog == null || catalog.IndexOf(LevelId) < 0)
                throw new InvalidOperationException("Level 4 definition/catalog entry is missing.");
            if (!Mathf.Approximately(definition.WallBaseDamage, 120f) ||
                !Mathf.Approximately(definition.WallDamagePerSpeed, 0f) ||
                !Mathf.Approximately(definition.WallMinimumImpactSpeed, 0f) ||
                !Mathf.Approximately(definition.WallMaximumDamage, 120f))
                throw new InvalidOperationException("Level 4 maximum wall-hit damage profile is not configured.");
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameplayLevelSceneController levels = FindSceneComponent<GameplayLevelSceneController>(scene);
            Transform root = levels != null ? levels.transform.Find(LevelRootName) : null;
            LevelFourPipeController2D pipes = root != null ? root.GetComponent<LevelFourPipeController2D>() : null;
            LevelFourPipeVFXController2D vfx = root != null ? root.GetComponentInChildren<LevelFourPipeVFXController2D>(true) : null;
            SandboxToolInput2D tools = root != null ? root.GetComponentInChildren<SandboxToolInput2D>(true) : null;
            LevelFourPipeProjectile2D[] projectiles = root != null
                ? root.GetComponentsInChildren<LevelFourPipeProjectile2D>(true)
                : Array.Empty<LevelFourPipeProjectile2D>();
            bool motionVfxReady = projectiles.Length == PoolSize * 2;
            for (int i = 0; i < projectiles.Length && motionVfxReady; i++)
                motionVfxReady = projectiles[i] != null && projectiles[i].HasMotionVFX;
            SerializedObject pipeData = pipes != null ? new SerializedObject(pipes) : null;
            bool fastDamageReady = pipeData != null &&
                Mathf.Approximately(pipeData.FindProperty("bombInterval").floatValue, .45f) &&
                Mathf.Approximately(pipeData.FindProperty("bombMaximumDamage").floatValue, 150f) &&
                Mathf.Approximately(pipeData.FindProperty("bombBlastDamage").floatValue, 65f) &&
                Mathf.Approximately(pipeData.FindProperty("sodaInterval").floatValue, .3f) &&
                Mathf.Approximately(pipeData.FindProperty("sodaMaximumDamage").floatValue, 75f);
            if (root == null || pipes == null || tools == null || tools.Tools.Count < 12 ||
                !motionVfxReady || !fastDamageReady ||
                vfx == null || vfx.BombImpactPoolSize != ImpactPoolSize || vfx.SodaImpactPoolSize != ImpactPoolSize)
                throw new InvalidOperationException("Level 4 scene references, reused tools, or projectile pools are incomplete.");
            Debug.Log("LEVEL_04_PIPE_ASSAULT_VALIDATION_OK: pooled bomb blast, chocolate debris, soda foam, bubbles, muzzle bursts and projectile trails are configured.");
        }
    }
}
#endif
