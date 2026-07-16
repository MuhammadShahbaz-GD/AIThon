using System;
using UnityEngine;

namespace KickTheBuddy.Haptics
{
    [CreateAssetMenu(menuName = "Kick The Buddy/Haptics/Haptic Profile", fileName = "Haptic Profile")]
    public sealed class HapticProfile : ScriptableObject
    {
        [Serializable]
        public sealed class Cue
        {
            [SerializeField] private GameHaptic cue;
            [Range(0f, 1f)] [SerializeField] private float intensity = 1f;
            [Min(0f)] [SerializeField] private float cooldown = .04f;
            [SerializeField] private HapticImpact impact = HapticImpact.Medium;
            [SerializeField] private bool usePattern;
            [SerializeField] private HapticPattern pattern;
            public GameHaptic Type => cue; public float Intensity => intensity; public float Cooldown => cooldown;
            public HapticImpact Impact => impact; public bool UsePattern => usePattern; public HapticPattern Pattern => pattern;
        }
        [SerializeField] private Cue[] cues = Array.Empty<Cue>();
        public Cue Find(GameHaptic cue) { for (int i = 0; i < cues.Length; i++) if (cues[i] != null && cues[i].Type == cue) return cues[i]; return null; }
    }
}
