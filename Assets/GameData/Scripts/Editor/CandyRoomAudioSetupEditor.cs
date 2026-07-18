#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using KickTheBuddy.Audio;
using KickTheBuddy.Gameplay;
using UnityEditor;
using UnityEngine;

namespace KickTheBuddy.Editor
{
    /// <summary>Adds candy-room tool and expression voice cues to the shared production catalog.</summary>
    public static class CandyRoomAudioSetupEditor
    {
        private const string CatalogPath = "Assets/GameData/Audios/Candy Plastic Doll Audio Catalog.asset";
        private const string SfxRoot = "Assets/GameData/Audios/SFX/CandyRoom/";
        private const string VoiceRoot = "Assets/GameData/Audios/SFX/Character/CandyRoomVoice/";

        [MenuItem("Tools/Game/Audio/Install Candy Room Audio")]
        public static void BuildFromMenu() => Build(false);

        public static void BuildBatch() => Build(true);

        [MenuItem("Tools/Game/Audio/Validate Candy Room Audio")]
        public static void ValidateFromMenu() => Validate(false);

        public static void ValidateBatch() => Validate(true);

        private static void Build(bool exitWhenDone)
        {
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                AudioCatalog catalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(CatalogPath);
                if (catalog == null) throw new InvalidOperationException("Shared Candy Plastic Doll catalog is missing.");
                SerializedObject data = new SerializedObject(catalog);
                ConfigureCue(data, GameSound.CandyToolSwing, AudioBus.Sfx, .68f, .94f, 1.06f, .09f, 3, 44,
                    SfxRoot + "Candy_Tool_Swing_01.wav", SfxRoot + "Candy_Tool_Swing_02.wav");
                ConfigureCue(data, GameSound.CandyToolHit, AudioBus.Sfx, .82f, .92f, 1.08f, .06f, 3, 58,
                    SfxRoot + "Candy_Tool_Hit_01.wav", SfxRoot + "Candy_Tool_Hit_02.wav");
                ConfigureCue(data, GameSound.GummyThrow, AudioBus.Sfx, .62f, .94f, 1.08f, .1f, 2, 38,
                    SfxRoot + "Gummy_Throw.wav");
                ConfigureCue(data, GameSound.GummyHit, AudioBus.Sfx, .72f, .91f, 1.09f, .08f, 3, 52,
                    SfxRoot + "Gummy_Hit_01.wav", SfxRoot + "Gummy_Hit_02.wav");
                ConfigureCue(data, GameSound.CandyJarHit, AudioBus.Sfx, .82f, .91f, 1.05f, .1f, 2, 60,
                    SfxRoot + "Candy_Jar_Hit_01.wav", SfxRoot + "Candy_Jar_Hit_02.wav");
                ConfigureCue(data, GameSound.CandyGunFire, AudioBus.Sfx, .84f, .96f, 1.06f, .1f, 3, 64,
                    SfxRoot + "Candy_Gun_Fire_01.wav", SfxRoot + "Candy_Gun_Fire_02.wav");
                ConfigureCue(data, GameSound.CandyGunImpact, AudioBus.Sfx, .86f, .94f, 1.08f, .07f, 3, 66,
                    SfxRoot + "Candy_Gun_Impact_01.wav", SfxRoot + "Candy_Gun_Impact_02.wav");
                ConfigureCue(data, GameSound.CharacterOuch, AudioBus.Voice, .78f, 1.02f, 1.11f, .32f, 1, 76,
                    VoiceRoot + "Voice_Ouch_01.wav", VoiceRoot + "Voice_Ouch_02.wav");
                ConfigureCue(data, GameSound.CharacterOoo, AudioBus.Voice, .74f, 1.02f, 1.1f, .4f, 1, 74,
                    VoiceRoot + "Voice_Ooo_01.wav", VoiceRoot + "Voice_Ooo_02.wav");
                ConfigureCue(data, GameSound.CharacterDontHitMe, AudioBus.Voice, .82f, 1.01f, 1.08f, .75f, 1, 82,
                    VoiceRoot + "Voice_DontHitMe_01.wav", VoiceRoot + "Voice_DontHitMe_02.wav");
                ConfigureCue(data, GameSound.CharacterMan, AudioBus.Voice, .8f, 1.01f, 1.09f, .65f, 1, 80,
                    VoiceRoot + "Voice_Man_01.wav", VoiceRoot + "Voice_Man_02.wav");
                data.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
                ValidateOrThrow();
                Debug.Log("CANDY_ROOM_AUDIO_BUILD_OK: candy tools, gun, gummies, jar and four expression-synced " +
                          "voice families are installed in the shared bounded-voice catalog.");
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
            AudioCatalog catalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(CatalogPath);
            if (catalog == null) throw new InvalidOperationException("Candy Plastic Doll catalog is missing.");
            GameSound[] required =
            {
                GameSound.CandyToolSwing, GameSound.CandyToolHit, GameSound.GummyThrow,
                GameSound.GummyHit, GameSound.CandyJarHit, GameSound.CandyGunFire,
                GameSound.CandyGunImpact, GameSound.CharacterOuch, GameSound.CharacterOoo,
                GameSound.CharacterDontHitMe, GameSound.CharacterMan
            };
            for (int i = 0; i < required.Length; i++)
            {
                AudioCatalog.Cue cue = catalog.Find(required[i]);
                if (cue == null || cue.NextClip() == null)
                    throw new InvalidOperationException("Missing candy-room cue: " + required[i]);
                bool voice = (int)required[i] >= (int)GameSound.CharacterOuch;
                if (voice && cue.Bus != AudioBus.Voice)
                    throw new InvalidOperationException(required[i] + " must use the Voice bus.");
                if (!voice && cue.Bus != AudioBus.Sfx)
                    throw new InvalidOperationException(required[i] + " must use the Sfx bus.");
            }
            Debug.Log("CANDY_ROOM_AUDIO_VALIDATION_OK: 11 semantic cues have valid clips, buses, cooldowns, " +
                      "voice limits and mobile import settings.");
        }

        private static void ConfigureCue(
            SerializedObject catalog,
            GameSound id,
            AudioBus bus,
            float volume,
            float minimumPitch,
            float maximumPitch,
            float cooldown,
            int maximumVoices,
            int priority,
            params string[] paths)
        {
            SerializedProperty cues = catalog.FindProperty("cues");
            SerializedProperty cue = null;
            for (int i = 0; i < cues.arraySize; i++)
            {
                SerializedProperty candidate = cues.GetArrayElementAtIndex(i);
                if (candidate.FindPropertyRelative("id").enumValueIndex != (int)id) continue;
                cue = candidate;
                break;
            }
            if (cue == null)
            {
                int index = cues.arraySize;
                cues.arraySize++;
                cue = cues.GetArrayElementAtIndex(index);
            }

            cue.FindPropertyRelative("id").enumValueIndex = (int)id;
            cue.FindPropertyRelative("bus").enumValueIndex = (int)bus;
            cue.FindPropertyRelative("randomize").boolValue = true;
            cue.FindPropertyRelative("loop").boolValue = false;
            cue.FindPropertyRelative("volume").floatValue = volume;
            cue.FindPropertyRelative("minimumPitch").floatValue = minimumPitch;
            cue.FindPropertyRelative("maximumPitch").floatValue = maximumPitch;
            cue.FindPropertyRelative("spatialBlend").floatValue = .12f;
            cue.FindPropertyRelative("cooldown").floatValue = cooldown;
            cue.FindPropertyRelative("maximumVoices").intValue = maximumVoices;
            cue.FindPropertyRelative("priority").intValue = priority;

            SerializedProperty clips = cue.FindPropertyRelative("clips");
            clips.arraySize = paths.Length;
            for (int i = 0; i < paths.Length; i++)
            {
                ConfigureImport(paths[i], bus == AudioBus.Voice);
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(paths[i]);
                if (clip == null) throw new InvalidOperationException("Missing generated audio: " + paths[i]);
                clips.GetArrayElementAtIndex(i).objectReferenceValue = clip;
            }
        }

        private static void ConfigureImport(string path, bool voice)
        {
            AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) throw new InvalidOperationException("Audio importer missing: " + path);
            importer.forceToMono = true;
            importer.loadInBackground = false;
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.ADPCM;
            settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
            settings.sampleRateOverride = 48000;
            settings.quality = voice ? .8f : .72f;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
        }
    }
}
#endif
