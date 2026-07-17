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
        private static readonly long[] CharacterBlastTimings = { 0L, 55L, 28L, 85L, 34L, 130L };
        private static readonly int[] CharacterBlastAmplitudes = { 0, 190, 0, 225, 0, 255 };
        private static readonly long[] ComboTimings = { 0L, 28L, 35L, 55L };
        private static readonly int[] ComboAmplitudes = { 0, 170, 0, 245 };
        private static readonly long[] KnockoutTimings = { 0L, 70L, 42L, 80L };
        private static readonly int[] KnockoutAmplitudes = { 0, 215, 0, 240 };
        private static readonly long[] LevelCompleteTimings = { 0L, 35L, 45L, 45L };
        private static readonly int[] LevelCompleteAmplitudes = { 0, 150, 0, 220 };
        private static readonly long[] LevelFailedTimings = { 0L, 90L, 45L, 110L };
        private static readonly int[] LevelFailedAmplitudes = { 0, 255, 0, 255 };
        public event Action<GameHaptic, float> HapticPlayed; public event Action<bool> EnabledChanged;
        public bool Enabled { get; set; }
        public bool IsSupported => driver != null && driver.IsSupported;
        private void Awake() { Enabled = enabledByDefault; driver = new HapticsPlatformDriver(); }
        public void Configure(bool enabled, float intensity = 1f) { Enabled = enabled; globalIntensity = Mathf.Clamp01(intensity); EnabledChanged?.Invoke(Enabled); if (!Enabled) Cancel(); }
        public bool Play(GameHaptic cue, float intensityScale = 1f)
        {
            if (!Enabled || driver == null) return false; HapticProfile.Cue entry = profile != null ? profile.Find(cue) : null; float now = Time.unscaledTime;
            if (now < nextCueTimes[(int)cue]) return false; float intensity = Mathf.Clamp01((entry != null ? entry.Intensity : DefaultIntensity(cue)) * intensityScale * globalIntensity);
            nextCueTimes[(int)cue] = now + (entry != null ? entry.Cooldown : DefaultCooldown(cue));
            if (entry != null) { if (entry.UsePattern) driver.Play(entry.Pattern, intensity); else driver.Impact(entry.Impact, intensity); }
            else if (TryGetDefaultPattern(cue, out HapticPattern pattern)) driver.Play(pattern, intensity);
            else driver.Impact(DefaultImpact(cue), intensity);
            HapticPlayed?.Invoke(cue, intensity); return true;
        }
        public void Impact(HapticImpact impact, float intensity = 1f) { if (Enabled) driver?.Impact(impact, intensity * globalIntensity); }
        public void Notification(HapticNotification notification) { if (Enabled) driver?.Notification(notification); }
        public void Selection() { if (Enabled) driver?.Selection(); }
        public void Play(in HapticPattern pattern, float intensity = 1f) { if (Enabled) driver?.Play(pattern, intensity * globalIntensity); }
        public void Cancel() { driver?.Cancel(); }
        private static HapticImpact DefaultImpact(GameHaptic cue) { switch (cue) { case GameHaptic.LightHit: case GameHaptic.Selection: return HapticImpact.Light; case GameHaptic.HeavyHit: case GameHaptic.LimbBreak: case GameHaptic.LevelFailed: case GameHaptic.Knockout: return HapticImpact.Heavy; case GameHaptic.Combo: case GameHaptic.CharacterBlast: return HapticImpact.Rigid; default: return HapticImpact.Medium; } }
        private static float DefaultIntensity(GameHaptic cue) { switch (cue) { case GameHaptic.Selection: return .3f; case GameHaptic.LightHit: return .4f; case GameHaptic.HeavyHit: return .8f; case GameHaptic.Combo: return 1f; case GameHaptic.Knockout: return .92f; case GameHaptic.LimbBreak: case GameHaptic.CharacterBlast: return 1f; default: return .75f; } }
        private static float DefaultCooldown(GameHaptic cue) { switch (cue) { case GameHaptic.LightHit: return .035f; case GameHaptic.HeavyHit: return .065f; case GameHaptic.Combo: return .16f; case GameHaptic.Knockout: return .3f; case GameHaptic.LimbBreak: return .2f; case GameHaptic.CharacterBlast: return .75f; default: return .08f; } }
        private static bool TryGetDefaultPattern(GameHaptic cue, out HapticPattern pattern)
        {
            long[] timings;
            int[] amplitudes;
            HapticImpact fallback;
            switch (cue)
            {
                case GameHaptic.Combo:
                    timings = ComboTimings; amplitudes = ComboAmplitudes; fallback = HapticImpact.Rigid; break;
                case GameHaptic.Knockout:
                    timings = KnockoutTimings; amplitudes = KnockoutAmplitudes; fallback = HapticImpact.Heavy; break;
                case GameHaptic.CharacterBlast:
                    timings = CharacterBlastTimings; amplitudes = CharacterBlastAmplitudes; fallback = HapticImpact.Heavy; break;
                case GameHaptic.LevelComplete:
                    timings = LevelCompleteTimings; amplitudes = LevelCompleteAmplitudes; fallback = HapticImpact.Medium; break;
                case GameHaptic.LevelFailed:
                    timings = LevelFailedTimings; amplitudes = LevelFailedAmplitudes; fallback = HapticImpact.Heavy; break;
                default:
                    pattern = default;
                    return false;
            }
            pattern = new HapticPattern { timings = timings, amplitudes = amplitudes, repeat = -1, iosFallback = fallback };
            return true;
        }
        private void OnDestroy() { if (driver is IDisposable disposable) disposable.Dispose(); driver = null; }
    }
}
