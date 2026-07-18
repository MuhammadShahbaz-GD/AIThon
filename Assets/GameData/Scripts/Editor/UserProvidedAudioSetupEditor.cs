#if UNITY_EDITOR
using System;
using KickTheBuddy.Audio;
using KickTheBuddy.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KickTheBuddy.Editor
{
    /// <summary>Maps the supplied production sound pack onto semantic gameplay cues.</summary>
    public static class UserProvidedAudioSetupEditor
    {
        private const string Root = "Assets/GameData/Audios/UserProvided";
        private const string CatalogPath = "Assets/GameData/Audios/Candy Plastic Doll Audio Catalog.asset";
        private const string GameplayScenePath = "Assets/GameData/Scene/RagdollSandbox.unity";

        [MenuItem("Tools/Gameplay/Audio/Apply User-Provided Sound Pack")]
        public static void SetupBatch()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureImporters();
            AudioCatalog catalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(CatalogPath);
            if (catalog == null) throw new InvalidOperationException("The authored AudioCatalog is missing.");

            SerializedObject serialized = new SerializedObject(catalog);
            serialized.FindProperty("gameplayMusic").objectReferenceValue = Clip("Bg Music.ogg");

            SetCue(serialized, GameSound.HitLight, AudioBus.Sfx, .8f, .94f, 1.06f, .05f, 4, 52, "Wall Hit.ogg");
            SetCue(serialized, GameSound.HitMedium, AudioBus.Sfx, .9f, .95f, 1.05f, .06f, 4, 62, "Hit Ball.ogg");
            SetCue(serialized, GameSound.HitHeavy, AudioBus.Sfx, 1f, .96f, 1.03f, .08f, 3, 78, "Hammer hit.ogg");
            SetCue(serialized, GameSound.CrackNew, AudioBus.Sfx, .85f, .97f, 1.03f, .1f, 2, 72, "Glass Cracked.ogg");
            SetCue(serialized, GameSound.CrackSevere, AudioBus.Sfx, 1f, .97f, 1.02f, .16f, 2, 84, "lollipop Crack.ogg");
            SetCue(serialized, GameSound.LollipopHit, AudioBus.Sfx, 1f, .96f, 1.04f, .06f, 3, 72, "lolliPop Hit.ogg");
            SetCue(serialized, GameSound.CandyToolHit, AudioBus.Sfx, 1f, .95f, 1.04f, .07f, 3, 76, "Hammer hit.ogg", "lolliPop Hit.ogg");
            SetCue(serialized, GameSound.GummyHit, AudioBus.Sfx, .92f, .94f, 1.06f, .07f, 3, 66, "Hit Ball.ogg");
            SetCue(serialized, GameSound.CandyJarHit, AudioBus.Sfx, .86f, .96f, 1.04f, .1f, 2, 58, "Balls.ogg");

            SetCue(serialized, GameSound.CandyGunFire, AudioBus.Sfx, .9f, .98f, 1.04f, .045f, 5, 58, "Ball Shoot.ogg");
            SetCue(serialized, GameSound.CandyGunImpact, AudioBus.Sfx, .9f, .96f, 1.04f, .045f, 5, 64, "Hit Ball.ogg");
            SetCue(serialized, GameSound.CannonFire, AudioBus.Sfx, .94f, .96f, 1.02f, .055f, 4, 62, "Ball Shoot.ogg");
            SetCue(serialized, GameSound.CannonChargedFire, AudioBus.Sfx, 1f, .92f, .98f, .07f, 3, 70, "Ball Shoot.ogg");
            SetCue(serialized, GameSound.CannonImpact, AudioBus.Sfx, .94f, .94f, 1.04f, .055f, 4, 68, "Hit Ball.ogg");

            SetCue(serialized, GameSound.PipeBombLaunch, AudioBus.Sfx, .92f, .92f, .99f, .12f, 3, 68, "Ball Shoot.ogg");
            SetCue(serialized, GameSound.PipeBombBlast, AudioBus.Sfx, 1f, .96f, 1.02f, .16f, 3, 96, "Bomb Blast,ogg.ogg");
            SetCue(serialized, GameSound.PipeSodaLaunch, AudioBus.Sfx, .78f, 1.01f, 1.08f, .08f, 4, 56, "Balls.ogg");
            SetCue(serialized, GameSound.PipeSodaImpact, AudioBus.Sfx, .92f, .95f, 1.05f, .07f, 4, 70, "Hit Ball.ogg");

            SetCue(serialized, GameSound.DeathBlast, AudioBus.Sfx, 1f, .97f, 1.02f, .4f, 2, 100, "Bomb Blast,ogg.ogg");
            SetCue(serialized, GameSound.LevelComplete, AudioBus.UI, 1f, 1f, 1f, .5f, 1, 100, "Level Complete.ogg");
            SetCue(serialized, GameSound.Coin, AudioBus.UI, .1f, .98f, 1.04f, .04f, 4, 55, "Balls.ogg");
            SetCue(serialized, GameSound.ScoreReward, AudioBus.UI, .1f, 1f, 1.05f, .05f, 3, 52, "Balls.ogg");

            SetCue(serialized, GameSound.CharacterSmile, AudioBus.Voice, .82f, .98f, 1.02f, .5f, 1, 86, "Cute haha.ogg");
            SetCue(serialized, GameSound.CharacterRelief, AudioBus.Voice, .78f, 1f, 1f, 5f, 1, 82, "Hahaha.ogg");
            SetCue(serialized, GameSound.CharacterGasp, AudioBus.Voice, .92f, .98f, 1.03f, .24f, 1, 90, "Hahh.ogg");
            SetCue(serialized, GameSound.CharacterAnnoyed, AudioBus.Voice, .88f, .98f, 1.02f, .35f, 1, 88, "Jump.ogg");
            SetCue(serialized, GameSound.CharacterCry, AudioBus.Voice, .94f, .98f, 1.02f, .3f, 1, 94, "Full Sad Ahh.ogg");
            SetCue(serialized, GameSound.CharacterKO, AudioBus.Voice, 1f, .94f, .99f, .6f, 1, 98, "Full Sad Ahh.ogg");
            SetCue(serialized, GameSound.CharacterOuch, AudioBus.Voice, .95f, 1f, 1.04f, .22f, 1, 92, "Sad Ahh.ogg");
            SetCue(serialized, GameSound.CharacterOoo, AudioBus.Voice, .92f, .98f, 1.02f, .24f, 1, 91, "Hahh.ogg");
            SetCue(serialized, GameSound.CharacterDontHitMe, AudioBus.Voice, .98f, .98f, 1.02f, .3f, 1, 96, "Full Sad Ahh.ogg");
            SetCue(serialized, GameSound.CharacterMan, AudioBus.Voice, .96f, .98f, 1.02f, .28f, 1, 95, "Sad Ahh.ogg");

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
            WireGameplayScene(catalog);
            AssetDatabase.SaveAssets();
            Validate(catalog);
            Debug.Log("User-provided gameplay audio pack applied and validated successfully.");
        }

        private static void ConfigureImporters()
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { Root });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer == null) continue;
                bool music = path.EndsWith("/Bg Music.ogg", StringComparison.OrdinalIgnoreCase);
                importer.forceToMono = !music;
                importer.loadInBackground = music;
                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                settings.loadType = music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = music ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.ADPCM;
                settings.quality = music ? .68f : 1f;
                settings.sampleRateSetting = AudioSampleRateSetting.OptimizeSampleRate;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
            }
        }

        private static void SetCue(SerializedObject catalog, GameSound id, AudioBus bus,
            float volume, float minPitch, float maxPitch, float cooldown, int voices, int priority,
            params string[] filenames)
        {
            SerializedProperty cues = catalog.FindProperty("cues");
            int index = -1;
            for (int i = 0; i < cues.arraySize; i++)
            {
                if (cues.GetArrayElementAtIndex(i).FindPropertyRelative("id").intValue == (int)id)
                { index = i; break; }
            }
            if (index < 0)
            {
                index = cues.arraySize;
                cues.InsertArrayElementAtIndex(index);
            }
            SerializedProperty cue = cues.GetArrayElementAtIndex(index);
            cue.FindPropertyRelative("id").intValue = (int)id;
            cue.FindPropertyRelative("bus").intValue = (int)bus;
            cue.FindPropertyRelative("randomize").boolValue = filenames.Length > 1;
            cue.FindPropertyRelative("loop").boolValue = false;
            cue.FindPropertyRelative("volume").floatValue = volume;
            cue.FindPropertyRelative("minimumPitch").floatValue = minPitch;
            cue.FindPropertyRelative("maximumPitch").floatValue = maxPitch;
            cue.FindPropertyRelative("spatialBlend").floatValue = 0f;
            cue.FindPropertyRelative("cooldown").floatValue = cooldown;
            cue.FindPropertyRelative("maximumVoices").intValue = voices;
            cue.FindPropertyRelative("priority").intValue = priority;
            SerializedProperty clips = cue.FindPropertyRelative("clips");
            clips.arraySize = filenames.Length;
            for (int i = 0; i < filenames.Length; i++) clips.GetArrayElementAtIndex(i).objectReferenceValue = Clip(filenames[i]);
        }

        private static AudioClip Clip(string filename)
        {
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "/" + filename);
            if (clip == null) throw new InvalidOperationException("Missing supplied audio clip: " + filename);
            return clip;
        }

        private static void WireGameplayScene(AudioCatalog catalog)
        {
            Scene scene = EditorSceneManager.OpenScene(GameplayScenePath, OpenSceneMode.Single);
            SoundManager[] managers = UnityEngine.Object.FindObjectsByType<SoundManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < managers.Length; i++)
            {
                SerializedObject manager = new SerializedObject(managers[i]);
                manager.FindProperty("catalog").objectReferenceValue = catalog;
                manager.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(managers[i]);
            }
            LevelFourPipeController2D[] pipes = UnityEngine.Object.FindObjectsByType<LevelFourPipeController2D>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            GameplayAudioController[] controllers = UnityEngine.Object.FindObjectsByType<GameplayAudioController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < controllers.Length; i++)
            {
                SerializedObject controller = new SerializedObject(controllers[i]);
                SerializedProperty property = controller.FindProperty("levelFourPipes");
                property.arraySize = pipes.Length;
                for (int p = 0; p < pipes.Length; p++) property.GetArrayElementAtIndex(p).objectReferenceValue = pipes[p];
                controller.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(controllers[i]);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save gameplay audio wiring.");
        }

        private static void Validate(AudioCatalog catalog)
        {
            if (catalog.GameplayMusic != Clip("Bg Music.ogg")) throw new InvalidOperationException("Gameplay music was not assigned.");
            GameSound[] required = { GameSound.HitLight, GameSound.HitMedium, GameSound.HitHeavy,
                GameSound.CrackNew, GameSound.CrackSevere, GameSound.CandyGunFire, GameSound.CannonFire,
                GameSound.DeathBlast, GameSound.LevelComplete, GameSound.CharacterOuch, GameSound.CharacterOoo,
                GameSound.CharacterGasp, GameSound.CharacterCry, GameSound.PipeBombLaunch,
                GameSound.PipeBombBlast, GameSound.PipeSodaLaunch, GameSound.PipeSodaImpact };
            for (int i = 0; i < required.Length; i++)
                if (catalog.Find(required[i]) == null || catalog.Find(required[i]).NextClip() == null)
                    throw new InvalidOperationException("Missing configured audio cue: " + required[i]);
        }
    }
}
#endif
