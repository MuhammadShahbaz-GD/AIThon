using System;
using KickTheBuddy.Gameplay;
using UnityEngine;

namespace KickTheBuddy.Audio
{
    [CreateAssetMenu(menuName = "Kick The Buddy/Audio/Audio Catalog", fileName = "Audio Catalog")]
    public sealed class AudioCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Cue
        {
            [SerializeField] private GameSound id;
            [SerializeField] private AudioBus bus = AudioBus.Sfx;
            [SerializeField] private AudioClip[] clips = Array.Empty<AudioClip>();
            [SerializeField] private bool randomize = true, loop;
            [Range(0f, 1f)] [SerializeField] private float volume = 1f;
            [Range(.5f, 1.5f)] [SerializeField] private float minimumPitch = .95f, maximumPitch = 1.05f;
            [Range(0f, 1f)] [SerializeField] private float spatialBlend;
            [Min(0f)] [SerializeField] private float cooldown = .05f;
            [Range(1, 8)] [SerializeField] private int maximumVoices = 3;
            [NonSerialized] private int sequenceIndex;
            public GameSound Id => id; public AudioBus Bus => bus; public bool Loop => loop; public float Volume => volume;
            public float MinimumPitch => minimumPitch; public float MaximumPitch => maximumPitch; public float SpatialBlend => spatialBlend;
            public float Cooldown => cooldown; public int MaximumVoices => maximumVoices;
            public AudioClip NextClip() { if (clips == null || clips.Length == 0) return null; if (randomize) return clips[UnityEngine.Random.Range(0, clips.Length)]; AudioClip clip = clips[sequenceIndex % clips.Length]; sequenceIndex = (sequenceIndex + 1) % clips.Length; return clip; }
        }
        [SerializeField] private AudioClip menuMusic, gameplayMusic;
        [SerializeField] private Cue[] cues = Array.Empty<Cue>();
        public AudioClip MenuMusic => menuMusic; public AudioClip GameplayMusic => gameplayMusic;
        public Cue Find(GameSound id) { for (int i = 0; i < cues.Length; i++) if (cues[i] != null && cues[i].Id == id) return cues[i]; return null; }
    }
}
