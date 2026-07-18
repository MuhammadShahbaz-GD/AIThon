#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using KickTheBuddy.Audio;
using KickTheBuddy.Gameplay;
using KickTheBuddy.Physics;
using KickTheBuddy.Physics.VFX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KickTheBuddy.Editor
{
    /// <summary>Creates, wires, imports, and validates the complete candy-plastic audio pass.</summary>
    public static class CandyPlasticAudioSetupEditor
    {
        private const string AudioRoot = "Assets/GameData/Audios";
        private const string CatalogPath = AudioRoot + "/Candy Plastic Doll Audio Catalog.asset";
        private const string LegacyCatalogPath = AudioRoot + "/Candy Glass Audio Catalog.asset";
        private const string GeneratorPath =
            AudioRoot + "/Generation/generate_candy_plastic_audio.py";
        private const string LegacyGeneratorPath =
            AudioRoot + "/Generation/generate_candy_glass_audio.py";
        private const string MenuMusicPath = AudioRoot + "/Music/Menu_Candy_Factory_Loop.wav";
        private const string GameplayMusicPath = AudioRoot + "/Music/Gameplay_Candy_Action_Loop.wav";

        private static readonly string[] ScenePaths =
        {
            "Assets/GameData/Scene/Splash.unity",
            "Assets/GameData/Scene/MainMenu.unity",
            "Assets/GameData/Scene/CandyLab.unity",
            "Assets/GameData/Scene/RagdollSandbox.unity"
        };

        private sealed class CueDefinition
        {
            public readonly GameSound Id;
            public readonly AudioBus Bus;
            public readonly float Volume;
            public readonly float MinimumPitch;
            public readonly float MaximumPitch;
            public readonly float Cooldown;
            public readonly int MaximumVoices;
            public readonly int Priority;
            public readonly string[] ClipPaths;

            public CueDefinition(
                GameSound id,
                AudioBus bus,
                float volume,
                float minimumPitch,
                float maximumPitch,
                float cooldown,
                int maximumVoices,
                int priority,
                params string[] clipPaths)
            {
                Id = id;
                Bus = bus;
                Volume = volume;
                MinimumPitch = minimumPitch;
                MaximumPitch = maximumPitch;
                Cooldown = cooldown;
                MaximumVoices = maximumVoices;
                Priority = priority;
                ClipPaths = clipPaths;
            }
        }

        [MenuItem("Tools/Gameplay/Audio/Setup Candy-Plastic Doll Audio")]
        private static void SetupFromMenu() => SetupBatch();

        [MenuItem("Tools/Gameplay/Audio/Validate Candy-Plastic Doll Audio")]
        private static void ValidateFromMenu()
        {
            ValidateBatch();
            EditorUtility.DisplayDialog("Candy-Plastic Doll Audio", "Audio catalog and scene wiring are valid.", "OK");
        }

        public static void SetupBatch()
        {
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                MigrateAssetNames();
                ConfigureAudioImporters();
                AudioCatalog catalog = CreateOrUpdateCatalog();
                DeleteLegacyGlassClips();
                WireScenes(catalog);
                AssetDatabase.SaveAssets();
                ValidateBatch();
                Debug.Log("Candy-plastic doll audio setup and validation completed successfully.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        /// <summary>Rebuilds the plastic sound content while preserving existing scene wiring.</summary>
        public static void UpdateCandyPlasticAssetsBatch()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            MigrateAssetNames();
            ConfigureAudioImporters();
            CreateOrUpdateCatalog();
            DeleteLegacyGlassClips();
            AssetDatabase.SaveAssets();
            ValidateBatch();
            Debug.Log("Candy-plastic doll audio assets updated without resaving gameplay scenes.");
        }

        /// <summary>Focused rewire used after level hierarchy changes without resaving legacy scenes.</summary>
        public static void RewirePrimaryGameplayBatch()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AudioCatalog catalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(CatalogPath);
            if (catalog == null) throw new InvalidOperationException("Candy-plastic AudioCatalog is missing.");
            Scene scene = EditorSceneManager.OpenScene(
                "Assets/GameData/Scene/RagdollSandbox.unity", OpenSceneMode.Single);
            SoundManager[] managers = FindAll<SoundManager>();
            for (int i = 0; i < managers.Length; i++)
            {
                SerializedObject serializedManager = new SerializedObject(managers[i]);
                serializedManager.FindProperty("catalog").objectReferenceValue = catalog;
                serializedManager.FindProperty("voicePoolSize").intValue = 12;
                serializedManager.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(managers[i]);
            }
            WireGameplayControllers(managers);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Could not save focused audio wiring.");
            AssetDatabase.SaveAssets();
        }

        public static void ValidateBatch()
        {
            AudioCatalog catalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(CatalogPath);
            if (catalog == null) throw new InvalidOperationException("Candy-plastic AudioCatalog is missing.");
            if (catalog.MenuMusic == null || catalog.GameplayMusic == null)
                throw new InvalidOperationException("Menu or gameplay music is missing from the AudioCatalog.");

            CueDefinition[] definitions = BuildDefinitions();
            for (int i = 0; i < definitions.Length; i++)
            {
                AudioCatalog.Cue cue = catalog.Find(definitions[i].Id);
                if (cue == null)
                    throw new InvalidOperationException($"Audio cue '{definitions[i].Id}' is missing.");
                if (cue.NextClip() == null)
                    throw new InvalidOperationException($"Audio cue '{definitions[i].Id}' has no clip.");
            }

            for (int sceneIndex = 0; sceneIndex < ScenePaths.Length; sceneIndex++)
            {
                Scene scene = EditorSceneManager.OpenScene(ScenePaths[sceneIndex], OpenSceneMode.Single);
                SoundManager[] managers = FindAll<SoundManager>();
                if (managers.Length == 0)
                    throw new InvalidOperationException($"{scene.path} has no SoundManager.");
                for (int i = 0; i < managers.Length; i++)
                {
                    SerializedObject serializedManager = new SerializedObject(managers[i]);
                    if (serializedManager.FindProperty("catalog").objectReferenceValue != catalog)
                        throw new InvalidOperationException($"{scene.path} is not assigned to the candy-plastic catalog.");
                }

                if (!ShouldWireGameplayControllers(scene.path)) continue;
                RagdollController[] ragdolls = FindAll<RagdollController>();
                if (ragdolls.Length == 0)
                    throw new InvalidOperationException($"{scene.path} has no authored ragdoll.");
                for (int i = 0; i < ragdolls.Length; i++)
                {
                    GameplayAudioController audio = ragdolls[i].GetComponent<GameplayAudioController>();
                    if (audio == null)
                        throw new InvalidOperationException(
                            $"{ragdolls[i].name} in {scene.path} has no GameplayAudioController.");
                }
            }
        }

        private static void ConfigureAudioImporters()
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { AudioRoot });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer == null) continue;

                bool music = path.Contains("/Music/");
                importer.forceToMono = !music;
                importer.loadInBackground = music;
                importer.ambisonic = false;

                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                settings.preloadAudioData = true;
                settings.loadType = music
                    ? AudioClipLoadType.CompressedInMemory
                    : AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = music
                    ? AudioCompressionFormat.Vorbis
                    : AudioCompressionFormat.ADPCM;
                settings.quality = music ? .64f : .9f;
                settings.sampleRateSetting = AudioSampleRateSetting.OptimizeSampleRate;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
            }
        }

        private static AudioCatalog CreateOrUpdateCatalog()
        {
            AudioCatalog catalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<AudioCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            SerializedObject serializedCatalog = new SerializedObject(catalog);
            serializedCatalog.FindProperty("menuMusic").objectReferenceValue =
                RequiredClip(MenuMusicPath);
            serializedCatalog.FindProperty("gameplayMusic").objectReferenceValue =
                RequiredClip(GameplayMusicPath);

            CueDefinition[] definitions = BuildDefinitions();
            SerializedProperty cues = serializedCatalog.FindProperty("cues");
            cues.arraySize = definitions.Length;
            for (int index = 0; index < definitions.Length; index++)
            {
                CueDefinition definition = definitions[index];
                SerializedProperty cue = cues.GetArrayElementAtIndex(index);
                cue.FindPropertyRelative("id").enumValueIndex = (int)definition.Id;
                cue.FindPropertyRelative("bus").enumValueIndex = (int)definition.Bus;
                cue.FindPropertyRelative("randomize").boolValue = definition.ClipPaths.Length > 1;
                cue.FindPropertyRelative("loop").boolValue = false;
                cue.FindPropertyRelative("volume").floatValue = definition.Volume;
                cue.FindPropertyRelative("minimumPitch").floatValue = definition.MinimumPitch;
                cue.FindPropertyRelative("maximumPitch").floatValue = definition.MaximumPitch;
                cue.FindPropertyRelative("spatialBlend").floatValue = 0f;
                cue.FindPropertyRelative("cooldown").floatValue = definition.Cooldown;
                cue.FindPropertyRelative("maximumVoices").intValue = definition.MaximumVoices;
                cue.FindPropertyRelative("priority").intValue = definition.Priority;

                SerializedProperty clips = cue.FindPropertyRelative("clips");
                clips.arraySize = definition.ClipPaths.Length;
                for (int clipIndex = 0; clipIndex < definition.ClipPaths.Length; clipIndex++)
                    clips.GetArrayElementAtIndex(clipIndex).objectReferenceValue =
                        RequiredClip(definition.ClipPaths[clipIndex]);
            }
            serializedCatalog.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        private static void WireScenes(AudioCatalog catalog)
        {
            for (int sceneIndex = 0; sceneIndex < ScenePaths.Length; sceneIndex++)
            {
                Scene scene = EditorSceneManager.OpenScene(ScenePaths[sceneIndex], OpenSceneMode.Single);
                SoundManager[] managers = FindAll<SoundManager>();
                for (int i = 0; i < managers.Length; i++)
                {
                    SerializedObject serializedManager = new SerializedObject(managers[i]);
                    serializedManager.FindProperty("catalog").objectReferenceValue = catalog;
                    serializedManager.FindProperty("voicePoolSize").intValue =
                        IsGameplayScene(scene.path) ? 12 : 8;
                    serializedManager.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(managers[i]);
                }

                if (ShouldWireGameplayControllers(scene.path))
                    WireGameplayControllers(managers);

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                    throw new InvalidOperationException($"Could not save audio wiring in {scene.path}.");
            }
        }

        private static void WireGameplayControllers(SoundManager[] managers)
        {
            SoundManager sounds = managers.Length > 0 ? managers[0] : null;
            GameplayManager[] gameplayManagers = FindAll<GameplayManager>();
            GameplayManager gameplay = gameplayManagers.Length > 0 ? gameplayManagers[0] : null;
            RagdollController[] ragdolls = FindAll<RagdollController>();
            SandboxTool2D[] allTools = FindAll<SandboxTool2D>();
            CandyCannonController2D[] allCannons = FindAll<CandyCannonController2D>();
            CandyCannonController2D cannons = allCannons.Length > 0 ? allCannons[0] : null;

            for (int i = 0; i < ragdolls.Length; i++)
            {
                RagdollController ragdoll = ragdolls[i];
                RagdollInputManager input = ragdoll.GetComponent<RagdollInputManager>();
                RagdollAnimationController animation = ragdoll.GetComponent<RagdollAnimationController>();
                CracksModifier[] cracks = ragdoll.GetComponentsInChildren<CracksModifier>(true);

                GameplayAudioController controller =
                    ragdoll.GetComponent<GameplayAudioController>();
                if (controller == null)
                    controller = Undo.AddComponent<GameplayAudioController>(ragdoll.gameObject);
                controller.Configure(sounds, gameplay, ragdoll, input, animation, allTools, cannons, cracks);
                EditorUtility.SetDirty(controller);
            }
        }

        private static CueDefinition[] BuildDefinitions()
        {
            string S(string relative) => AudioRoot + "/" + relative;
            return new[]
            {
                Cue(GameSound.Button, AudioBus.UI, .72f, .96f, 1.04f, .035f, 3, 50,
                    S("SFX/UI/UI_Click_01.wav"), S("SFX/UI/UI_Click_02.wav")),
                Cue(GameSound.Toggle, AudioBus.UI, .72f, .98f, 1.02f, .06f, 2, 55,
                    S("SFX/UI/UI_Toggle.wav")),
                Cue(GameSound.Panel, AudioBus.UI, .68f, .98f, 1.02f, .08f, 2, 55,
                    S("SFX/UI/UI_Panel.wav")),
                Cue(GameSound.Confirm, AudioBus.UI, .78f, .98f, 1.02f, .08f, 2, 65,
                    S("SFX/UI/UI_Confirm.wav")),
                Cue(GameSound.Grab, AudioBus.Sfx, .7f, .94f, 1.06f, .08f, 1, 45,
                    S("SFX/Ragdoll/Ragdoll_Grab.wav")),
                Cue(GameSound.Release, AudioBus.Sfx, .68f, .94f, 1.06f, .08f, 1, 45,
                    S("SFX/Ragdoll/Ragdoll_Release.wav")),
                Cue(GameSound.Stretch, AudioBus.Sfx, .5f, .91f, 1.08f, .24f, 1, 38,
                    S("SFX/Ragdoll/Ragdoll_Stretch.wav")),
                Cue(GameSound.SpringRecoil, AudioBus.Sfx, .62f, .92f, 1.08f, .1f, 2, 48,
                    S("SFX/Ragdoll/Spring_Recoil_01.wav"), S("SFX/Ragdoll/Spring_Recoil_02.wav")),
                Cue(GameSound.CandyRattle, AudioBus.Sfx, .54f, .92f, 1.08f, .11f, 2, 35,
                    S("SFX/Ragdoll/Candy_Rattle_01.wav"), S("SFX/Ragdoll/Candy_Rattle_02.wav")),
                Cue(GameSound.HitLight, AudioBus.Sfx, .62f, .94f, 1.08f, .055f, 3, 42,
                    S("SFX/Impacts/Plastic_Light_01.wav"), S("SFX/Impacts/Plastic_Light_02.wav"),
                    S("SFX/Impacts/Plastic_Light_03.wav")),
                Cue(GameSound.HitMedium, AudioBus.Sfx, .76f, .94f, 1.06f, .065f, 3, 52,
                    S("SFX/Impacts/Plastic_Medium_01.wav"), S("SFX/Impacts/Plastic_Medium_02.wav"),
                    S("SFX/Impacts/Plastic_Medium_03.wav")),
                Cue(GameSound.HitHeavy, AudioBus.Sfx, .9f, .95f, 1.04f, .085f, 3, 68,
                    S("SFX/Impacts/Plastic_Heavy_01.wav"), S("SFX/Impacts/Plastic_Heavy_02.wav"),
                    S("SFX/Impacts/Plastic_Heavy_03.wav")),
                Cue(GameSound.CrackNew, AudioBus.Sfx, .72f, .96f, 1.04f, .12f, 2, 65,
                    S("SFX/Impacts/Plastic_Stress_New.wav")),
                Cue(GameSound.CrackSevere, AudioBus.Sfx, .88f, .95f, 1.03f, .18f, 2, 76,
                    S("SFX/Impacts/Plastic_Stress_Severe.wav")),
                Cue(GameSound.LimbBreak, AudioBus.Sfx, .94f, .96f, 1.03f, .2f, 2, 90,
                    S("SFX/Impacts/Limb_Break_01.wav"), S("SFX/Impacts/Limb_Break_02.wav")),
                Cue(GameSound.SpringDetach, AudioBus.Sfx, .72f, .92f, 1.06f, .18f, 2, 82,
                    S("SFX/Impacts/Spring_Detach.wav")),
                Cue(GameSound.LollipopSwing, AudioBus.Sfx, .58f, .92f, 1.08f, .1f, 2, 38,
                    S("SFX/Tools/Lollipop_Swing.wav")),
                Cue(GameSound.LollipopHit, AudioBus.Sfx, .84f, .94f, 1.05f, .08f, 2, 64,
                    S("SFX/Tools/Lollipop_Hit_01.wav"), S("SFX/Tools/Lollipop_Hit_02.wav")),
                Cue(GameSound.JellyThrow, AudioBus.Sfx, .46f, .92f, 1.08f, .1f, 2, 35,
                    S("SFX/Tools/Jelly_Throw.wav")),
                Cue(GameSound.JellySplat, AudioBus.Sfx, .76f, .92f, 1.06f, .1f, 2, 52,
                    S("SFX/Tools/Jelly_Splat_01.wav"), S("SFX/Tools/Jelly_Splat_02.wav")),
                Cue(GameSound.JellyStick, AudioBus.Sfx, .64f, .96f, 1.04f, .15f, 1, 55,
                    S("SFX/Tools/Jelly_Stick.wav")),
                Cue(GameSound.JellySlide, AudioBus.Sfx, .46f, .94f, 1.05f, .2f, 1, 38,
                    S("SFX/Tools/Jelly_Slide.wav")),
                Cue(GameSound.CannonFire, AudioBus.Sfx, .82f, .96f, 1.04f, .06f, 4, 58,
                    S("SFX/Cannon/Cannon_Fire_01.wav"), S("SFX/Cannon/Cannon_Fire_02.wav"),
                    S("SFX/Cannon/Cannon_Fire_03.wav")),
                Cue(GameSound.CannonChargedFire, AudioBus.Sfx, .96f, .98f, 1.02f, .12f, 2, 78,
                    S("SFX/Cannon/Cannon_Charged.wav")),
                Cue(GameSound.CannonImpact, AudioBus.Sfx, .9f, .96f, 1.04f, .065f, 3, 72,
                    S("SFX/Cannon/Cannon_Impact_01.wav"), S("SFX/Cannon/Cannon_Impact_02.wav")),
                Cue(GameSound.CannonMiss, AudioBus.Sfx, .44f, .92f, 1.07f, .12f, 2, 30,
                    S("SFX/Cannon/Cannon_Miss.wav")),
                Cue(GameSound.Combo, AudioBus.Sfx, .72f, .98f, 1.04f, .12f, 2, 72,
                    S("SFX/Flow/Combo_01.wav"), S("SFX/Flow/Combo_02.wav")),
                Cue(GameSound.ComboHigh, AudioBus.Sfx, .88f, .99f, 1.03f, .16f, 2, 82,
                    S("SFX/Flow/Combo_03.wav")),
                Cue(GameSound.CharacterSmile, AudioBus.Voice, .42f, .97f, 1.04f, 2.5f, 1, 32,
                    S("SFX/Character/Character_Smile.wav")),
                Cue(GameSound.CharacterGasp, AudioBus.Voice, .58f, .96f, 1.05f, .42f, 1, 60,
                    S("SFX/Character/Character_Gasp_01.wav"), S("SFX/Character/Character_Gasp_02.wav")),
                Cue(GameSound.CharacterAnnoyed, AudioBus.Voice, .62f, .96f, 1.04f, .7f, 1, 62,
                    S("SFX/Character/Character_Annoyed.wav")),
                Cue(GameSound.CharacterCry, AudioBus.Voice, .62f, .97f, 1.03f, .85f, 1, 74,
                    S("SFX/Character/Character_Cry.wav")),
                Cue(GameSound.CharacterKO, AudioBus.Voice, .72f, .98f, 1.02f, 1f, 1, 84,
                    S("SFX/Character/Character_KO.wav")),
                Cue(GameSound.CharacterRelief, AudioBus.Voice, .58f, .98f, 1.03f, 1f, 1, 64,
                    S("SFX/Character/Character_Relief.wav")),
                Cue(GameSound.LevelStart, AudioBus.UI, .72f, .99f, 1.01f, .5f, 1, 82,
                    S("SFX/Flow/Level_Start.wav")),
                Cue(GameSound.LevelComplete, AudioBus.UI, .96f, .99f, 1.01f, 1f, 1, 96,
                    S("SFX/Flow/Level_Complete.wav")),
                Cue(GameSound.LevelFailed, AudioBus.UI, .8f, .99f, 1.01f, 1f, 1, 90,
                    S("SFX/Flow/Level_Failed.wav")),
                Cue(GameSound.Coin, AudioBus.UI, .1f, .98f, 1.06f, .04f, 4, 55,
                    S("SFX/Flow/Coin_01.wav"), S("SFX/Flow/Coin_02.wav")),
                Cue(GameSound.ScoreReward, AudioBus.UI, .1f, 1f, 1.08f, .05f, 3, 48,
                    S("SFX/Flow/Coin_01.wav"), S("SFX/Flow/Coin_02.wav")),
                Cue(GameSound.DeathBlast, AudioBus.Sfx, 1f, .99f, 1.01f, 2f, 1, 100,
                    S("SFX/Death/Plastic_Doll_Death_Burst.wav"))
            };
        }

        private static void MigrateAssetNames()
        {
            if (AssetDatabase.LoadAssetAtPath<AudioCatalog>(CatalogPath) == null &&
                AssetDatabase.LoadAssetAtPath<AudioCatalog>(LegacyCatalogPath) != null)
            {
                string error = AssetDatabase.MoveAsset(LegacyCatalogPath, CatalogPath);
                if (!string.IsNullOrEmpty(error))
                    throw new InvalidOperationException("Could not rename the audio catalog: " + error);
            }

            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(GeneratorPath) == null &&
                AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(LegacyGeneratorPath) != null)
            {
                string error = AssetDatabase.MoveAsset(LegacyGeneratorPath, GeneratorPath);
                if (!string.IsNullOrEmpty(error))
                    throw new InvalidOperationException("Could not rename the audio generator: " + error);
            }
        }

        private static void DeleteLegacyGlassClips()
        {
            string[] guids = AssetDatabase.FindAssets(
                "t:AudioClip", new[] { AudioRoot + "/SFX/Impacts", AudioRoot + "/SFX/Death" });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (path.IndexOf("/Glass_", StringComparison.Ordinal) >= 0 ||
                    path.EndsWith("/Character_Death_Blast.wav", StringComparison.Ordinal))
                    AssetDatabase.DeleteAsset(path);
            }
        }

        private static CueDefinition Cue(
            GameSound id,
            AudioBus bus,
            float volume,
            float minimumPitch,
            float maximumPitch,
            float cooldown,
            int maximumVoices,
            int priority,
            params string[] clipPaths) =>
            new CueDefinition(id, bus, volume, minimumPitch, maximumPitch, cooldown,
                maximumVoices, priority, clipPaths);

        private static AudioClip RequiredClip(string path)
        {
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null) throw new InvalidOperationException($"Required audio clip is missing: {path}");
            return clip;
        }

        private static bool IsGameplayScene(string path) =>
            path.EndsWith("/CandyLab.unity", StringComparison.Ordinal) ||
            path.EndsWith("/RagdollSandbox.unity", StringComparison.Ordinal);

        private static bool ShouldWireGameplayControllers(string path) =>
            path.EndsWith("/RagdollSandbox.unity", StringComparison.Ordinal);

        private static T[] FindAll<T>() where T : UnityEngine.Object =>
            UnityEngine.Object.FindObjectsByType<T>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
    }
}
#endif
