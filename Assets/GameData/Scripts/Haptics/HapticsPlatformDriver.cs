using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace KickTheBuddy.Haptics
{
    internal sealed class HapticsPlatformDriver : IHapticsDriver, IDisposable
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaClass plugin;
#endif
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void ATH_Init();
        [DllImport("__Internal")] private static extern bool ATH_IsSupported();
        [DllImport("__Internal")] private static extern void ATH_Impact(int style, float intensity);
        [DllImport("__Internal")] private static extern void ATH_Notification(int type);
        [DllImport("__Internal")] private static extern void ATH_Selection();
        [DllImport("__Internal")] private static extern void ATH_Cancel();
#endif
        public HapticsPlatformDriver()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try { using (AndroidJavaClass player = new AndroidJavaClass("com.unity3d.player.UnityPlayer")) { AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"); plugin = new AndroidJavaClass("com.azeltech.haptics.ATHapticsPlugin"); plugin.CallStatic("initialize", activity); } } catch (Exception exception) { Debug.LogWarning("Android haptics unavailable: " + exception.Message); plugin = null; }
#elif UNITY_IOS && !UNITY_EDITOR
            ATH_Init();
#endif
        }
        public bool IsSupported
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                try { return plugin != null && plugin.CallStatic<bool>("isSupported"); } catch { return false; }
#elif UNITY_IOS && !UNITY_EDITOR
                return ATH_IsSupported();
#else
                return false;
#endif
            }
        }
        public void Impact(HapticImpact impact, float intensity)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Vibrate(Duration(impact), Mathf.RoundToInt(Mathf.Clamp01(intensity) * 255f));
#elif UNITY_IOS && !UNITY_EDITOR
            ATH_Impact((int)impact, Mathf.Clamp01(intensity));
#endif
        }
        public void Notification(HapticNotification notification)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            switch (notification) { case HapticNotification.Success: Pattern(new long[] { 0, 35, 45, 45 }, new int[] { 0, 150, 0, 220 }, -1); break; case HapticNotification.Warning: Pattern(new long[] { 0, 70, 45, 35 }, new int[] { 0, 210, 0, 150 }, -1); break; default: Pattern(new long[] { 0, 90, 45, 110 }, new int[] { 0, 255, 0, 255 }, -1); break; }
#elif UNITY_IOS && !UNITY_EDITOR
            ATH_Notification((int)notification);
#endif
        }
        public void Selection() { Impact(HapticImpact.Soft, .35f); }
        public void Play(in HapticPattern pattern, float intensity)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (pattern.timings == null || pattern.timings.Length == 0) return; int[] amplitudes = Scale(pattern.amplitudes, pattern.timings.Length, intensity); Pattern(pattern.timings, amplitudes, pattern.repeat);
#elif UNITY_IOS && !UNITY_EDITOR
            ATH_Impact((int)pattern.iosFallback, Mathf.Clamp01(intensity));
#endif
        }
        public void Cancel()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try { plugin?.CallStatic("cancel"); } catch { }
#elif UNITY_IOS && !UNITY_EDITOR
            ATH_Cancel();
#endif
        }
#if UNITY_ANDROID && !UNITY_EDITOR
        private void Vibrate(long milliseconds, int amplitude) { try { if (plugin != null) plugin.CallStatic("vibrate", milliseconds, Mathf.Clamp(amplitude, 1, 255)); else Handheld.Vibrate(); } catch { Handheld.Vibrate(); } }
        private void Pattern(long[] timings, int[] amplitudes, int repeat) { try { if (plugin != null) plugin.CallStatic("vibratePattern", timings, amplitudes, repeat); else Handheld.Vibrate(); } catch { Handheld.Vibrate(); } }
#endif
        private static long Duration(HapticImpact impact) { switch (impact) { case HapticImpact.Light: return 22; case HapticImpact.Heavy: return 65; case HapticImpact.Soft: return 15; case HapticImpact.Rigid: return 85; default: return 40; } }
        private static int[] Scale(int[] source, int length, float intensity) { int[] result = new int[length]; for (int i = 0; i < length; i++) { int value = source != null && i < source.Length ? source[i] : 180; result[i] = Mathf.Clamp(Mathf.RoundToInt(value * Mathf.Clamp01(intensity)), 0, 255); } return result; }
        public void Dispose() {
#if UNITY_ANDROID && !UNITY_EDITOR
            plugin?.Dispose(); plugin = null;
#endif
        }
    }
}
