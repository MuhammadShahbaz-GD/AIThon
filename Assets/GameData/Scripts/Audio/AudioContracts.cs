using System;
using UnityEngine;

namespace KickTheBuddy.Audio
{
    public enum AudioBus { Music, Sfx, UI, Voice, Ambient }
    public readonly struct AudioPlaybackHandle
    {
        public readonly int Id; public readonly AudioSource Source;
        public bool IsValid => Source != null; public bool IsPlaying => Source != null && Source.isPlaying;
        public AudioPlaybackHandle(int id, AudioSource source) { Id = id; Source = source; }
    }
    public interface IGameAudioService
    {
        AudioPlaybackHandle PlaySfx(KickTheBuddy.Gameplay.GameSound cue, Vector3 position = default, float intensity = 1f);
        void PlayMusic(bool gameplay, float fadeDuration = .5f);
        void StopMusic(float fadeDuration = .25f);
        void SetBusVolume(AudioBus bus, float normalizedVolume);
        void PauseAll(bool paused);
        void StopAllSfx();
    }
}
