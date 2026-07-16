#if UNITY_EDITOR
using KickTheBuddy.Audio;
using KickTheBuddy.Gameplay;
using UnityEditor;
using UnityEngine;

namespace KickTheBuddy.Editor
{
    [CustomEditor(typeof(SoundManager))]
    public sealed class CoreAudioEditor : UnityEditor.Editor
    {
        private bool preview;
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("Core audio facade: catalog lookup, bounded voice pooling, mixer-ready buses, cooldowns, voice limits, deterministic/random variants, and unscaled music fades.", MessageType.Info);
            DrawDefaultInspector();
            preview = EditorGUILayout.BeginFoldoutHeaderGroup(preview, "Runtime Preview");
            if (preview)
            {
                using (new EditorGUI.DisabledScope(!Application.isPlaying))
                {
                    SoundManager manager = (SoundManager)target;
                    if (GUILayout.Button("Play Menu Music")) manager.PlayMusic(false);
                    if (GUILayout.Button("Play Gameplay Music")) manager.PlayMusic(true);
                    if (GUILayout.Button("Play UI Click")) manager.Play(GameSound.Button);
                    if (GUILayout.Button("Stop All SFX")) manager.StopAllSfx();
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }
    }

    public static class CoreAudioSetup
    {
        [MenuItem("Tools/Gameplay/Validate Core Audio")]
        private static void Validate()
        {
            SoundManager manager = Object.FindObjectOfType<SoundManager>();
            if (manager == null) EditorUtility.DisplayDialog("Core Audio", "SoundManager is missing from the active scene.", "OK");
            else EditorUtility.DisplayDialog("Core Audio", "Core SoundManager is installed. Assign an AudioCatalog and optional mixer groups for production content.", "OK");
        }
    }
}
#endif
