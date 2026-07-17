using KickTheBuddy.Gameplay;
using KickTheBuddy.Physics;
using UnityEngine;

namespace KickTheBuddy.Haptics
{
    /// <summary>Translates gameplay events into scenario-specific haptic cues.</summary>
    [DisallowMultipleComponent]
    public sealed class GameplayHapticsAdapter : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private HapticsManager haptics;

        [Header("Impact Feedback")]
        [Min(0f)] [SerializeField] private float heavyDamageThreshold = 15f;
        [Min(2)] [SerializeField] private int comboMilestone = 3;
        [Min(3)] [SerializeField] private int comboForMaximumStrength = 12;

        private GameplayManager gameplay;
        private GameSaveManager saves;
        private GameFlowController flow;
        private RagdollController ragdoll;
        private bool characterBlastPlayed;

        private void Start()
        {
            if (haptics == null) haptics = GetComponent<HapticsManager>();
            GameBootstrapper root = GameBootstrapper.Instance;
            if (root == null || haptics == null)
            {
                enabled = false;
                return;
            }

            gameplay = root.Gameplay;
            saves = root.Saves;
            flow = root.Flow;
            haptics.Configure(saves.Data.vibrationEnabled);
            gameplay.LevelCompleted += HandleComplete;
            gameplay.LevelFailed += HandleFailed;
            if (flow != null) flow.GameplayStarted += HandleGameplayStarted;
            BindRagdoll();
        }

        public void SetEnabled(bool value)
        {
            if (saves == null || haptics == null) return;
            saves.Data.vibrationEnabled = value;
            saves.Save();
            haptics.Configure(value);
            if (value) haptics.Play(GameHaptic.Selection);
        }

        public void Selection() => haptics?.Play(GameHaptic.Selection);

        private void HandleGameplayStarted(LevelDefinition level) => BindRagdoll();

        private void BindRagdoll()
        {
            UnbindRagdoll();
            characterBlastPlayed = false;
            ragdoll = FindObjectOfType<RagdollController>();
            if (ragdoll == null) return;
            ragdoll.OnDamageTaken += HandleDamage;
            ragdoll.OnLimbBroken += HandleLimbBreak;
            ragdoll.OnComboAdvanced += HandleCombo;
            ragdoll.OnCharacterKO += HandleKnockout;
            ragdoll.OnCharacterDied += HandleCharacterDied;
            ragdoll.OnCharacterRevived += HandleCharacterRevived;
        }

        private void UnbindRagdoll()
        {
            if (ragdoll == null) return;
            ragdoll.OnDamageTaken -= HandleDamage;
            ragdoll.OnLimbBroken -= HandleLimbBreak;
            ragdoll.OnComboAdvanced -= HandleCombo;
            ragdoll.OnCharacterKO -= HandleKnockout;
            ragdoll.OnCharacterDied -= HandleCharacterDied;
            ragdoll.OnCharacterRevived -= HandleCharacterRevived;
            ragdoll = null;
        }

        private void HandleDamage(float damage, Vector2 point)
        {
            if (haptics == null || ragdoll == null || ragdoll.CurrentHealth <= 0f) return;
            GameHaptic cue = damage >= heavyDamageThreshold ? GameHaptic.HeavyHit : GameHaptic.LightHit;
            haptics.Play(cue, Mathf.Clamp01(damage / 25f));
        }

        private void HandleCombo(int combo, float damage, Vector2 point)
        {
            if (haptics == null || ragdoll == null || ragdoll.CurrentHealth <= 0f || combo < comboMilestone || combo % comboMilestone != 0)
                return;

            float strength = Mathf.Lerp(.9f, 1f,
                Mathf.InverseLerp(comboMilestone, Mathf.Max(comboMilestone + 1, comboForMaximumStrength), combo));
            haptics.Play(GameHaptic.Combo, strength);
        }

        private void HandleLimbBreak(Rigidbody2D body, Vector2 point)
        {
            if (ragdoll != null && ragdoll.CurrentHealth > 0f)
                haptics?.Play(GameHaptic.LimbBreak);
        }

        private void HandleKnockout()
        {
            if (ragdoll != null && ragdoll.CurrentHealth > 0f)
                haptics?.Play(GameHaptic.Knockout);
        }

        private void HandleCharacterDied(Vector2 point)
        {
            if (characterBlastPlayed || haptics == null) return;
            characterBlastPlayed = true;
            haptics.Play(GameHaptic.CharacterBlast);
        }

        private void HandleCharacterRevived() => characterBlastPlayed = false;

        private void HandleComplete(int score, int stars) => haptics?.Play(GameHaptic.LevelComplete);

        private void HandleFailed() => haptics?.Play(GameHaptic.LevelFailed);

        private void OnDestroy()
        {
            if (gameplay != null)
            {
                gameplay.LevelCompleted -= HandleComplete;
                gameplay.LevelFailed -= HandleFailed;
            }
            if (flow != null) flow.GameplayStarted -= HandleGameplayStarted;
            UnbindRagdoll();
        }

        private void OnValidate()
        {
            heavyDamageThreshold = Mathf.Max(0f, heavyDamageThreshold);
            comboMilestone = Mathf.Max(2, comboMilestone);
            comboForMaximumStrength = Mathf.Max(comboMilestone + 1, comboForMaximumStrength);
        }
    }
}
