package com.azeltech.haptics;

import android.content.Context;
import android.os.Build;
import android.os.VibrationEffect;
import android.os.Vibrator;

public final class ATHapticsPlugin {
    private static Vibrator vibrator;
    public static void initialize(Context context) { vibrator = (Vibrator) context.getSystemService(Context.VIBRATOR_SERVICE); }
    public static boolean isSupported() { return vibrator != null && vibrator.hasVibrator(); }
    public static void vibrate(long milliseconds, int amplitude) {
        if (!isSupported()) return;
        if (Build.VERSION.SDK_INT >= 26) vibrator.vibrate(VibrationEffect.createOneShot(milliseconds, Math.max(1, Math.min(255, amplitude))));
        else vibrator.vibrate(milliseconds);
    }
    public static void vibratePattern(long[] timings, int[] amplitudes, int repeat) {
        if (!isSupported() || timings == null || timings.length == 0) return;
        if (Build.VERSION.SDK_INT >= 26 && amplitudes != null && amplitudes.length == timings.length) vibrator.vibrate(VibrationEffect.createWaveform(timings, amplitudes, repeat));
        else vibrator.vibrate(timings, repeat);
    }
    public static void cancel() { if (vibrator != null) vibrator.cancel(); }
}
