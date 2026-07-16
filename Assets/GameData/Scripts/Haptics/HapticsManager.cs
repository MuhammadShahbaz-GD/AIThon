using System;
using UnityEngine;

namespace KickTheBuddy.Haptics
{
    /// <summary>Single responsibility facade that enforces settings, cooldowns, and event reporting.</summary>
    [DisallowMultipleComponent]
    public sealed class HapticsManager : MonoBehaviour, IHapticsService
    {
        [SerializeField] private bool enabledByDefault = true;
        [Range(0f, 1f)] [SerializeField] private float globalIntensity = 1f;
        [SerializeField] private HapticProfile profile;
        private IHapticsDriver driver;
        private readonly float[] nextCueTimes = new float[Enum.GetValues(typeof(GameHaptic)).Length];
        public event Action<GameHaptic, float> HapticPlayed; public event Action<bool> EnabledChanged;
        public bool Enabled { get; set; }
        public bool IsSupported => driver != null && driver.IsSupported;
        private void Awake() { Enabled = enabledByDefault; driver = new HapticsPlatformDriver(); }
        public void Configure(bool enabled, float intensity = 1f) { Enabled = enabled; globalIntensity = Mathf.Clamp01(intensity); EnabledChanged?.Invoke(Enabled); if (!Enabled) Cancel(); }
        public bool Play(GameHaptic cue, float intensityScale = 1f)
        {
            if (!Enabled || driver == null) return false; HapticProfile.Cue entry = profile != null ? profile.Find(cue) : null; float now = Time.unscaledTime;
            if (entry != null && now < nextCueTimes[(int)cue]) return false; float intensity = Mathf.Clamp01((entry != null ? entry.Intensity : DefaultIntensity(cue)) * intensityScale * globalIntensity);
            if (entry != null) { nextCueTimes[(int)cue] = now + entry.Cooldown; if (entry.UsePattern) driver.Play(entry.Pattern, intensity); else driver.Impact(entry.Impact, intensity); }
            else driver.Impact(DefaultImpact(cue), intensity);
            HapticPlayed?.Invoke(cue, intensity); return true;
        }
        public void Impact(HapticImpact impact, float intensity = 1f) { if (Enabled) driver?.Impact(impact, intensity * globalIntensity); }
        public void Notification(HapticNotification notification) { if (Enabled) driver?.Notification(notification); }
        public void Selection() { if (Enabled) driver?.Selection(); }
        public void Play(in HapticPattern pattern, float intensity = 1f) { if (Enabled) driver?.Play(pattern, intensity * globalIntensity); }
        public void Cancel() { driver?.Cancel(); }
        private static HapticImpact DefaultImpact(GameHaptic cue) { switch (cue) { case GameHaptic.LightHit: case GameHaptic.Selection: return HapticImpact.Light; case GameHaptic.HeavyHit: case GameHaptic.LimbBreak: case GameHaptic.LevelFailed: return HapticImpact.Heavy; case GameHaptic.Combo: return HapticImpact.Rigid; default: return HapticImpact.Medium; } }
        private static float DefaultIntensity(GameHaptic cue) { switch (cue) { case GameHaptic.Selection: return .3f; case GameHaptic.LightHit: return .4f; case GameHaptic.HeavyHit: return .8f; case GameHaptic.LimbBreak: return 1f; default: return .75f; } }
        private void OnDestroy() { if (driver is IDisposable disposable) disposable.Dispose(); driver = null; }
    }
}
