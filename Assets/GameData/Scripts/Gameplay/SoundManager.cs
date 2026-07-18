using System;
using System.Collections;
using KickTheBuddy.Audio;
using UnityEngine;
using UnityEngine.Audio;

namespace KickTheBuddy.Gameplay
{
    public enum GameSound
    {
        Button,
        Toggle,
        Panel,
        Confirm,
        Grab,
        Release,
        Stretch,
        SpringRecoil,
        CandyRattle,
        HitLight,
        HitMedium,
        HitHeavy,
        CrackNew,
        CrackSevere,
        LimbBreak,
        SpringDetach,
        LollipopSwing,
        LollipopHit,
        JellyThrow,
        JellySplat,
        JellyStick,
        JellySlide,
        CannonFire,
        CannonChargedFire,
        CannonImpact,
        CannonMiss,
        Combo,
        ComboHigh,
        CharacterSmile,
        CharacterGasp,
        CharacterAnnoyed,
        CharacterCry,
        CharacterKO,
        CharacterRelief,
        LevelStart,
        LevelComplete,
        LevelFailed,
        Coin,
        ScoreReward,
        DeathBlast,
        CandyToolSwing,
        CandyToolHit,
        GummyThrow,
        GummyHit,
        CandyJarHit,
        CandyGunFire,
        CandyGunImpact,
        CharacterOuch,
        CharacterOoo,
        CharacterDontHitMe,
        CharacterMan,
        PipeBombLaunch,
        PipeBombBlast,
        PipeSodaLaunch,
        PipeSodaImpact
    }

    /// <summary>Core game audio facade. Persistent callers use this API while catalogs, pooling, buses, and fades remain internal.</summary>
    [DefaultExecutionOrder(-400)]
    [DisallowMultipleComponent]
    public sealed class SoundManager : MonoBehaviour, IGameAudioService
    {
        [Serializable] private sealed class LegacySoundEntry { public GameSound sound; public AudioClip[] clips = Array.Empty<AudioClip>(); [Range(0f, 1f)] public float volume = 1f; [Range(.5f, 1.5f)] public float minimumPitch = .95f, maximumPitch = 1.05f; [Min(0f)] public float cooldown = .05f; [NonSerialized] public float nextTime; }
        [Header("Catalog")]
        [SerializeField] private AudioCatalog catalog;
        [Tooltip("Backward-compatible fallback until clips are moved into an Audio Catalog.")]
        [SerializeField] private AudioClip menuMusic, gameplayMusic;
        [SerializeField] private LegacySoundEntry[] sounds = Array.Empty<LegacySoundEntry>();
        [Header("Mixer Buses")]
        [SerializeField] private AudioMixerGroup musicGroup, sfxGroup, uiGroup, voiceGroup, ambientGroup;
        [Header("Pooling")]
        [Range(4, 24)] [SerializeField] private int voicePoolSize = 10;
        [SerializeField] private bool persistAcrossScenes;
        [Header("Audibility")]
        [Tooltip("Keeps gameplay SFX clearly audible while still respecting the player's SFX bus volume.")]
        [Range(0f, 1f)] [SerializeField] private float minimumSfxIntensity = .68f;

        private AudioSource musicSource; private AudioSource[] voices; private int[] voicePriorities; private AudioCatalog.Cue[] cueLookup; private int cursor, playbackId;
        private readonly float[] busVolumes = { 1f, 1f, 1f, 1f, 1f };
        private readonly float[] nextCueTimes = new float[Enum.GetValues(typeof(GameSound)).Length];
        private Coroutine musicFade; private float musicVolume = .8f, soundVolume = 1f;

        public event Action<GameSound> SoundPlayed; public event Action<AudioClip> MusicChanged;
        public event Action<AudioPlaybackHandle, GameSound> PlaybackStarted; public event Action<AudioBus, float> BusVolumeChanged;

        private void Awake() { EnsureInitialized(); if (persistAcrossScenes) DontDestroyOnLoad(gameObject); }
        private void EnsureInitialized()
        {
            if (musicSource != null) return;
            GameObject musicObject = new GameObject("Music Source"); musicObject.transform.SetParent(transform, false); musicSource = musicObject.AddComponent<AudioSource>(); musicSource.loop = true; musicSource.playOnAwake = false; musicSource.outputAudioMixerGroup = musicGroup;
            voices = new AudioSource[Mathf.Max(4, voicePoolSize)];
            voicePriorities = new int[voices.Length];
            for (int i = 0; i < voices.Length; i++) { GameObject voice = new GameObject("Voice " + (i + 1)); voice.transform.SetParent(transform, false); voices[i] = voice.AddComponent<AudioSource>(); voices[i].playOnAwake = false; }
            cueLookup = new AudioCatalog.Cue[nextCueTimes.Length];
            if (catalog != null)
                for (int i = 0; i < cueLookup.Length; i++)
                    cueLookup[i] = catalog.Find((GameSound)i);
        }
        public void ApplyVolumes(float music, float sound) { EnsureInitialized(); musicVolume = Mathf.Clamp01(music); soundVolume = Mathf.Clamp01(sound); SetBusVolume(AudioBus.Music, musicVolume); SetBusVolume(AudioBus.Sfx, soundVolume); SetBusVolume(AudioBus.UI, soundVolume); SetBusVolume(AudioBus.Voice, soundVolume); SetBusVolume(AudioBus.Ambient, soundVolume); }
        public void PlayMusic(bool gameplay) { PlayMusic(gameplay, .5f); }
        public void PlayMusic(bool gameplay, float fadeDuration)
        {
            EnsureInitialized(); AudioClip clip = catalog != null ? (gameplay ? catalog.GameplayMusic : catalog.MenuMusic) : (gameplay ? gameplayMusic : menuMusic);
            if (musicSource.clip == clip && musicSource.isPlaying) return; if (musicFade != null) StopCoroutine(musicFade); musicFade = StartCoroutine(ChangeMusic(clip, fadeDuration));
        }
        public void StopMusic(float fadeDuration = .25f) { EnsureInitialized(); if (musicFade != null) StopCoroutine(musicFade); musicFade = StartCoroutine(ChangeMusic(null, fadeDuration)); }
        public void Play(GameSound sound, Vector2 position = default) { PlaySfx(sound, position); }
        public AudioPlaybackHandle PlaySfx(GameSound cue, Vector3 position = default, float intensity = 1f)
        {
            EnsureInitialized();
            bool critical = cue == GameSound.DeathBlast;
            float now = Time.unscaledTime;
            if (!critical && now < nextCueTimes[(int)cue]) return default;
            AudioCatalog.Cue entry = cueLookup != null ? cueLookup[(int)cue] : null; AudioClip clip; float volume, minPitch, maxPitch, spatial; AudioBus bus; float cooldown; int maxVoices, priority;
            if (entry != null) { clip = entry.NextClip(); volume = entry.Volume; minPitch = entry.MinimumPitch; maxPitch = entry.MaximumPitch; spatial = entry.SpatialBlend; bus = entry.Bus; cooldown = entry.Cooldown; maxVoices = entry.MaximumVoices; priority = entry.Priority; }
            else { LegacySoundEntry legacy = FindLegacy(cue); if (legacy == null || legacy.clips == null || legacy.clips.Length == 0) return default; clip = legacy.clips[UnityEngine.Random.Range(0, legacy.clips.Length)]; volume = legacy.volume; minPitch = legacy.minimumPitch; maxPitch = legacy.maximumPitch; spatial = 0f; bus = cue == GameSound.Button ? AudioBus.UI : AudioBus.Sfx; cooldown = legacy.cooldown; maxVoices = 3; priority = 50; }
            if (clip == null || (!critical && CountPlaying(clip) >= maxVoices)) return default;
            AudioSource source = NextVoice(critical ? 100 : priority, critical);
            if (source == null) return default;
            ConfigureVoice(source, bus, position, spatial);
            source.clip = clip;
            float audibleIntensity = bus == AudioBus.Sfx
                ? Mathf.Max(minimumSfxIntensity, Mathf.Clamp01(intensity))
                : Mathf.Clamp01(intensity);
            source.volume = volume * audibleIntensity * busVolumes[(int)bus];
            source.pitch = UnityEngine.Random.Range(minPitch, maxPitch);
            source.loop = entry != null && entry.Loop;
            source.Play();
            nextCueTimes[(int)cue] = now + cooldown;
            AudioPlaybackHandle handle = new AudioPlaybackHandle(++playbackId, source); SoundPlayed?.Invoke(cue); PlaybackStarted?.Invoke(handle, cue); return handle;
        }
        public void SetBusVolume(AudioBus bus, float normalizedVolume) { float value = Mathf.Clamp01(normalizedVolume); busVolumes[(int)bus] = value; if (bus == AudioBus.Music && musicSource != null) musicSource.volume = value; BusVolumeChanged?.Invoke(bus, value); }
        public void PauseAll(bool paused) { EnsureInitialized(); if (paused) { musicSource.Pause(); for (int i = 0; i < voices.Length; i++) voices[i].Pause(); } else { musicSource.UnPause(); for (int i = 0; i < voices.Length; i++) voices[i].UnPause(); } }
        public void StopAllSfx() { EnsureInitialized(); for (int i = 0; i < voices.Length; i++) voices[i].Stop(); }
        private IEnumerator ChangeMusic(AudioClip next, float duration)
        {
            float half = Mathf.Max(0f, duration * .5f); if (musicSource.isPlaying && half > 0f) yield return Fade(musicSource.volume, 0f, half); musicSource.Stop(); musicSource.clip = next;
            if (next != null) { musicSource.volume = half > 0f ? 0f : busVolumes[(int)AudioBus.Music]; musicSource.Play(); if (half > 0f) yield return Fade(0f, busVolumes[(int)AudioBus.Music], half); MusicChanged?.Invoke(next); } musicFade = null;
        }
        private IEnumerator Fade(float from, float to, float duration) { float elapsed = 0f; while (elapsed < duration) { elapsed += Time.unscaledDeltaTime; musicSource.volume = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration)); yield return null; } musicSource.volume = to; }
        private AudioSource NextVoice(int priority, bool forcePreempt = false)
        {
            int lowestPriority = int.MaxValue;
            int lowestIndex = -1;
            for (int i = 0; i < voices.Length; i++)
            {
                int index = (cursor + i) % voices.Length;
                AudioSource voice = voices[index];
                if (!voice.isPlaying)
                {
                    voicePriorities[index] = priority;
                    cursor = (index + 1) % voices.Length;
                    return voice;
                }
                if (voicePriorities[index] < lowestPriority)
                {
                    lowestPriority = voicePriorities[index];
                    lowestIndex = index;
                }
            }
            if (lowestIndex < 0 || (!forcePreempt && lowestPriority > priority)) return null;
            AudioSource fallback = voices[lowestIndex];
            fallback.Stop();
            voicePriorities[lowestIndex] = priority;
            cursor = (lowestIndex + 1) % voices.Length;
            return fallback;
        }
        private void ConfigureVoice(AudioSource source, AudioBus bus, Vector3 position, float spatial) { source.transform.position = position; source.spatialBlend = spatial; source.outputAudioMixerGroup = Group(bus); }
        private AudioMixerGroup Group(AudioBus bus) { switch (bus) { case AudioBus.Music: return musicGroup; case AudioBus.UI: return uiGroup; case AudioBus.Voice: return voiceGroup; case AudioBus.Ambient: return ambientGroup; default: return sfxGroup; } }
        private int CountPlaying(AudioClip clip) { int count = 0; for (int i = 0; i < voices.Length; i++) if (voices[i].isPlaying && voices[i].clip == clip) count++; return count; }
        private LegacySoundEntry FindLegacy(GameSound cue) { for (int i = 0; i < sounds.Length; i++) if (sounds[i] != null && sounds[i].sound == cue) return sounds[i]; return null; }
        private void OnDisable() { if (musicFade != null) StopCoroutine(musicFade); musicFade = null; }
        private void OnValidate() { minimumSfxIntensity = Mathf.Clamp01(minimumSfxIntensity); }
    }
}
