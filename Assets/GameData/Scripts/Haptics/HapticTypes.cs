using System;

namespace KickTheBuddy.Haptics
{
    public enum HapticImpact { Light, Medium, Heavy, Soft, Rigid }
    public enum HapticNotification { Success, Warning, Error }
    public enum GameHaptic { Selection, LightHit, HeavyHit, LimbBreak, Combo, LevelComplete, LevelFailed }

    [Serializable]
    public struct HapticPattern
    {
        public long[] timings;
        public int[] amplitudes;
        public int repeat;
        public HapticImpact iosFallback;
    }

    public interface IHapticsService
    {
        bool Enabled { get; set; }
        bool IsSupported { get; }
        void Impact(HapticImpact impact, float intensity = 1f);
        void Notification(HapticNotification notification);
        void Selection();
        void Play(in HapticPattern pattern, float intensity = 1f);
        void Cancel();
    }

    internal interface IHapticsDriver
    {
        bool IsSupported { get; }
        void Impact(HapticImpact impact, float intensity);
        void Notification(HapticNotification notification);
        void Selection();
        void Play(in HapticPattern pattern, float intensity);
        void Cancel();
    }
}
